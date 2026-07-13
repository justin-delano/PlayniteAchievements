using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.UI;

namespace PlayniteAchievements.Services.Recording
{
    /// <summary>
    /// Records unlock video clips via a user-supplied ffmpeg. While a game runs, ffmpeg captures
    /// the game's monitor into a rolling buffer of short MPEG-TS segments under the plugin's user
    /// data path; on each own-unlock a clip covering BOTH the unlock moment and the toast
    /// appearing on screen is trimmed out of the buffer (see <see cref="SegmentTimeline"/>).
    /// Subscribes to <see cref="PlayniteAchievementsPlugin.AchievementUnlocked"/> in parallel to
    /// the toast service, and to <see cref="ToastNotificationService.WaveDisplayed"/> for the
    /// clip end anchor. Per-unlock failures are silent-but-logged; configuration failures
    /// (invalid ffmpeg, low disk, repeated capture crashes) raise one notification per session.
    /// </summary>
    internal sealed class UnlockRecordingService : IDisposable
    {
        /// <summary>Rolling capture segment length in seconds (K).</summary>
        internal const int SegmentSeconds = 5;

        private const string BufferRootFolderName = "RecordingBuffer";
        private const long MinFreeBytesToStart = 2L * 1024 * 1024 * 1024;
        private const long MinFreeBytesToContinue = 500L * 1024 * 1024;
        private const long MaxBufferBytes = 2L * 1024 * 1024 * 1024;
        private const int WindowResolveTimeoutSeconds = 60;
        private const int WindowResolvePollMs = 2000;
        private const int ToastWaitTimeoutSeconds = 30;
        private const int MaxCaptureRestarts = 3;
        private const int RestartBackoffSeconds = 5;
        private const int PruneIntervalSeconds = 30;
        private const int StopGraceSeconds = 3;
        private const int DrainTimeoutSeconds = 45;
        private const string UnavailableNotificationId = "PlayAch-RecordingUnavailable";

        private readonly IPlayniteAPI _api;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;
        private readonly string _pluginUserDataPath;
        private readonly Func<int?> _getRunningGameProcessId;
        private readonly Func<string, bool> _isProviderRecordingEnabled;
        private readonly ToastNotificationService _toastNotifications;
        private readonly UnlockScreenshotService _screenshotService;
        private readonly FfmpegValidationService _validation;

        private readonly object _gate = new object();
        private readonly List<ClipRequest> _pending = new List<ClipRequest>();
        private readonly Dictionary<string, Task<string>> _inFlightByWindow =
            new Dictionary<string, Task<string>>(StringComparer.Ordinal);
        private readonly HashSet<Task> _inFlightTasks = new HashSet<Task>();

        private CaptureSession _session;
        private WindowsJobObject _jobObject;
        private bool _sessionNotified;
        private bool _disposed;

        public UnlockRecordingService(
            IPlayniteAPI api,
            PlayniteAchievementsSettings settings,
            ILogger logger,
            string pluginUserDataPath,
            Func<int?> getRunningGameProcessId,
            ToastNotificationService toastNotifications = null,
            Func<string, bool> isProviderRecordingEnabled = null)
        {
            _api = api;
            _settings = settings;
            _logger = logger;
            _pluginUserDataPath = pluginUserDataPath;
            _getRunningGameProcessId = getRunningGameProcessId;
            _toastNotifications = toastNotifications;
            _isProviderRecordingEnabled = isProviderRecordingEnabled;
            _screenshotService = new UnlockScreenshotService(logger);
            _validation = new FfmpegValidationService(logger);

            PlayniteAchievementsPlugin.AchievementUnlocked += OnAchievementUnlocked;
            if (_toastNotifications != null)
            {
                _toastNotifications.WaveDisplayed += OnToastWaveDisplayed;
            }
        }

        private sealed class CaptureSession
        {
            public string BufferDirectory;
            public string FfmpegPath;
            public string CaptureArguments;
            public string GameName;
            public DateTime CaptureStartUtc;
            public FfmpegProcessHost CaptureHost;
            public CancellationTokenSource Cts;
            public Timer PruneTimer;
            public int RestartCount;
            public volatile bool Stopping;
        }

        private sealed class ClipRequest
        {
            public CaptureSession Session;
            public string ProviderKey;
            public string GameName;
            public string AchievementName;
            public int AchievementNumber;
            public int TotalCount;
            public DateTime? UnlockTimeUtc;
            public DateTime DetectionUtc;
        }

        // === Session lifecycle ===

        public void OnGameStarted(Playnite.SDK.Models.Game game)
        {
            if (_disposed)
            {
                return;
            }

            _sessionNotified = false;
            OnGameStopped();

            var persisted = _settings?.Persisted;
            if (persisted?.EnableUnlockRecordings != true)
            {
                return;
            }

            var ffmpegPath = persisted.FfmpegPath?.Trim();
            var outputDir = ResolveOutputDirectory(persisted);
            if (string.IsNullOrEmpty(ffmpegPath) || !SafeFileExists(ffmpegPath) || string.IsNullOrWhiteSpace(outputDir))
            {
                _logger?.Warn("[Recording] Unlock recordings are enabled but the ffmpeg path or output folder is missing/invalid; skipping this session.");
                NotifyRecordingUnavailableOnce();
                return;
            }

            var bufferRoot = Path.Combine(_pluginUserDataPath, BufferRootFolderName);
            CleanupStaleBufferDirectories(bufferRoot);

            if (!HasFreeSpace(bufferRoot, MinFreeBytesToStart))
            {
                _logger?.Warn("[Recording] Less than 2 GB free on the buffer drive; skipping this session.");
                NotifyRecordingUnavailableOnce();
                return;
            }

            var session = new CaptureSession
            {
                BufferDirectory = Path.Combine(
                    bufferRoot,
                    DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)),
                FfmpegPath = ffmpegPath,
                GameName = game?.Name,
                Cts = new CancellationTokenSource()
            };

            lock (_gate)
            {
                _session = session;
            }

            _ = Task.Run(() => StartCaptureWhenWindowResolvesAsync(session));
        }

        public void OnGameStopped()
        {
            CaptureSession session;
            lock (_gate)
            {
                session = _session;
                _session = null;
            }

            if (session == null)
            {
                return;
            }

            session.Stopping = true;
            try
            {
                session.Cts.Cancel();
            }
            catch
            {
            }

            session.PruneTimer?.Dispose();
            session.PruneTimer = null;
            _ = Task.Run(() => ShutdownSessionAsync(session));
        }

        /// <summary>
        /// Waits (2s polls, up to 60s) for the game window to become resolvable so the capture is
        /// scoped to the game's monitor, then spawns the rolling ffmpeg capture. Monitor capture
        /// (not window) is deliberate: ffmpeg can't follow a moving window.
        /// </summary>
        private async Task StartCaptureWhenWindowResolvesAsync(CaptureSession session)
        {
            try
            {
                var token = session.Cts.Token;
                var deadline = DateTime.UtcNow.AddSeconds(WindowResolveTimeoutSeconds);
                System.Drawing.Rectangle? bounds = null;
                while (!token.IsCancellationRequested)
                {
                    var processId = _getRunningGameProcessId?.Invoke();
                    // Give the started process time to open its main window before falling back
                    // to whatever window is in the foreground (probably Playnite's monitor).
                    var stillLaunching = processId.HasValue &&
                                         !ProcessHasMainWindow(processId.Value) &&
                                         DateTime.UtcNow < deadline;
                    if (!stillLaunching)
                    {
                        bounds = _screenshotService.TryGetGameMonitorBounds(processId);
                        if (bounds.HasValue || DateTime.UtcNow >= deadline)
                        {
                            break;
                        }
                    }

                    await Task.Delay(WindowResolvePollMs, token).ConfigureAwait(false);
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (!bounds.HasValue)
                {
                    _logger?.Warn("[Recording] No game window/monitor resolved within 60s; recording skipped for this session.");
                    return;
                }

                var persisted = _settings?.Persisted;
                if (persisted == null)
                {
                    return;
                }

                var encoderArgs = await ResolveEncoderArgumentsAsync(session.FfmpegPath, persisted.RecordingEncoder)
                    .ConfigureAwait(false);
                var backend = persisted.RecordingCaptureBackend == RecordingCaptureBackend.Ddagrab
                    ? RecordingCaptureBackend.Ddagrab
                    : RecordingCaptureBackend.Gdigrab;

                session.CaptureArguments = RecordingCommandBuilder.BuildCaptureArguments(
                    new RecordingCommandBuilder.CaptureOptions
                    {
                        Backend = backend,
                        Fps = persisted.RecordingFps,
                        MonitorX = bounds.Value.X,
                        MonitorY = bounds.Value.Y,
                        MonitorWidth = bounds.Value.Width,
                        MonitorHeight = bounds.Value.Height,
                        MonitorIndex = ResolveMonitorIndex(bounds.Value),
                        Resolution = persisted.RecordingResolution,
                        EncoderArguments = encoderArgs,
                        SegmentSeconds = SegmentSeconds,
                        BufferDirectory = session.BufferDirectory
                    });

                Directory.CreateDirectory(session.BufferDirectory);
                if (session.Stopping || !SpawnCapture(session))
                {
                    return;
                }

                session.PruneTimer = new Timer(
                    _ => PruneTick(session),
                    null,
                    TimeSpan.FromSeconds(PruneIntervalSeconds),
                    TimeSpan.FromSeconds(PruneIntervalSeconds));
                _logger?.Info(
                    $"[Recording] Capture started for '{session.GameName}' on monitor {bounds.Value} ({backend}, {encoderArgs}), buffer={session.BufferDirectory}.");
            }
            catch (OperationCanceledException)
            {
                // Game stopped while waiting for the window.
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[Recording] Failed to start capture session.");
            }
        }

        private bool SpawnCapture(CaptureSession session)
        {
            var host = new FfmpegProcessHost(session.FfmpegPath, session.CaptureArguments, _logger);
            host.Exited += (s, e) => OnCaptureExited(session, host);
            if (!host.Start(EnsureJobObject()))
            {
                host.Dispose();
                _logger?.Warn("[Recording] ffmpeg capture process failed to start.");
                return false;
            }

            session.CaptureStartUtc = DateTime.UtcNow;
            session.CaptureHost = host;
            return true;
        }

        /// <summary>
        /// Capture crash recovery: up to 3 restarts with a 5s backoff, then the session is
        /// disabled with one notification. The stderr tail is logged for diagnosis.
        /// </summary>
        private void OnCaptureExited(CaptureSession session, FfmpegProcessHost host)
        {
            if (_disposed || session.Stopping || !ReferenceEquals(session.CaptureHost, host))
            {
                return;
            }

            var tail = host.StdErrTail;
            session.RestartCount++;
            if (session.RestartCount > MaxCaptureRestarts)
            {
                _logger?.Warn(
                    $"[Recording] ffmpeg capture exited {session.RestartCount} times; disabling recording for this session. stderr tail:\n{tail}");
                session.Stopping = true;
                NotifyRecordingUnavailableOnce();
                return;
            }

            _logger?.Warn(
                $"[Recording] ffmpeg capture exited unexpectedly (exit={host.ExitCode}); restart {session.RestartCount}/{MaxCaptureRestarts} in {RestartBackoffSeconds}s. stderr tail:\n{tail}");
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(RestartBackoffSeconds), session.Cts.Token).ConfigureAwait(false);
                    if (!_disposed && !session.Stopping)
                    {
                        host.Dispose();
                        SpawnCapture(session);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, "[Recording] Capture restart failed.");
                }
            });
        }

        private async Task ShutdownSessionAsync(CaptureSession session)
        {
            try
            {
                var host = session.CaptureHost;
                if (host != null)
                {
                    await host.StopGracefullyAsync(TimeSpan.FromSeconds(StopGraceSeconds)).ConfigureAwait(false);
                }

                // Toasts queued for this session were just cleared; produce any still-pending
                // clips with the no-toast end anchor before the buffer goes away.
                List<ClipRequest> pending;
                lock (_gate)
                {
                    pending = _pending.Where(r => ReferenceEquals(r.Session, session)).ToList();
                    _pending.RemoveAll(r => ReferenceEquals(r.Session, session));
                }

                foreach (var request in pending)
                {
                    _logger?.Debug($"[Recording] Game stopped before a toast for '{request.AchievementName}'; using the detection-anchored clip end.");
                    StartClipProduction(request, toastShownUtc: null);
                }

                Task[] inFlight;
                lock (_gate)
                {
                    inFlight = _inFlightTasks.ToArray();
                }

                if (inFlight.Length > 0)
                {
                    await Task.WhenAny(Task.WhenAll(inFlight), Task.Delay(TimeSpan.FromSeconds(DrainTimeoutSeconds)))
                        .ConfigureAwait(false);
                }

                host?.Dispose();
                session.CaptureHost = null;
                lock (_gate)
                {
                    _inFlightByWindow.Clear();
                }

                TryDeleteDirectory(session.BufferDirectory);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[Recording] Session shutdown failed.");
            }
        }

        // === Unlock handling ===

        private void OnAchievementUnlocked(object sender, AchievementUnlockedEventArgs e)
        {
            if (_disposed || e == null || e.IsPreview || e.IsFriendUnlock)
            {
                return;
            }

            if (_settings?.Persisted?.EnableUnlockRecordings != true)
            {
                return;
            }

            CaptureSession session;
            lock (_gate)
            {
                session = _session;
            }

            if (session == null || session.Stopping || session.CaptureHost == null || session.CaptureHost.HasExited)
            {
                return;
            }

            if (_isProviderRecordingEnabled?.Invoke(e.ProviderKey) == false)
            {
                return;
            }

            var request = new ClipRequest
            {
                Session = session,
                ProviderKey = e.ProviderKey,
                GameName = e.GameName,
                AchievementName = e.DisplayName,
                AchievementNumber = e.AchievementNumber,
                TotalCount = e.TotalCount,
                UnlockTimeUtc = e.UnlockTimeUtc,
                DetectionUtc = DateTime.UtcNow
            };

            lock (_gate)
            {
                _pending.Add(request);
            }

            _ = Task.Run(() => ToastWaitFallbackAsync(request));
        }

        /// <summary>
        /// End-anchor fallback: when no toast arrives within 30s of detection (toasts disabled
        /// for the provider, or queue starvation) the clip is produced anchored on detection.
        /// </summary>
        private async Task ToastWaitFallbackAsync(ClipRequest request)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(ToastWaitTimeoutSeconds), request.Session.Cts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Session shut down; ShutdownSessionAsync already drained pending requests.
            }

            bool stillPending;
            lock (_gate)
            {
                stillPending = _pending.Remove(request);
            }

            if (!stillPending)
            {
                return;
            }

            _logger?.Debug(
                $"[Recording] No toast within {ToastWaitTimeoutSeconds}s for '{request.AchievementName}'; using the detection-anchored clip end.");
            StartClipProduction(request, toastShownUtc: null);
        }

        private void OnToastWaveDisplayed(object sender, ToastWaveDisplayedEventArgs e)
        {
            if (_disposed || e?.Wave == null || e.Wave.Count == 0)
            {
                return;
            }

            var matches = new List<ClipRequest>();
            lock (_gate)
            {
                foreach (var vm in e.Wave)
                {
                    var match = _pending.FirstOrDefault(r =>
                        string.Equals(r.ProviderKey, vm.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.AchievementName, vm.AchievementName, StringComparison.Ordinal));
                    if (match != null)
                    {
                        _pending.Remove(match);
                        matches.Add(match);
                    }
                }
            }

            foreach (var request in matches)
            {
                StartClipProduction(request, e.ShownUtc);
            }
        }

        // === Clip production ===

        private void StartClipProduction(ClipRequest request, DateTime? toastShownUtc)
        {
            var task = Task.Run(() => ProduceClipAsync(request, toastShownUtc));
            lock (_gate)
            {
                _inFlightTasks.Add(task);
            }

            task.ContinueWith(
                t =>
                {
                    lock (_gate)
                    {
                        _inFlightTasks.Remove(t);
                    }
                },
                TaskContinuationOptions.ExecuteSynchronously);
        }

        private async Task ProduceClipAsync(ClipRequest request, DateTime? toastShownUtc)
        {
            try
            {
                var session = request.Session;
                var persisted = _settings?.Persisted;
                if (persisted == null)
                {
                    return;
                }

                var pollInterval = Math.Max(10, persisted.InGamePollIntervalSeconds);
                var (windowStart, windowEnd) = SegmentTimeline.ComputeClipWindow(
                    request.UnlockTimeUtc,
                    request.DetectionUtc,
                    toastShownUtc,
                    session.CaptureStartUtc,
                    oldestSegmentStartUtc: null,
                    pollIntervalSeconds: pollInterval,
                    targetClipSeconds: persisted.RecordingClipSeconds);

                if ((windowEnd - windowStart).TotalSeconds < SegmentTimeline.MinimumWindowSeconds)
                {
                    _logger?.Debug(
                        $"[Recording] Clip window for '{request.AchievementName}' collapsed below {SegmentTimeline.MinimumWindowSeconds}s; skipping.");
                    return;
                }

                var outputPath = BuildOutputPath(persisted, request);
                if (outputPath == null)
                {
                    return;
                }

                // One encode per distinct clip window: a burst of unlocks in one wave shares one
                // ffmpeg run and the duplicates copy the finished file.
                var key = BuildWindowKey(session, windowStart, windowEnd);
                Task<string> producer = null;
                TaskCompletionSource<string> owner = null;
                lock (_gate)
                {
                    if (!_inFlightByWindow.TryGetValue(key, out producer))
                    {
                        owner = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                        _inFlightByWindow[key] = owner.Task;
                    }
                }

                if (owner == null)
                {
                    var producedPath = await producer.ConfigureAwait(false);
                    if (producedPath != null && SafeFileExists(producedPath))
                    {
                        File.Copy(producedPath, outputPath);
                        _logger?.Info($"[Recording] Saved unlock clip (shared window copy): {outputPath}");
                    }

                    return;
                }

                string result = null;
                try
                {
                    result = await EncodeClipAsync(session, request, toastShownUtc, windowStart, windowEnd, outputPath)
                        .ConfigureAwait(false);
                }
                finally
                {
                    owner.TrySetResult(result);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[Recording] Clip production failed for '{request?.AchievementName}'.");
            }
        }

        private async Task<string> EncodeClipAsync(
            CaptureSession session,
            ClipRequest request,
            DateTime? toastShownUtc,
            DateTime windowStart,
            DateTime windowEnd,
            string outputPath)
        {
            // Wait until the segment covering the window end has closed (K + margin) so the
            // concat never reads a half-written segment.
            var readyAtUtc = windowEnd.AddSeconds(SegmentSeconds + 2);
            var wait = readyAtUtc - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait).ConfigureAwait(false);
            }

            var segments = SegmentTimeline.ParseSegments(ListSegments(session.BufferDirectory), TimeZoneInfo.Local);
            var plan = SegmentTimeline.PlanClip(segments, windowStart, windowEnd, SegmentSeconds);
            if (plan == null)
            {
                _logger?.Debug($"[Recording] No buffered segments overlap the clip window for '{request.AchievementName}'; skipping.");
                return null;
            }

            LogRecordingTiming(session, request, toastShownUtc, windowStart, windowEnd, plan.Segments.Count);

            var listPath = Path.Combine(session.BufferDirectory, $"clip_{Guid.NewGuid():N}.txt");
            var tempPath = Path.Combine(session.BufferDirectory, $"clip_{Guid.NewGuid():N}.mp4");
            try
            {
                File.WriteAllText(
                    listPath,
                    RecordingCommandBuilder.BuildConcatListContent(plan.Segments.Select(s => s.Path)));

                // Stream-copy first (near-zero CPU while the game runs); one re-encode retry when
                // the copy fails or produces an empty file.
                var ok = await RunTrimAsync(session, listPath, plan, tempPath, reencode: false).ConfigureAwait(false);
                if (!ok)
                {
                    TryDeleteFile(tempPath);
                    ok = await RunTrimAsync(session, listPath, plan, tempPath, reencode: true).ConfigureAwait(false);
                }

                if (!ok)
                {
                    _logger?.Warn($"[Recording] Clip export failed for '{request.AchievementName}' (copy and re-encode).");
                    return null;
                }

                File.Move(tempPath, outputPath);
                _logger?.Info($"[Recording] Saved unlock clip: {outputPath}");
                return outputPath;
            }
            finally
            {
                TryDeleteFile(listPath);
                TryDeleteFile(tempPath);
            }
        }

        private async Task<bool> RunTrimAsync(
            CaptureSession session,
            string listPath,
            SegmentTimeline.ClipPlan plan,
            string tempPath,
            bool reencode)
        {
            var arguments = RecordingCommandBuilder.BuildTrimArguments(
                listPath,
                plan.StartOffsetSeconds,
                plan.DurationSeconds,
                tempPath,
                reencode);
            using (var host = new FfmpegProcessHost(session.FfmpegPath, arguments, _logger))
            {
                if (!host.Start(EnsureJobObject()))
                {
                    return false;
                }

                var timeout = TimeSpan.FromSeconds(Math.Max(30, plan.DurationSeconds) + 60);
                var exitCode = await host.WaitForExitAsync(timeout).ConfigureAwait(false);
                if (exitCode != 0)
                {
                    _logger?.Debug($"[Recording] ffmpeg trim exited with {exitCode} (reencode={reencode}): {host.StdErrTail}");
                    return false;
                }
            }

            try
            {
                var info = new FileInfo(tempPath);
                return info.Exists && info.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The per-clip timing line (Info) that makes refresh-latency-driven clip stretching
        /// visible in the plugin log.
        /// </summary>
        private void LogRecordingTiming(
            CaptureSession session,
            ClipRequest request,
            DateTime? toastShownUtc,
            DateTime windowStart,
            DateTime windowEnd,
            int segmentCount)
        {
            try
            {
                var precise = SegmentTimeline.IsPreciseUnlockTime(
                    request.UnlockTimeUtc, session.CaptureStartUtc, request.DetectionUtc);
                var unlockText = precise ? Stamp(request.UnlockTimeUtc.Value) : "coarse";
                var unlockToDetect = precise
                    ? (request.DetectionUtc - request.UnlockTimeUtc.Value).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)
                    : "?";
                var toastText = toastShownUtc.HasValue ? Stamp(toastShownUtc.Value) : "none";
                var detectToToast = toastShownUtc.HasValue
                    ? (toastShownUtc.Value - request.DetectionUtc).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)
                    : "?";
                _logger?.Info(
                    $"[RecordingTiming] unlock={unlockText} detected={Stamp(request.DetectionUtc)} " +
                    $"(unlock→detect {unlockToDetect}s) toastShown={toastText} (detect→toast {detectToToast}s) " +
                    $"window=[{Stamp(windowStart)}..{Stamp(windowEnd)}] ({(windowEnd - windowStart).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s) " +
                    $"segments={segmentCount}");

                // Verification: the toast (plus tail) must sit inside the clip window.
                if (toastShownUtc.HasValue && toastShownUtc.Value.AddSeconds(SegmentTimeline.TailSeconds) > windowEnd)
                {
                    _logger?.Debug("[RecordingTiming] toast tail extends past the window end; the toast may be cut off in the clip.");
                }
            }
            catch
            {
            }
        }

        private static string Stamp(DateTime utc)
        {
            return utc.ToString("HH:mm:ss.f", CultureInfo.InvariantCulture);
        }

        private static string BuildWindowKey(CaptureSession session, DateTime start, DateTime end)
        {
            // Rounded to 2s so a burst of unlocks detected milliseconds apart shares one encode.
            const long twoSeconds = 2 * TimeSpan.TicksPerSecond;
            var s = (long)Math.Round(start.Ticks / (double)twoSeconds);
            var e = (long)Math.Round(end.Ticks / (double)twoSeconds);
            return $"{session.BufferDirectory}|{s}|{e}";
        }

        private string BuildOutputPath(PersistedSettings persisted, ClipRequest request)
        {
            try
            {
                var baseDir = ResolveOutputDirectory(persisted);
                if (string.IsNullOrWhiteSpace(baseDir))
                {
                    return null;
                }

                var relative = UnlockScreenshotService.BuildRelativePath(
                    request.ProviderKey,
                    request.GameName,
                    request.AchievementName,
                    request.AchievementNumber,
                    request.TotalCount,
                    variantSuffix: null,
                    extension: ".mp4");
                var folder = Path.Combine(baseDir, relative.Folder);
                Directory.CreateDirectory(folder);
                return UnlockScreenshotService.EnsureUniquePath(Path.Combine(folder, relative.FileName));
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[Recording] Failed to build clip output path.");
                return null;
            }
        }

        // === Buffer maintenance ===

        /// <summary>
        /// Every 30s: prunes segments beyond the rolling depth/byte cap and stops the capture
        /// when the buffer drive drops below 500 MB free.
        /// </summary>
        private void PruneTick(CaptureSession session)
        {
            if (_disposed || session.Stopping)
            {
                return;
            }

            try
            {
                var persisted = _settings?.Persisted;
                var pollInterval = Math.Max(10, persisted?.InGamePollIntervalSeconds ?? 15);
                var segments = SegmentTimeline.ParseSegments(ListSegments(session.BufferDirectory), TimeZoneInfo.Local);
                var prunable = SegmentTimeline.SelectPrunable(
                    segments,
                    pollInterval,
                    persisted?.RecordingClipSeconds ?? 15,
                    SegmentSeconds,
                    MaxBufferBytes);
                foreach (var segment in prunable)
                {
                    TryDeleteFile(segment.Path);
                }

                if (!HasFreeSpace(session.BufferDirectory, MinFreeBytesToContinue))
                {
                    _logger?.Warn("[Recording] Less than 500 MB free on the buffer drive; stopping the capture for this session.");
                    session.Stopping = true;
                    session.PruneTimer?.Dispose();
                    session.PruneTimer = null;
                    NotifyRecordingUnavailableOnce();
                    var host = session.CaptureHost;
                    if (host != null)
                    {
                        _ = Task.Run(() => host.StopGracefullyAsync(TimeSpan.FromSeconds(StopGraceSeconds)));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[Recording] Prune tick failed.");
            }
        }

        private static IEnumerable<(string Path, long SizeBytes)> ListSegments(string bufferDirectory)
        {
            var result = new List<(string, long)>();
            try
            {
                if (!Directory.Exists(bufferDirectory))
                {
                    return result;
                }

                foreach (var file in Directory.GetFiles(
                             bufferDirectory,
                             RecordingCommandBuilder.SegmentFilePrefix + "*" + RecordingCommandBuilder.SegmentFileExtension))
                {
                    long size = 0;
                    try
                    {
                        size = new FileInfo(file).Length;
                    }
                    catch
                    {
                    }

                    result.Add((file, size));
                }
            }
            catch
            {
            }

            return result;
        }

        /// <summary>Deletes leftover buffer directories from crashed sessions at game start.</summary>
        private void CleanupStaleBufferDirectories(string bufferRoot)
        {
            try
            {
                if (!Directory.Exists(bufferRoot))
                {
                    return;
                }

                foreach (var directory in Directory.GetDirectories(bufferRoot))
                {
                    _logger?.Debug($"[Recording] Removing stale recording buffer: {directory}");
                    TryDeleteDirectory(directory);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[Recording] Stale buffer cleanup failed.");
            }
        }

        // === Helpers ===

        private async Task<string> ResolveEncoderArgumentsAsync(string ffmpegPath, RecordingEncoder encoder)
        {
            if (encoder != RecordingEncoder.Auto)
            {
                return RecordingCommandBuilder.BuildEncoderArguments(encoder, null);
            }

            try
            {
                var result = await _validation.ValidateAsync(ffmpegPath).ConfigureAwait(false);
                return RecordingCommandBuilder.BuildEncoderArguments(RecordingEncoder.Auto, result?.AvailableEncoders);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[Recording] Encoder probe failed; defaulting to libx264.");
                return RecordingCommandBuilder.BuildEncoderArguments(RecordingEncoder.Auto, null);
            }
        }

        private static string ResolveOutputDirectory(PersistedSettings persisted)
        {
            var directory = persisted?.UnlockRecordingDirectory;
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = persisted?.UnlockScreenshotDirectory;
            }

            return string.IsNullOrWhiteSpace(directory) ? null : directory.Trim();
        }

        private WindowsJobObject EnsureJobObject()
        {
            lock (_gate)
            {
                return _jobObject ?? (_jobObject = new WindowsJobObject());
            }
        }

        private static bool ProcessHasMainWindow(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    return process.MainWindowHandle != IntPtr.Zero;
                }
            }
            catch
            {
                return false;
            }
        }

        private static int ResolveMonitorIndex(System.Drawing.Rectangle monitorBounds)
        {
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                for (var i = 0; i < screens.Length; i++)
                {
                    if (screens[i].Bounds == monitorBounds)
                    {
                        return i;
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        private bool HasFreeSpace(string path, long minimumBytes)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root))
                {
                    return true;
                }

                return new DriveInfo(root).AvailableFreeSpace >= minimumBytes;
            }
            catch (Exception ex)
            {
                // Unknown drives (UNC quirks) fail open: recording is best-effort.
                _logger?.Debug(ex, "[Recording] Free-space check failed.");
                return true;
            }
        }

        private void NotifyRecordingUnavailableOnce()
        {
            if (_sessionNotified)
            {
                return;
            }

            _sessionNotified = true;
            try
            {
                var title = ResourceProvider.GetString("LOCPlayAch_Title_PluginName") ?? "Playnite Achievements";
                var message = ResourceProvider.GetString("LOCPlayAch_Notification_RecordingUnavailable");
                _api?.Notifications?.Add(new NotificationMessage(
                    UnavailableNotificationId,
                    $"{title}\n{message}",
                    NotificationType.Error));
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[Recording] Failed to show the recording-unavailable notification.");
            }
        }

        private static bool SafeFileExists(string path)
        {
            try
            {
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            PlayniteAchievementsPlugin.AchievementUnlocked -= OnAchievementUnlocked;
            if (_toastNotifications != null)
            {
                _toastNotifications.WaveDisplayed -= OnToastWaveDisplayed;
            }

            CaptureSession session;
            lock (_gate)
            {
                session = _session;
                _session = null;
                _pending.Clear();
                _inFlightByWindow.Clear();
            }

            if (session != null)
            {
                session.Stopping = true;
                try
                {
                    session.Cts.Cancel();
                }
                catch
                {
                }

                session.PruneTimer?.Dispose();
                session.CaptureHost?.Dispose();
            }

            // Closing the job object kills any ffmpeg process that somehow survived disposal.
            _jobObject?.Dispose();
            _jobObject = null;
        }
    }
}

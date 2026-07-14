using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace PlayniteAchievements.Services.UI
{
    internal sealed class StableForegroundGameChangedEventArgs : EventArgs
    {
        public StableForegroundGameChangedEventArgs(Game game)
        {
            Game = game;
        }

        public Game Game { get; }
    }

    /// <summary>
    /// Maps the foreground window to a running Playnite game so screenshots and video capture
    /// follow the game the user is actually playing. Event-driven: a WinEvent hook (installed on
    /// the WPF dispatcher thread, which already pumps messages) fires only on real foreground
    /// changes, so there is no steady-state polling. Classification of an unseen process id
    /// (started-pid match, executable path under the game's install directory, bounded
    /// parent-process walk) runs once per pid and is cached until the tracked set changes.
    ///
    /// A foreground change to a different game is debounced: the switch is only reported after
    /// the same game has stayed foreground for <see cref="StableSwitchDelayMs"/>, so alt-tabbing
    /// never thrashes consumers that restart an ffmpeg capture on switch.
    /// </summary>
    internal sealed class ActiveGameWindowTracker : IDisposable
    {
        private const int StableSwitchDelayMs = 5000;
        private const int MaxParentChainDepth = 10;

        private sealed class TrackedGame
        {
            public Game Game;
            public int? StartedProcessId;
            public int? LearnedProcessId;
            public IntPtr LearnedHwnd;
            public string NormalizedInstallDirectory;
        }

        private readonly ILogger _logger;
        private readonly object _sync = new object();
        private readonly Dictionary<Guid, TrackedGame> _tracked = new Dictionary<Guid, TrackedGame>();
        // Foreground pid -> owning game id (null: classified as not a tracked game). Cleared
        // whenever the tracked set changes so stale attributions never outlive a session.
        private readonly Dictionary<int, Guid?> _pidGameCache = new Dictionary<int, Guid?>();

        private Dispatcher _hookDispatcher;
        private IntPtr _hook;
        // Kept in a field so the unmanaged hook can't call into a collected delegate.
        private WinEventDelegate _hookDelegate;
        private Timer _stableTimer;
        private Guid? _foregroundGameId;
        private Guid? _stableForegroundGameId;
        private Guid? _pendingStableGameId;
        private bool _disposed;

        public ActiveGameWindowTracker(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Raised (on a thread-pool thread) after the foreground has settled on a different
        /// tracked game for <see cref="StableSwitchDelayMs"/>.
        /// </summary>
        public event EventHandler<StableForegroundGameChangedEventArgs> StableForegroundGameChanged;

        /// <summary>The tracked game the current foreground window belongs to, if any.</summary>
        public Guid? ForegroundGameId
        {
            get
            {
                lock (_sync)
                {
                    return _foregroundGameId;
                }
            }
        }

        /// <summary>The debounced foreground game. Does not decay when no game is foreground.</summary>
        public Guid? StableForegroundGameId
        {
            get
            {
                lock (_sync)
                {
                    return _stableForegroundGameId;
                }
            }
        }

        public void OnGameStarted(Game game, int? startedProcessId)
        {
            if (_disposed || game == null || game.Id == Guid.Empty)
            {
                return;
            }

            lock (_sync)
            {
                _tracked[game.Id] = new TrackedGame
                {
                    Game = game,
                    StartedProcessId = startedProcessId,
                    NormalizedInstallDirectory = NormalizeDirectory(game.InstallDirectory)
                };
                _pidGameCache.Clear();

                // A game that just started is what the user is about to play; seed the stable
                // owner so consumers don't wait a full debounce for the obvious answer.
                _foregroundGameId = game.Id;
                _stableForegroundGameId = game.Id;
                _pendingStableGameId = null;
                _stableTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }

            EnsureHookInstalled();
        }

        public void OnGameStopped(Guid gameId)
        {
            if (gameId == Guid.Empty)
            {
                return;
            }

            var trackedRemaining = true;
            lock (_sync)
            {
                if (!_tracked.Remove(gameId))
                {
                    return;
                }

                _pidGameCache.Clear();
                if (_foregroundGameId == gameId)
                {
                    _foregroundGameId = null;
                }

                if (_stableForegroundGameId == gameId)
                {
                    _stableForegroundGameId = null;
                }

                if (_pendingStableGameId == gameId)
                {
                    _pendingStableGameId = null;
                    _stableTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                }

                trackedRemaining = _tracked.Count > 0;
            }

            if (!trackedRemaining)
            {
                RemoveHook();
            }
        }

        /// <summary>
        /// Best window handle for a tracked game: the foreground-learned handle when still valid
        /// (correct for launcher-wrapped titles whose started process is a dead bootstrapper),
        /// otherwise the started process's main window. IntPtr.Zero when neither resolves.
        /// </summary>
        public IntPtr TryGetWindowHandle(Guid gameId)
        {
            int? pid;
            lock (_sync)
            {
                if (!_tracked.TryGetValue(gameId, out var tracked))
                {
                    return IntPtr.Zero;
                }

                if (tracked.LearnedHwnd != IntPtr.Zero && IsWindow(tracked.LearnedHwnd))
                {
                    return tracked.LearnedHwnd;
                }

                tracked.LearnedHwnd = IntPtr.Zero;
                pid = tracked.LearnedProcessId ?? tracked.StartedProcessId;
            }

            if (!pid.HasValue || pid.Value <= 0)
            {
                return IntPtr.Zero;
            }

            try
            {
                using (var process = Process.GetProcessById(pid.Value))
                {
                    return process.MainWindowHandle;
                }
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Best process id for a tracked game: the foreground-learned pid when available (the
        /// process that actually owns the game window), else the started pid.
        /// </summary>
        public int? TryGetProcessId(Guid gameId)
        {
            lock (_sync)
            {
                return _tracked.TryGetValue(gameId, out var tracked)
                    ? tracked.LearnedProcessId ?? tracked.StartedProcessId
                    : null;
            }
        }

        // === Foreground hook ===

        private void EnsureHookInstalled()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                InstallHookOnDispatcher(dispatcher);
            }
            else
            {
                dispatcher.BeginInvoke(new Action(() => InstallHookOnDispatcher(dispatcher)));
            }
        }

        private void InstallHookOnDispatcher(Dispatcher dispatcher)
        {
            lock (_sync)
            {
                if (_disposed || _hook != IntPtr.Zero || _tracked.Count == 0)
                {
                    return;
                }

                _hookDispatcher = dispatcher;
                _hookDelegate = OnForegroundWinEvent;
                _hook = SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND,
                    EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _hookDelegate,
                    0,
                    0,
                    WINEVENT_OUTOFCONTEXT);
                if (_hook == IntPtr.Zero)
                {
                    _logger?.Debug("[WindowTracker] SetWinEventHook failed; foreground tracking disabled.");
                    _hookDelegate = null;
                }
            }
        }

        private void RemoveHook()
        {
            Dispatcher dispatcher;
            lock (_sync)
            {
                dispatcher = _hookDispatcher;
            }

            if (dispatcher == null)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                RemoveHookOnDispatcher();
            }
            else
            {
                dispatcher.BeginInvoke(new Action(RemoveHookOnDispatcher));
            }
        }

        private void RemoveHookOnDispatcher()
        {
            lock (_sync)
            {
                if (_hook != IntPtr.Zero)
                {
                    try
                    {
                        UnhookWinEvent(_hook);
                    }
                    catch
                    {
                    }

                    _hook = IntPtr.Zero;
                    _hookDelegate = null;
                }
            }
        }

        private void OnForegroundWinEvent(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            try
            {
                if (_disposed || hwnd == IntPtr.Zero)
                {
                    return;
                }

                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == 0)
                {
                    return;
                }

                var gameId = ClassifyProcess((int)pid);
                lock (_sync)
                {
                    _foregroundGameId = gameId;
                    if (!gameId.HasValue)
                    {
                        // Non-game foreground (Playnite, browser, ...): the stable owner does not
                        // decay, and any pending switch is abandoned.
                        if (_pendingStableGameId.HasValue)
                        {
                            _pendingStableGameId = null;
                            _stableTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                        }

                        return;
                    }

                    if (_tracked.TryGetValue(gameId.Value, out var tracked))
                    {
                        tracked.LearnedHwnd = hwnd;
                        tracked.LearnedProcessId = (int)pid;
                    }

                    if (gameId == _stableForegroundGameId)
                    {
                        if (_pendingStableGameId.HasValue)
                        {
                            _pendingStableGameId = null;
                            _stableTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                        }

                        return;
                    }

                    if (_pendingStableGameId == gameId)
                    {
                        return;
                    }

                    _pendingStableGameId = gameId;
                    if (_stableTimer == null)
                    {
                        _stableTimer = new Timer(OnStableTimer, null, StableSwitchDelayMs, Timeout.Infinite);
                    }
                    else
                    {
                        _stableTimer.Change(StableSwitchDelayMs, Timeout.Infinite);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[WindowTracker] Foreground event handling failed.");
            }
        }

        private void OnStableTimer(object state)
        {
            try
            {
                Game switchedTo = null;
                lock (_sync)
                {
                    if (_disposed || !_pendingStableGameId.HasValue)
                    {
                        return;
                    }

                    var candidate = _pendingStableGameId.Value;
                    _pendingStableGameId = null;

                    // One re-check at fire time: the switch only stands if the candidate's game is
                    // still the foreground classification.
                    GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
                    if (pid == 0 || ClassifyProcessLocked((int)pid) != candidate)
                    {
                        return;
                    }

                    if (!_tracked.TryGetValue(candidate, out var tracked))
                    {
                        return;
                    }

                    _stableForegroundGameId = candidate;
                    switchedTo = tracked.Game;
                }

                if (switchedTo != null)
                {
                    _logger?.Info($"[WindowTracker] Stable foreground game: {switchedTo.Name}.");
                    StableForegroundGameChanged?.Invoke(
                        this,
                        new StableForegroundGameChangedEventArgs(switchedTo));
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[WindowTracker] Stable switch handling failed.");
            }
        }

        // === pid -> game classification ===

        private Guid? ClassifyProcess(int pid)
        {
            lock (_sync)
            {
                return ClassifyProcessLocked(pid);
            }
        }

        private Guid? ClassifyProcessLocked(int pid)
        {
            if (_pidGameCache.TryGetValue(pid, out var cached))
            {
                return cached;
            }

            var result = ClassifyProcessCore(pid);
            _pidGameCache[pid] = result;
            return result;
        }

        private Guid? ClassifyProcessCore(int pid)
        {
            // 1. Direct pid match against started or previously learned pids (direct-exe games
            //    and emulators, where Playnite started the process itself).
            foreach (var entry in _tracked)
            {
                if (entry.Value.StartedProcessId == pid || entry.Value.LearnedProcessId == pid)
                {
                    return entry.Key;
                }
            }

            // 2. Executable path under a tracked game's install directory (launcher-wrapped
            //    titles whose started process is a dead bootstrapper).
            var exePath = TryGetProcessImagePath(pid);
            if (!string.IsNullOrEmpty(exePath))
            {
                foreach (var entry in _tracked)
                {
                    var installDir = entry.Value.NormalizedInstallDirectory;
                    if (!string.IsNullOrEmpty(installDir) &&
                        exePath.StartsWith(installDir, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger?.Debug(
                            $"[WindowTracker] pid {pid} classified as '{entry.Value.Game?.Name}' via install directory.");
                        return entry.Key;
                    }
                }
            }

            // 3. Parent chain up to a tracked started pid (launcher alive with the game exe
            //    outside the install directory).
            var ancestorGame = ClassifyByParentChain(pid);
            if (ancestorGame.HasValue)
            {
                return ancestorGame;
            }

            return null;
        }

        private Guid? ClassifyByParentChain(int pid)
        {
            var startedPids = new Dictionary<int, Guid>();
            foreach (var entry in _tracked)
            {
                if (entry.Value.StartedProcessId is int startedPid && startedPid > 0)
                {
                    startedPids[startedPid] = entry.Key;
                }
            }

            if (startedPids.Count == 0)
            {
                return null;
            }

            var parentByPid = SnapshotParentMap();
            if (parentByPid == null)
            {
                return null;
            }

            var current = pid;
            for (var depth = 0; depth < MaxParentChainDepth; depth++)
            {
                if (!parentByPid.TryGetValue(current, out var parent) || parent <= 0 || parent == current)
                {
                    return null;
                }

                if (startedPids.TryGetValue(parent, out var gameId))
                {
                    // The snapshot contains the child, so the ancestor pid is still a live process
                    // in the same snapshot; a reused pid would not appear as this chain's parent.
                    _logger?.Debug($"[WindowTracker] pid {pid} classified via parent chain (ancestor {parent}).");
                    return gameId;
                }

                current = parent;
            }

            return null;
        }

        private static string TryGetProcessImagePath(int pid)
        {
            var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var builder = new StringBuilder(1024);
                var size = (uint)builder.Capacity;
                return QueryFullProcessImageName(handle, 0, builder, ref size)
                    ? builder.ToString()
                    : null;
            }
            catch
            {
                return null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private Dictionary<int, int> SnapshotParentMap()
        {
            var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == IntPtr.Zero || snapshot == INVALID_HANDLE_VALUE)
            {
                return null;
            }

            try
            {
                var map = new Dictionary<int, int>();
                var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                if (!Process32First(snapshot, ref entry))
                {
                    return null;
                }

                do
                {
                    map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
                }
                while (Process32Next(snapshot, ref entry));

                return map;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[WindowTracker] Process snapshot failed.");
                return null;
            }
            finally
            {
                CloseHandle(snapshot);
            }
        }

        private static string NormalizeDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            try
            {
                var full = Path.GetFullPath(directory.Trim());
                return full.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? full
                    : full + Path.DirectorySeparatorChar;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            RemoveHook();
            lock (_sync)
            {
                _stableTimer?.Dispose();
                _stableTimer = null;
                _tracked.Clear();
                _pidGameCache.Clear();
            }
        }

        // === P/Invoke ===

        private delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern bool QueryFullProcessImageName(
            IntPtr hProcess,
            uint dwFlags,
            StringBuilder lpExeName,
            ref uint lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }
    }
}

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Models.Achievements;

namespace PlayniteAchievements.Services.ThemeIntegration
{
    /// <summary>
    /// Coalesces and executes theme-integration updates so that:
    /// - Controls can request updates freely without recomputing per-control.
    /// - Heavy list prep/sorting runs off the UI thread.
    /// - Settings are only updated for the latest requested game.
    /// </summary>
    public sealed class ThemeIntegrationUpdateService : IDisposable
    {
        private readonly ThemeIntegrationService _integrator;
        private readonly AchievementManager _achievementManager;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;
        private readonly Dispatcher _uiDispatcher;

        private readonly object _gate = new object();
        private Task _runner;
        private int _requestVersion;
        private int _processedVersion;
        private Guid? _requestedGameId;

        private CancellationTokenSource _activeCts;

        private Guid? _appliedGameId;
        private DateTime _appliedLastUpdatedUtc;
        private int _appliedSettingsRevision;

        private double _ultraRareThreshold;
        private double _rareThreshold;
        private double _uncommonThreshold;
        private int _settingsRevision;

        public ThemeIntegrationUpdateService(
            ThemeIntegrationService integrator,
            AchievementManager AchievementManager,
            PlayniteAchievementsSettings settings,
            ILogger logger,
            Dispatcher uiDispatcher)
        {
            _integrator = integrator ?? throw new ArgumentNullException(nameof(integrator));
            _achievementManager = AchievementManager ?? throw new ArgumentNullException(nameof(AchievementManager));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger;
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));

            RefreshCachedThresholds();
            _settings.PropertyChanged += Settings_PropertyChanged;
        }

        public void Dispose()
        {
            try { _settings.PropertyChanged -= Settings_PropertyChanged; } catch { }
            CancelActive();
        }

        public void RequestUpdate(Guid? gameId)
        {
            lock (_gate)
            {
                _requestVersion++;
                _requestedGameId = gameId;
                if (_runner == null || _runner.IsCompleted)
                {
                    _runner = RunAsync();
                }
            }

            // Clear immediately when switching games so the UI doesn't show the previous selection.
            if (gameId.HasValue && (!_appliedGameId.HasValue || _appliedGameId.Value != gameId.Value))
            {
                _ = _uiDispatcher.BeginInvoke(new Action(() =>
                {
                    try { _integrator.ClearSingleGameThemeProperties(); } catch { }
                }), DispatcherPriority.Background);
            }
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            if (e.PropertyName == $"{nameof(PlayniteAchievementsSettings.Persisted)}.{nameof(PersistedSettings.UltraRareThreshold)}" ||
                e.PropertyName == nameof(PersistedSettings.UltraRareThreshold) ||
                e.PropertyName == $"{nameof(PlayniteAchievementsSettings.Persisted)}.{nameof(PersistedSettings.RareThreshold)}" ||
                e.PropertyName == nameof(PersistedSettings.RareThreshold) ||
                e.PropertyName == $"{nameof(PlayniteAchievementsSettings.Persisted)}.{nameof(PersistedSettings.UncommonThreshold)}" ||
                e.PropertyName == nameof(PersistedSettings.UncommonThreshold))
            {
                RefreshCachedThresholds();
                Interlocked.Increment(ref _settingsRevision);

                // Refresh current game snapshot so themes reflect new thresholds.
                if (_appliedGameId.HasValue)
                {
                    RequestUpdate(_appliedGameId);
                }
            }
        }

        private void RefreshCachedThresholds()
        {
            try
            {
                _ultraRareThreshold = _settings.Persisted.UltraRareThreshold;
                _rareThreshold = _settings.Persisted.RareThreshold;
                _uncommonThreshold = _settings.Persisted.UncommonThreshold;

                // Keep global helper in sync for any callers that use it (fullscreen lists, badges).
                RarityHelper.Configure(_ultraRareThreshold, _rareThreshold, _uncommonThreshold);
            }
            catch
            {
                _ultraRareThreshold = 5;
                _rareThreshold = 10;
                _uncommonThreshold = 50;
                RarityHelper.Configure(_ultraRareThreshold, _rareThreshold, _uncommonThreshold);
            }
        }

        private void CancelActive()
        {
            try
            {
                var cts = _activeCts;
                if (cts != null)
                {
                    cts.Cancel();
                    cts.Dispose();
                }
            }
            catch
            {
            }
            finally
            {
                _activeCts = null;
            }
        }

        private async Task RunAsync()
        {
            while (true)
            {
                int version;
                Guid? gameId;
                CancellationToken token;
                var settingsRevision = Volatile.Read(ref _settingsRevision);

                lock (_gate)
                {
                    if (_processedVersion == _requestVersion)
                    {
                        return;
                    }

                    version = _requestVersion;
                    gameId = _requestedGameId;
                    _processedVersion = version;

                    CancelActive();
                    _activeCts = new CancellationTokenSource();
                    token = _activeCts.Token;
                }

                if (token.IsCancellationRequested)
                {
                    continue;
                }

                if (!gameId.HasValue)
                {
                    await ApplyClearAsync(version).ConfigureAwait(false);
                    continue;
                }

                GameAchievementData gameData = null;
                try
                {
                    gameData = await Task.Run(() => _achievementManager.GetGameAchievementData(gameId.Value), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Theme integration update failed to fetch game data.");
                }

                if (token.IsCancellationRequested)
                {
                    continue;
                }

                if (gameData == null || gameData.NoAchievements)
                {
                    await ApplyClearAsync(version).ConfigureAwait(false);
                    continue;
                }

                if (_appliedGameId.HasValue &&
                    _appliedGameId.Value == gameId.Value &&
                    _appliedLastUpdatedUtc == gameData.LastUpdatedUtc &&
                    _appliedSettingsRevision == settingsRevision)
                {
                    continue;
                }

                SingleGameSnapshot snapshot = null;
                try
                {
                    var ultra = _ultraRareThreshold;
                    var rare = _rareThreshold;
                    var uncommon = _uncommonThreshold;

                    snapshot = await Task.Run(
                        () => SingleGameSnapshotService.BuildSnapshot(gameId.Value, gameData, ultra, rare, uncommon),
                        token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Theme integration update failed while building snapshot.");
                }

                if (token.IsCancellationRequested)
                {
                    continue;
                }

                await ApplySnapshotAsync(version, snapshot, gameId.Value, gameData.LastUpdatedUtc, settingsRevision).ConfigureAwait(false);
            }
        }

        private Task ApplyClearAsync(int version)
        {
            return _uiDispatcher.InvokeAsync(() =>
            {
                if (!IsLatest(version))
                {
                    return;
                }

                _integrator.ClearSingleGameThemeProperties();
                _appliedGameId = null;
                _appliedLastUpdatedUtc = default;
                _appliedSettingsRevision = Volatile.Read(ref _settingsRevision);
            }, DispatcherPriority.Background).Task;
        }

        private Task ApplySnapshotAsync(int version, SingleGameSnapshot snapshot, Guid gameId, DateTime lastUpdatedUtc, int settingsRevision)
        {
            return _uiDispatcher.InvokeAsync(() =>
            {
                if (!IsLatest(version))
                {
                    return;
                }

                _integrator.ApplySnapshot(snapshot);
                _appliedGameId = gameId;
                _appliedLastUpdatedUtc = lastUpdatedUtc;
                _appliedSettingsRevision = settingsRevision;
            }, DispatcherPriority.Background).Task;
        }

        private bool IsLatest(int version)
        {
            lock (_gate)
            {
                return version == _requestVersion;
            }
        }
    }
}

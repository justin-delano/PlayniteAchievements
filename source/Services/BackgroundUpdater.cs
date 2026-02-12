using System;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using PlayniteAchievements.Models;

namespace PlayniteAchievements.Services
{
    public class BackgroundUpdater
    {
        private readonly ScanManager _achievementService;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;
        private readonly NotificationPublisher _notifications;
        private readonly Action _onUpdateCompleted;

        private readonly object _ctsLock = new object();
        private CancellationTokenSource _cts;

        public BackgroundUpdater(
            ScanManager achievementService,
            PlayniteAchievementsSettings settings,
            ILogger logger,
            NotificationPublisher notifications,
            Action onUpdateCompleted)
        {
            _achievementService = achievementService;
            _settings = settings;
            _logger = logger;
            _notifications = notifications;
            _onUpdateCompleted = onUpdateCompleted;
        }

        public void Start()
        {
            lock (_ctsLock)
            {
                if (_cts != null)
                {
                    return;
                }

                _cts = new CancellationTokenSource();
            }

            var token = _cts.Token;

            var interval = TimeSpan.FromHours(Math.Max(1, _settings.Persisted.PeriodicUpdateHours));

            // Run an initial check immediately on startup, then continue with the normal loop.
            Task.Run(async () =>
            {
                try
                {
                    await PerformUpdateIfNeeded(interval, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var msg = ResourceProvider.GetString("LOCPlayAch_Error_Periodic_InitialCheckFailed");
                    _logger.Error(ex, msg);
                }

                await PeriodicUpdateLoop(interval, token).ConfigureAwait(false);
            }, token);
        }

        public void Stop()
        {
            CancellationTokenSource ctsToDispose = null;

            lock (_ctsLock)
            {
                if (_cts == null)
                {
                    return;
                }

                ctsToDispose = _cts;
                _cts = null;
            }

            try
            {
                ctsToDispose?.Cancel();
                _achievementService?.CancelCurrentRebuild();
            }
            catch (Exception ex)
            {
                // Log but ignore shutdown errors to ensure cleanup completes
                _logger?.Debug(ex, "[PeriodicUpdate] Error during background update service shutdown.");
            }
            finally
            {
                ctsToDispose?.Dispose();
            }
        }

        private async Task PeriodicUpdateLoop(TimeSpan interval, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await PerformUpdateIfNeeded(interval, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var msg = ResourceProvider.GetString("LOCPlayAch_Error_Periodic_UpdateFailed");
                    _logger.Error(ex, msg);
                }

                await DelayNextUpdate(interval, token).ConfigureAwait(false);
            }
        }

        private async Task PerformUpdateIfNeeded(TimeSpan interval, CancellationToken token)
        {
            if (ShouldPerformUpdate(interval))
            {
                await ExecuteUpdate(token).ConfigureAwait(false);
            }
            else
            {
                _logger.Debug("[PeriodicUpdate] Cache is recent; skipping update.");
            }
        }

        private bool ShouldPerformUpdate(TimeSpan interval)
        {
            var cacheValid = _achievementService.Cache?.IsCacheValid() ?? false;
            _logger.Debug($"[PeriodicUpdate] Cache valid={cacheValid}");

            return _settings.Persisted.EnablePeriodicUpdates && !cacheValid;
        }

        private async Task ExecuteUpdate(CancellationToken token)
        {
            _logger.Debug("[PeriodicUpdate] Triggering cache update...");

            try
            {
                await _achievementService.ExecuteScanAsync(ScanModeType.Quick).ConfigureAwait(false);

                _logger.Debug("[PeriodicUpdate] Cache update completed.");
                HandleUpdateCompletion();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Graceful shutdown
            }
            catch (Exception ex)
            {
                var msg = ResourceProvider.GetString("LOCPlayAch_Error_Periodic_UpdateFailed");
                _logger.Error(ex, msg);
            }
        }

        private void HandleUpdateCompletion()
        {
            var lastStatus = _achievementService.GetLastRebuildStatus() ?? ResourceProvider.GetString("LOCPlayAch_Rebuild_Completed");
            _notifications?.ShowPeriodicStatus(lastStatus);
            _onUpdateCompleted?.Invoke();
        }

        private async Task DelayNextUpdate(TimeSpan interval, CancellationToken token)
        {
            try
            {
                await Task.Delay(interval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Loop will terminate
            }
        }
    }
}

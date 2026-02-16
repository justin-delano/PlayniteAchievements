using PlayniteAchievements.Models;
using PlayniteAchievements.Providers;
using PlayniteAchievements.ViewModels;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.PSN
{
    internal sealed class PsnDataProvider : IDataProvider
    {
        private readonly ILogger _logger;
        private readonly PsnLibraryBridge _bridge;
        private readonly PsnScanner _scanner;
        private readonly PlayniteAchievementsSettingsViewModel _settings;

        public PsnDataProvider(ILogger logger, IPlayniteAPI api, PlayniteAchievementsSettingsViewModel settings)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (api == null) throw new ArgumentNullException(nameof(api));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            _bridge = new PsnLibraryBridge(api, _logger);
            _scanner = new PsnScanner(_logger, _bridge);

            // Best-effort init (provider can still be present even if PSNLibrary isn't installed)
            _bridge.TryInitialize();
        }

        public string ProviderName => ResourceProvider.GetString("LOCPlayAch_Provider_PlayStation");

        // Reuse an existing icon key to avoid crashes if a PSN-specific resource isn't present
        public string ProviderIconKey => "ProviderIconGOG";

        public string ProviderColorHex => "#003791";

        // Real auth is verified during ScanAsync via token retrieval
        public bool IsAuthenticated => _bridge.IsAvailable;

        public bool IsCapable(Game game)
        {
            if (!IsPsnEnabled || !_bridge.IsAvailable || game == null)
            {
                return false;
            }

            var id = (game.GameId ?? string.Empty).Trim();
            if (LooksLikePsnId(id))
            {
                return true;
            }

            var src = (game.Source?.Name ?? string.Empty).Trim();
            return src.IndexOf("PlayStation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   src.Equals("PSN", StringComparison.OrdinalIgnoreCase);
        }

        public Task<RebuildPayload> ScanAsync(
            List<Game> gamesToScan,
            Action<ProviderScanUpdate> progressCallback,
            Func<GameAchievementData, Task> onGameScanned,
            CancellationToken cancel)
        {
            if (!IsPsnEnabled)
            {
                return Task.FromResult(new RebuildPayload { Summary = new RebuildSummary() });
            }

            return _scanner.ScanAsync(gamesToScan, progressCallback, onGameScanned, cancel);
        }

        private bool IsPsnEnabled => _settings?.Settings?.Persisted?.EnablePsnAchievements ?? true;

        private static bool LooksLikePsnId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            // Direct ids
            if (id.StartsWith("CUSA", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("PPSA", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("NPWR", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // PSNLibrary sometimes stores "psn#...#CUSA12345_00" etc.
            return id.IndexOf("#CUSA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   id.IndexOf("#PPSA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   id.IndexOf("#NPWR", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

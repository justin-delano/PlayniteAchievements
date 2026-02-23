using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.RPCS3
{
    /// <summary>
    /// Data provider for RPCS3 PlayStation 3 emulator trophy tracking.
    /// Parses local trophy files (TROPCONF.SFM + TROPUSR.DAT) from RPCS3 installation.
    /// </summary>
    internal sealed class Rpcs3DataProvider : IDataProvider
    {
        private readonly Rpcs3Scanner _scanner;
        private readonly PlayniteAchievementsSettings _settings;

        public Rpcs3DataProvider(ILogger logger, PlayniteAchievementsSettings settings)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _settings = settings;

            _scanner = new Rpcs3Scanner(logger, _settings);
        }

        public string ProviderName
        {
            get
            {
                var value = ResourceProvider.GetString("LOCPlayAch_Provider_RPCS3");
                return string.IsNullOrWhiteSpace(value) ? "RPCS3" : value;
            }
        }

        public string ProviderKey => "RPCS3";

        public string ProviderIconKey => "ProviderIconRPCS3";

        public string ProviderColorHex => "#0070D1";

        public bool IsAuthenticated
        {
            get
            {
                var installFolder = _settings?.Persisted?.Rpcs3InstallationFolder;
                if (string.IsNullOrWhiteSpace(installFolder))
                {
                    return false;
                }

                var trophyPath = Path.Combine(installFolder, "trophy");
                return Directory.Exists(trophyPath);
            }
        }

        public bool IsCapable(Game game)
        {
            if (game == null)
            {
                return false;
            }

            var src = (game.Source?.Name ?? string.Empty).Trim();
            if (src.IndexOf("RPCS3", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // Fallback: check if platform is PlayStation 3
            var platforms = game.Platforms;
            if (platforms != null)
            {
                foreach (var platform in platforms)
                {
                    if (platform == null) continue;

                    var platformName = (platform.Name ?? string.Empty).Trim();
                    if (platformName.IndexOf("PlayStation 3", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        platformName.IndexOf("PS3", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public Task<RebuildPayload> RefreshAsync(
            List<Game> gamesToRefresh,
            Action<ProviderRefreshUpdate> progressCallback,
            Func<GameAchievementData, Task> OnGameRefreshed,
            CancellationToken cancel)
        {
            return _scanner.RefreshAsync(gamesToRefresh, progressCallback, OnGameRefreshed, cancel);
        }
    }
}

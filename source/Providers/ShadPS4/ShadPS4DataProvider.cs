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

namespace PlayniteAchievements.Providers.ShadPS4
{
    internal sealed class ShadPS4DataProvider : IDataProvider
    {
        private readonly ShadPS4Scanner _scanner;
        private readonly PlayniteAchievementsSettings _settings;

        public ShadPS4DataProvider(ILogger logger, PlayniteAchievementsSettings settings)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _settings = settings;

            _scanner = new ShadPS4Scanner(logger, _settings);
        }

        public string ProviderName
        {
            get
            {
                var value = ResourceProvider.GetString("LOCPlayAch_Provider_ShadPS4");
                return string.IsNullOrWhiteSpace(value) ? "ShadPS4" : value;
            }
        }

        public string ProviderKey => "ShadPS4";

        public string ProviderIconKey => "ProviderIconShadPS4";

        public string ProviderColorHex => "#0070D1";

        public bool IsAuthenticated
        {
            get
            {
                var installFolder = _settings?.Persisted?.ShadPS4InstallationFolder;
                if (string.IsNullOrWhiteSpace(installFolder))
                {
                    return false;
                }

                var gameDataPath = Path.Combine(installFolder, "user", "game_data");
                return Directory.Exists(gameDataPath);
            }
        }

        public bool IsCapable(Game game)
        {
            if (game == null)
            {
                return false;
            }

            var src = (game.Source?.Name ?? string.Empty).Trim();
            if (src.IndexOf("ShadPS4", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
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

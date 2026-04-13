using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.ShadPS4
{
    internal sealed class ShadPS4Scanner
    {
        public ShadPS4Scanner(
            ILogger logger,
            PlayniteAchievementsSettings settings,
            ShadPS4Settings providerSettings,
            ShadPS4DataProvider provider = null,
            IPlayniteAPI playniteApi = null,
            string pluginUserDataPath = null)
        {
        }

        public Task<RebuildPayload> RefreshAsync(
            IReadOnlyList<Game> gamesToRefresh,
            Action<Game> onGameStarting,
            Func<Game, GameAchievementData, Task> onGameCompleted,
            CancellationToken cancel)
        {
            return Task.FromResult(new RebuildPayload());
        }
    }

    public sealed class ShadPS4SettingsView : ProviderSettingsViewBase
    {
        public ShadPS4SettingsView(IPlayniteAPI playniteApi)
        {
        }
    }
}

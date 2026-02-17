using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.ViewModels;
using Playnite.SDK;

namespace PlayniteAchievements.Services.Sidebar
{
    public sealed class SidebarDataBuilder
    {
        private readonly AchievementManager _achievementManager;
        private readonly IPlayniteAPI _playniteApi;
        private readonly ILogger _logger;

        public SidebarDataBuilder(AchievementManager achievementManager, IPlayniteAPI playniteApi, ILogger logger)
        {
            _achievementManager = achievementManager ?? throw new ArgumentNullException(nameof(achievementManager));
            _playniteApi = playniteApi;
            _logger = logger;
        }

        public SidebarDataSnapshot Build(
            PlayniteAchievementsSettings settings,
            ISet<string> revealedKeys,
            CancellationToken cancel)
        {
            settings ??= new PlayniteAchievementsSettings();
            revealedKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var snapshot = new SidebarDataSnapshot();

            var allAchievements = new List<AchievementDisplayItem>();
            var gamesOverview = new List<GameOverviewItem>();
            var recentAchievements = new List<RecentAchievementItem>();

            var globalCounts = new Dictionary<DateTime, int>();
            var singleGameCounts = new Dictionary<Guid, Dictionary<DateTime, int>>();

            int totalAchievements = 0;
            int totalUnlocked = 0;
            int commonCount = 0;
            int uncommonCount = 0;
            int rareCount = 0;
            int ultraRareCount = 0;
            int perfectGames = 0;

            var unlockedByProvider = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var showIcon = settings.Persisted?.ShowHiddenIcon ?? false;
            var showTitle = settings.Persisted?.ShowHiddenTitle ?? false;
            var showDescription = settings.Persisted?.ShowHiddenDescription ?? false;
            var anyHidingEnabled = !showIcon || !showTitle || !showDescription;
            var canResolveReveals = anyHidingEnabled && revealedKeys.Count > 0;

            List<GameAchievementData> allGameData;
            allGameData = _achievementManager.GetAllGameAchievementData() ?? new List<GameAchievementData>();

            foreach (var gameData in allGameData)
            {
                cancel.ThrowIfCancellationRequested();
                if (gameData?.Achievements == null || gameData.NoAchievements || gameData.Achievements.Count == 0)
                {
                    continue;
                }

                var playniteGame = gameData.PlayniteGameId.HasValue
                    ? _playniteApi?.Database?.Games?.Get(gameData.PlayniteGameId.Value)
                    : null;

                // Build overview stats for this game.
                var achievements = gameData.Achievements;
                int gameTotal = achievements.Count;
                int gameUnlocked = 0;
                int gameCommon = 0, gameUncommon = 0, gameRare = 0, gameUltraRare = 0;

                for (int i = 0; i < achievements.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();

                    var ach = achievements[i];
                    if (ach == null)
                    {
                        continue;
                    }

                    var item = new AchievementDisplayItem
                    {
                        GameName = gameData.GameName ?? "Unknown",
                        PlayniteGameId = gameData.PlayniteGameId,
                        DisplayName = ach.DisplayName ?? ach.ApiName ?? "Unknown",
                        Description = ach.Description ?? string.Empty,
                        IconPath = ach.UnlockedIconPath,
                        UnlockTimeUtc = ach.UnlockTimeUtc,
                        GlobalPercentUnlocked = ach.GlobalPercentUnlocked,
                        Unlocked = ach.Unlocked,
                        Hidden = ach.Hidden,
                        ApiName = ach.ApiName,
                        ShowHiddenIcon = showIcon,
                        ShowHiddenTitle = showTitle,
                        ShowHiddenDescription = showDescription,
                        ProgressNum = ach.ProgressNum,
                        ProgressDenom = ach.ProgressDenom
                    };

                    if (canResolveReveals && ach.Hidden && !ach.Unlocked)
                    {
                        var revealKey = MakeRevealKey(gameData.PlayniteGameId, ach.ApiName, gameData.GameName);
                        item.IsRevealed = revealedKeys.Contains(revealKey);
                    }
                    else
                    {
                        item.IsRevealed = false;
                    }

                    allAchievements.Add(item);

                    if (ach.Unlocked)
                    {
                        gameUnlocked++;

                        var pct = ach.GlobalPercentUnlocked ?? 100;
                        var tier = RarityHelper.GetRarityTier(pct);
                        switch (tier)
                        {
                            case RarityTier.UltraRare: ultraRareCount++; gameUltraRare++; break;
                            case RarityTier.Rare: rareCount++; gameRare++; break;
                            case RarityTier.Uncommon: uncommonCount++; gameUncommon++; break;
                            default: commonCount++; gameCommon++; break;
                        }

                        if (ach.UnlockTimeUtc.HasValue)
                        {
                            var unlockDate = DateTimeUtilities.AsUtcKind(ach.UnlockTimeUtc.Value).Date;
                            Increment(globalCounts, unlockDate);

                            if (gameData.PlayniteGameId.HasValue)
                            {
                                if (!singleGameCounts.TryGetValue(gameData.PlayniteGameId.Value, out var dict))
                                {
                                    dict = new Dictionary<DateTime, int>();
                                    singleGameCounts[gameData.PlayniteGameId.Value] = dict;
                                }
                                Increment(dict, unlockDate);

                                recentAchievements.Add(new RecentAchievementItem
                                {
                                    ApiName = ach.ApiName,
                                    Name = ach.DisplayName ?? ach.ApiName ?? "Unknown",
                                    Description = ach.Description ?? string.Empty,
                                    GameName = gameData.GameName ?? "Unknown",
                                    IconPath = ach.UnlockedIconPath,
                                    UnlockTime = DateTimeUtilities.AsUtcKind(ach.UnlockTimeUtc.Value),
                                    GlobalPercent = ach.GlobalPercentUnlocked ?? 0,
                                    GameIconPath = !string.IsNullOrEmpty(playniteGame?.Icon) ? _playniteApi.Database.GetFullFilePath(playniteGame.Icon) : null,
                                    GameCoverPath = !string.IsNullOrEmpty(playniteGame?.CoverImage) ? _playniteApi.Database.GetFullFilePath(playniteGame.CoverImage) : null,
                                    Hidden = ach.Hidden
                                });
                            }
                        }
                    }
                }

                totalAchievements += gameTotal;
                totalUnlocked += gameUnlocked;

                // Track unlocked achievements by provider
                var provider = gameData.ProviderName ?? "Unknown";
                if (!unlockedByProvider.ContainsKey(provider))
                    unlockedByProvider[provider] = 0;
                unlockedByProvider[provider] += gameUnlocked;

                if (gameUnlocked == gameTotal && gameTotal > 0)
                {
                    perfectGames++;
                }

                gamesOverview.Add(new GameOverviewItem
                {
                    GameName = gameData.GameName ?? "Unknown",
                    GameLogo = !string.IsNullOrEmpty(playniteGame?.Icon) ? _playniteApi.Database.GetFullFilePath(playniteGame.Icon) : null,
                    GameCoverPath = !string.IsNullOrEmpty(playniteGame?.CoverImage) ? _playniteApi.Database.GetFullFilePath(playniteGame.CoverImage) : null,
                    AppId = gameData.AppId,
                    PlayniteGameId = gameData.PlayniteGameId,
                    TotalAchievements = gameTotal,
                    UnlockedAchievements = gameUnlocked,
                    CommonCount = gameCommon,
                    UncommonCount = gameUncommon,
                    RareCount = gameRare,
                    UltraRareCount = gameUltraRare,
                    LastPlayed = playniteGame?.LastActivity,
                    IsPerfect = gameUnlocked == gameTotal && gameTotal > 0,
                    Provider = gameData.ProviderName ?? "Unknown"
                });
            }

            // Sort stable outputs once so UI work is minimal.
            gamesOverview = gamesOverview
                .OrderByDescending(g => g.LastPlayed ?? DateTime.MinValue)
                .ToList();

            recentAchievements = recentAchievements
                .OrderByDescending(a => a.UnlockTime)
                .ToList();

            snapshot.Achievements = allAchievements;
            snapshot.GamesOverview = gamesOverview;
            snapshot.RecentAchievements = recentAchievements;
            snapshot.GlobalUnlockCountsByDate = globalCounts;
            snapshot.UnlockCountsByDateByGame = singleGameCounts;

            snapshot.TotalGames = gamesOverview.Count;
            snapshot.TotalAchievements = totalAchievements;
            snapshot.TotalUnlocked = totalUnlocked;
            snapshot.TotalCommon = commonCount;
            snapshot.TotalUncommon = uncommonCount;
            snapshot.TotalRare = rareCount;
            snapshot.TotalUltraRare = ultraRareCount;
            snapshot.PerfectGames = perfectGames;
            snapshot.GlobalProgressionPercent = totalAchievements > 0 ? (double)totalUnlocked / totalAchievements * 100 : 0;

            // Calculate total locked as difference between total and unlocked
            snapshot.TotalLocked = totalAchievements - totalUnlocked;
            snapshot.UnlockedByProvider = unlockedByProvider;

            return snapshot;
        }

        private static void Increment(Dictionary<DateTime, int> dict, DateTime date)
        {
            if (dict.TryGetValue(date, out var existing))
            {
                dict[date] = existing + 1;
            }
            else
            {
                dict[date] = 1;
            }
        }

        private static string MakeRevealKey(Guid? playniteGameId, string apiName, string gameName)
        {
            var gamePart = playniteGameId?.ToString() ?? (gameName ?? string.Empty);
            return $"{gamePart}\u001f{apiName ?? string.Empty}";
        }
    }
}

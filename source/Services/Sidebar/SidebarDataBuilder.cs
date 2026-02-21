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
            int completedGames = 0;

            var unlockedByProvider = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var allGameData = _achievementManager.GetAllGameAchievementData() ?? new List<GameAchievementData>();
            for (var i = 0; i < allGameData.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();

                var fragment = BuildGameFragment(settings, revealedKeys, allGameData[i]);
                if (fragment == null)
                {
                    continue;
                }

                allAchievements.AddRange(fragment.Achievements);

                if (fragment.GameOverview != null)
                {
                    gamesOverview.Add(fragment.GameOverview);
                }

                if (fragment.RecentAchievements != null && fragment.RecentAchievements.Count > 0)
                {
                    recentAchievements.AddRange(fragment.RecentAchievements);
                }

                if (fragment.PlayniteGameId.HasValue)
                {
                    singleGameCounts[fragment.PlayniteGameId.Value] = fragment.UnlockCountsByDate;
                }

                foreach (var kvp in fragment.UnlockCountsByDate)
                {
                    IncrementBy(globalCounts, kvp.Key, kvp.Value);
                }

                totalAchievements += fragment.TotalAchievements;
                totalUnlocked += fragment.UnlockedAchievements;
                commonCount += fragment.CommonCount;
                uncommonCount += fragment.UncommonCount;
                rareCount += fragment.RareCount;
                ultraRareCount += fragment.UltraRareCount;
                if (fragment.IsCompleted)
                {
                    completedGames++;
                }

                var provider = fragment.ProviderName ?? "Unknown";
                if (!unlockedByProvider.ContainsKey(provider))
                {
                    unlockedByProvider[provider] = 0;
                }

                unlockedByProvider[provider] += fragment.UnlockedAchievements;
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
            snapshot.CompletedGames = completedGames;
            snapshot.GlobalProgressionPercent = totalAchievements > 0 ? (double)totalUnlocked / totalAchievements * 100 : 0;

            snapshot.TotalLocked = totalAchievements - totalUnlocked;
            snapshot.UnlockedByProvider = unlockedByProvider;

            return snapshot;
        }

        public SidebarGameFragment BuildGameFragment(
            PlayniteAchievementsSettings settings,
            ISet<string> revealedKeys,
            GameAchievementData gameData)
        {
            settings ??= new PlayniteAchievementsSettings();
            revealedKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (gameData?.Achievements == null || !gameData.HasAchievements || gameData.Achievements.Count == 0)
            {
                return null;
            }

            var playniteGame = gameData.PlayniteGameId.HasValue
                ? _playniteApi?.Database?.Games?.Get(gameData.PlayniteGameId.Value)
                : null;
            var gameIconPath = !string.IsNullOrEmpty(playniteGame?.Icon)
                ? _playniteApi.Database.GetFullFilePath(playniteGame.Icon)
                : null;
            var gameCoverPath = !string.IsNullOrEmpty(playniteGame?.CoverImage)
                ? _playniteApi.Database.GetFullFilePath(playniteGame.CoverImage)
                : null;

            var fragment = new SidebarGameFragment
            {
                CacheKey = gameData.PlayniteGameId?.ToString(),
                PlayniteGameId = gameData.PlayniteGameId,
                ProviderName = gameData.ProviderName ?? "Unknown"
            };

            var showIcon = settings.Persisted?.ShowHiddenIcon ?? false;
            var showTitle = settings.Persisted?.ShowHiddenTitle ?? false;
            var showDescription = settings.Persisted?.ShowHiddenDescription ?? false;
            var anyHidingEnabled = !showIcon || !showTitle || !showDescription;
            var canResolveReveals = anyHidingEnabled && revealedKeys.Count > 0;
            var useScaledPoints = settings.Persisted?.RaPointsMode == "scaled" &&
                                  string.Equals(gameData.ProviderName, "RetroAchievements", StringComparison.OrdinalIgnoreCase);

            var achievements = gameData.Achievements;
            int gameTotal = achievements.Count;
            int gameUnlocked = 0;
            int gameCommon = 0;
            int gameUncommon = 0;
            int gameRare = 0;
            int gameUltraRare = 0;

            for (var i = 0; i < achievements.Count; i++)
            {
                var ach = achievements[i];
                if (ach == null)
                {
                    continue;
                }

                // Determine which points to display based on provider and settings
                int? pointsToDisplay = ach.Points;
                if (useScaledPoints)
                {
                    pointsToDisplay = ach.ScaledPoints ?? ach.Points;
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
                    ProgressDenom = ach.ProgressDenom,
                    PointsValue = pointsToDisplay
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

                fragment.Achievements.Add(item);

                if (ach.Unlocked)
                {
                    gameUnlocked++;

                    var pct = ach.GlobalPercentUnlocked ?? 100;
                    var tier = RarityHelper.GetRarityTier(pct);
                    switch (tier)
                    {
                        case RarityTier.UltraRare:
                            gameUltraRare++;
                            break;
                        case RarityTier.Rare:
                            gameRare++;
                            break;
                        case RarityTier.Uncommon:
                            gameUncommon++;
                            break;
                        default:
                            gameCommon++;
                            break;
                    }

                    if (ach.UnlockTimeUtc.HasValue)
                    {
                        var unlockDate = DateTimeUtilities.AsUtcKind(ach.UnlockTimeUtc.Value).Date;
                        Increment(fragment.UnlockCountsByDate, unlockDate);

                        if (gameData.PlayniteGameId.HasValue)
                        {
                            fragment.RecentAchievements.Add(new RecentAchievementItem
                            {
                                ApiName = ach.ApiName,
                                PlayniteGameId = gameData.PlayniteGameId,
                                Name = ach.DisplayName ?? ach.ApiName ?? "Unknown",
                                Description = ach.Description ?? string.Empty,
                                GameName = gameData.GameName ?? "Unknown",
                                IconPath = ach.UnlockedIconPath,
                                UnlockTime = DateTimeUtilities.AsUtcKind(ach.UnlockTimeUtc.Value),
                                GlobalPercent = ach.GlobalPercentUnlocked ?? 0,
                                PointsValue = pointsToDisplay,
                                ProgressNum = ach.ProgressNum,
                                ProgressDenom = ach.ProgressDenom,
                                GameIconPath = gameIconPath,
                                GameCoverPath = gameCoverPath,
                                Hidden = ach.Hidden
                            });
                        }
                    }
                }
            }

            fragment.TotalAchievements = gameTotal;
            fragment.UnlockedAchievements = gameUnlocked;
            fragment.CommonCount = gameCommon;
            fragment.UncommonCount = gameUncommon;
            fragment.RareCount = gameRare;
            fragment.UltraRareCount = gameUltraRare;
            fragment.IsCompleted = gameData.IsCompleted;

            fragment.GameOverview = new GameOverviewItem
            {
                GameName = gameData.GameName ?? "Unknown",
                GameLogo = gameIconPath,
                GameCoverPath = gameCoverPath,
                AppId = gameData.AppId,
                PlayniteGameId = gameData.PlayniteGameId,
                TotalAchievements = gameTotal,
                UnlockedAchievements = gameUnlocked,
                CommonCount = gameCommon,
                UncommonCount = gameUncommon,
                RareCount = gameRare,
                UltraRareCount = gameUltraRare,
                LastPlayed = playniteGame?.LastActivity,
                IsCompleted = gameData.IsCompleted,
                Provider = gameData.ProviderName ?? "Unknown"
            };

            return fragment;
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

        private static void IncrementBy(Dictionary<DateTime, int> dict, DateTime date, int count)
        {
            if (dict.TryGetValue(date, out var existing))
            {
                dict[date] = existing + count;
            }
            else
            {
                dict[date] = count;
            }
        }

        private static string MakeRevealKey(Guid? playniteGameId, string apiName, string gameName)
        {
            var gamePart = playniteGameId?.ToString() ?? (gameName ?? string.Empty);
            return $"{gamePart}\u001f{apiName ?? string.Empty}";
        }
    }
}

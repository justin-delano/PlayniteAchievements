using Playnite.SDK;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.ThemeIntegration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PlayniteAchievements.Services.ThemeIntegration
{
    internal static class LibraryRuntimeStateBuilder
    {
        public static LibraryRuntimeState Build(
            List<GameAchievementData> allData,
            IPlayniteAPI api,
            CancellationToken token,
            bool includeHeavyAchievementLists = true)
        {
            token.ThrowIfCancellationRequested();

            allData ??= new List<GameAchievementData>();
            var state = new LibraryRuntimeState
            {
                HeavyListsBuilt = includeHeavyAchievementLists
            };

            var summariesById = new Dictionary<Guid, GameSummaryRuntimeItem>();
            var allGames = new List<GameSummaryRuntimeItem>();

            foreach (var data in allData)
            {
                token.ThrowIfCancellationRequested();

                if (data?.PlayniteGameId == null || !data.HasAchievements)
                {
                    continue;
                }

                var total = data.Achievements?.Count ?? 0;
                if (total <= 0)
                {
                    continue;
                }

                var unlocked = 0;
                var latestUnlockUtc = DateTime.MinValue;
                for (int i = 0; i < data.Achievements.Count; i++)
                {
                    var achievement = data.Achievements[i];
                    if (achievement?.Unlocked != true)
                    {
                        continue;
                    }

                    unlocked++;
                    if (!achievement.UnlockTimeUtc.HasValue)
                    {
                        continue;
                    }

                    var utc = NormalizeUtc(achievement.UnlockTimeUtc.Value);
                    if (utc > latestUnlockUtc)
                    {
                        latestUnlockUtc = utc;
                    }
                }

                GetTrophyCounts(
                    data,
                    out var commonUnlockCount,
                    out var uncommonUnlockCount,
                    out var rareUnlockCount,
                    out var ultraRareUnlockCount,
                    out var gold,
                    out var silver,
                    out var bronze);

                var summary = new GameSummaryRuntimeItem
                {
                    GameId = data.PlayniteGameId.Value,
                    Name = data.Game?.Name ?? data.GameName ?? string.Empty,
                    Platform = data.Game?.Source?.Name ?? "Unknown",
                    CoverImagePath = ResolveCoverImagePath(data.Game, api),
                    Progress = unlocked == total ? 100 : (int)Math.Floor(100.0 * unlocked / total),
                    GoldCount = gold,
                    SilverCount = silver,
                    BronzeCount = bronze,
                    IsCompleted = data.IsCompleted,
                    LastUnlockDate = latestUnlockUtc == DateTime.MinValue ? DateTime.MinValue : latestUnlockUtc.ToLocalTime(),
                    CommonUnlockCount = commonUnlockCount,
                    UncommonUnlockCount = uncommonUnlockCount,
                    RareUnlockCount = rareUnlockCount,
                    UltraRareUnlockCount = ultraRareUnlockCount
                };

                allGames.Add(summary);
                summariesById[summary.GameId] = summary;
            }

            allGames = allGames
                .OrderByDescending(item => item.LastUnlockDate)
                .ThenByDescending(item => item.Progress)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            state.AllGamesWithAchievements = allGames;
            state.GameSummariesDesc = allGames;
            state.GameSummariesAsc = allGames
                .OrderBy(item => item.LastUnlockDate)
                .ThenByDescending(item => item.Progress)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            state.CompletedGamesDesc = state.GameSummariesDesc.Where(item => item.IsCompleted).ToList();
            state.CompletedGamesAsc = state.GameSummariesAsc.Where(item => item.IsCompleted).ToList();
            state.PlatinumGames = allGames
                .Where(item => item.IsCompleted && item.LastUnlockDate != DateTime.MinValue)
                .OrderByDescending(item => item.LastUnlockDate)
                .ToList();
            state.PlatinumGamesAscending = state.PlatinumGames.OrderBy(item => item.LastUnlockDate).ToList();

            state.PlatinumTrophies = state.PlatinumGames.Count;
            state.GoldTrophies = allGames.Sum(item => item.GoldCount);
            state.SilverTrophies = allGames.Sum(item => item.SilverCount);
            state.BronzeTrophies = allGames.Sum(item => item.BronzeCount);
            state.TotalCommonUnlockCount = allGames.Sum(item => item.CommonUnlockCount);
            state.TotalUncommonUnlockCount = allGames.Sum(item => item.UncommonUnlockCount);
            state.TotalRareUnlockCount = allGames.Sum(item => item.RareUnlockCount);
            state.TotalUltraRareUnlockCount = allGames.Sum(item => item.UltraRareUnlockCount);
            state.TotalUnlockCount =
                state.TotalCommonUnlockCount +
                state.TotalUncommonUnlockCount +
                state.TotalRareUnlockCount +
                state.TotalUltraRareUnlockCount;
            state.TotalTrophies =
                state.PlatinumTrophies +
                state.GoldTrophies +
                state.SilverTrophies +
                state.BronzeTrophies;

            PopulateProviderLists(state, allData, summariesById);

            if (state.TotalTrophies > 0)
            {
                state.Score = state.PlatinumTrophies * 300 +
                              state.GoldTrophies * 90 +
                              state.SilverTrophies * 30 +
                              state.BronzeTrophies * 15;
                ComputeLevel(state.Score, out var level, out var levelProgress);
                state.Level = level;
                state.LevelProgress = levelProgress;
                state.Rank = RankFromLevel(level);
            }

            PopulateAchievementLists(state, allData, token, includeHeavyAchievementLists);
            return state;
        }

        private static void PopulateProviderLists(
            LibraryRuntimeState state,
            IEnumerable<GameAchievementData> allData,
            IReadOnlyDictionary<Guid, GameSummaryRuntimeItem> summariesById)
        {
            var steamGames = new List<GameSummaryRuntimeItem>();
            var gogGames = new List<GameSummaryRuntimeItem>();
            var epicGames = new List<GameSummaryRuntimeItem>();
            var xboxGames = new List<GameSummaryRuntimeItem>();
            var psnGames = new List<GameSummaryRuntimeItem>();
            var retroAchievementsGames = new List<GameSummaryRuntimeItem>();
            var rpcs3Games = new List<GameSummaryRuntimeItem>();
            var shadPS4Games = new List<GameSummaryRuntimeItem>();
            var manualGames = new List<GameSummaryRuntimeItem>();

            foreach (var data in allData)
            {
                if (data?.PlayniteGameId == null || !summariesById.TryGetValue(data.PlayniteGameId.Value, out var summary))
                {
                    continue;
                }

                switch (data.ProviderKey ?? string.Empty)
                {
                    case "Steam":
                        steamGames.Add(summary);
                        break;
                    case "GOG":
                        gogGames.Add(summary);
                        break;
                    case "Epic":
                        epicGames.Add(summary);
                        break;
                    case "Xbox":
                        xboxGames.Add(summary);
                        break;
                    case "PSN":
                        psnGames.Add(summary);
                        break;
                    case "RetroAchievements":
                        retroAchievementsGames.Add(summary);
                        break;
                    case "RPCS3":
                        rpcs3Games.Add(summary);
                        break;
                    case "ShadPS4":
                        shadPS4Games.Add(summary);
                        break;
                    case "Manual":
                        manualGames.Add(summary);
                        break;
                }
            }

            state.SteamGames = steamGames.OrderByDescending(item => item.LastUnlockDate).ToList();
            state.GOGGames = gogGames.OrderByDescending(item => item.LastUnlockDate).ToList();
            state.EpicGames = epicGames.OrderByDescending(item => item.LastUnlockDate).ToList();
            state.XboxGames = xboxGames.OrderByDescending(item => item.LastUnlockDate).ToList();
            state.PSNGames = psnGames.OrderByDescending(item => item.LastUnlockDate).ToList();
            state.RetroAchievementsGames = retroAchievementsGames.OrderByDescending(item => item.LastUnlockDate).ToList();
            state.RPCS3Games = rpcs3Games.OrderByDescending(item => item.LastUnlockDate).ToList();
            state.ShadPS4Games = shadPS4Games.OrderByDescending(item => item.LastUnlockDate).ToList();
            state.ManualGames = manualGames.OrderByDescending(item => item.LastUnlockDate).ToList();
        }

        private static void PopulateAchievementLists(
            LibraryRuntimeState state,
            List<GameAchievementData> allData,
            CancellationToken token,
            bool includeHeavyAchievementLists)
        {
            if (includeHeavyAchievementLists)
            {
                var allAchievements = new List<AchievementDetail>();
                foreach (var data in allData)
                {
                    token.ThrowIfCancellationRequested();
                    if (data?.Achievements == null)
                    {
                        continue;
                    }

                    foreach (var achievement in data.Achievements)
                    {
                        if (achievement == null)
                        {
                            continue;
                        }

                        achievement.Game = data.Game;
                        allAchievements.Add(achievement);
                    }
                }

                state.AllAchievementsUnlockAsc = allAchievements
                    .OrderBy(a => a?.UnlockTimeUtc)
                    .ThenBy(a => a?.DisplayName)
                    .ToList();
                state.AllAchievementsUnlockDesc = allAchievements
                    .OrderByDescending(a => a?.UnlockTimeUtc)
                    .ThenBy(a => a?.DisplayName)
                    .ToList();
                state.AllAchievementsRarityAsc = allAchievements
                    .OrderBy(a => a?.GlobalPercentUnlocked ?? 100)
                    .ThenBy(a => a?.DisplayName)
                    .ToList();
                state.AllAchievementsRarityDesc = allAchievements
                    .OrderByDescending(a => a?.GlobalPercentUnlocked ?? 100)
                    .ThenBy(a => a?.DisplayName)
                    .ToList();

                var unlockedAchievements = allAchievements
                    .Where(a => a != null && a.UnlockTimeUtc.HasValue && a.UnlockTimeUtc.Value != DateTime.MinValue)
                    .ToList();
                PopulateRecentLists(state, unlockedAchievements, includeFullLists: true);
                return;
            }

            var unlockedRecent = new List<AchievementDetail>();
            foreach (var data in allData)
            {
                token.ThrowIfCancellationRequested();
                if (data?.Achievements == null)
                {
                    continue;
                }

                foreach (var achievement in data.Achievements)
                {
                    if (achievement == null ||
                        !achievement.UnlockTimeUtc.HasValue ||
                        achievement.UnlockTimeUtc.Value == DateTime.MinValue)
                    {
                        continue;
                    }

                    achievement.Game = data.Game;
                    unlockedRecent.Add(achievement);
                }
            }

            PopulateRecentLists(state, unlockedRecent, includeFullLists: false);
        }

        private static void PopulateRecentLists(
            LibraryRuntimeState state,
            List<AchievementDetail> unlockedAchievements,
            bool includeFullLists)
        {
            unlockedAchievements ??= new List<AchievementDetail>();

            var mostRecent = unlockedAchievements
                .OrderByDescending(a => NormalizeUtc(a.UnlockTimeUtc.Value))
                .ThenBy(a => a.DisplayName)
                .ToList();
            var rareRecentCutoffUtc = DateTime.UtcNow.AddDays(-180);
            var rareRecent = unlockedAchievements
                .Where(a => NormalizeUtc(a.UnlockTimeUtc.Value) >= rareRecentCutoffUtc)
                .OrderBy(a => a.GlobalPercentUnlocked ?? 100)
                .ThenByDescending(a => NormalizeUtc(a.UnlockTimeUtc.Value))
                .ThenBy(a => a.DisplayName)
                .ToList();

            if (includeFullLists)
            {
                state.MostRecentUnlocks = mostRecent;
                state.RarestRecentUnlocks = rareRecent;
            }

            state.MostRecentUnlocksTop3 = mostRecent.Take(3).ToList();
            state.MostRecentUnlocksTop5 = mostRecent.Take(5).ToList();
            state.MostRecentUnlocksTop10 = mostRecent.Take(10).ToList();
            state.RarestRecentUnlocksTop3 = rareRecent.Take(3).ToList();
            state.RarestRecentUnlocksTop5 = rareRecent.Take(5).ToList();
            state.RarestRecentUnlocksTop10 = rareRecent.Take(10).ToList();
        }

        private static string ResolveCoverImagePath(Playnite.SDK.Models.Game game, IPlayniteAPI api)
        {
            if (game == null || api?.Database == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(game.CoverImage))
            {
                var coverPath = api.Database.GetFullFilePath(game.CoverImage);
                if (!string.IsNullOrWhiteSpace(coverPath))
                {
                    return coverPath;
                }
            }

            if (!string.IsNullOrWhiteSpace(game.Icon))
            {
                var iconPath = api.Database.GetFullFilePath(game.Icon);
                if (!string.IsNullOrWhiteSpace(iconPath))
                {
                    return iconPath;
                }
            }

            return string.Empty;
        }

        private static void GetTrophyCounts(
            GameAchievementData data,
            out int commonUnlockCount,
            out int uncommonUnlockCount,
            out int rareUnlockCount,
            out int ultraRareUnlockCount,
            out int gold,
            out int silver,
            out int bronze)
        {
            commonUnlockCount = 0;
            uncommonUnlockCount = 0;
            rareUnlockCount = 0;
            ultraRareUnlockCount = 0;
            gold = 0;
            silver = 0;
            bronze = 0;

            if (data?.Achievements == null)
            {
                return;
            }

            foreach (var achievement in data.Achievements)
            {
                if (achievement?.Unlocked != true || !achievement.GlobalPercentUnlocked.HasValue)
                {
                    continue;
                }

                switch (RarityHelper.GetRarityTier(achievement.GlobalPercentUnlocked.Value))
                {
                    case RarityTier.UltraRare:
                        ultraRareUnlockCount++;
                        gold++;
                        break;
                    case RarityTier.Rare:
                        rareUnlockCount++;
                        gold++;
                        break;
                    case RarityTier.Uncommon:
                        uncommonUnlockCount++;
                        silver++;
                        break;
                    default:
                        commonUnlockCount++;
                        bronze++;
                        break;
                }
            }
        }

        private static DateTime NormalizeUtc(DateTime timestamp)
        {
            if (timestamp.Kind == DateTimeKind.Unspecified)
            {
                return DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
            }

            return timestamp.Kind == DateTimeKind.Local ? timestamp.ToUniversalTime() : timestamp;
        }

        private static void ComputeLevel(int score, out int level, out int levelProgressPercent)
        {
            level = 0;
            levelProgressPercent = 0;

            if (score <= 0)
            {
                return;
            }

            var rangeMin = 0;
            var rangeMax = 100;
            var step = 100;

            while (score > rangeMax)
            {
                level++;
                rangeMin = rangeMax + 1;
                step += 100;
                rangeMax = rangeMin + step - 1;
            }

            var rangeSpan = rangeMax - rangeMin + 1;
            var progress = (int)(((double)(score - rangeMin) / rangeSpan) * 100);
            levelProgressPercent = Math.Max(0, Math.Min(100, progress));
        }

        private static string RankFromLevel(int level)
        {
            if (level <= 0) return "Bronze1";
            if (level <= 3) return "Bronze1";
            if (level <= 7) return "Bronze2";
            if (level <= 12) return "Bronze3";
            if (level <= 21) return "Silver1";
            if (level <= 31) return "Silver2";
            if (level <= 44) return "Silver3";
            if (level <= 59) return "Gold1";
            if (level <= 77) return "Gold2";
            if (level <= 97) return "Gold3";
            if (level <= 119) return "Plat1";
            if (level <= 144) return "Plat2";
            if (level <= 171) return "Plat3";
            return "Plat";
        }
    }
}

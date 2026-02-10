using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.ThemeIntegration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Input;

namespace PlayniteAchievements.Services.ThemeIntegration
{
    /// <summary>
    /// Game metadata for snapshot building.
    /// </summary>
    public sealed class GameInfo
    {
        public string Name { get; set; }
        public string Platform { get; set; }
        public string CoverImagePath { get; set; }
    }

    /// <summary>
    /// Service for building all-games achievement snapshots.
    /// Creates immutable AllGamesSnapshot instances for trophy overview displays.
    /// </summary>
    public static class AllGamesSnapshotService
    {
        /// <summary>
        /// Build an all-games snapshot from achievement data.
        /// </summary>
        /// <param name="allData">All games achievement data.</param>
        /// <param name="gameInfo">Game metadata mapping.</param>
        /// <param name="openGameWindowCallback">Callback to open a game's achievement window.</param>
        /// <param name="token">Cancellation token.</param>
        public static AllGamesSnapshot BuildSnapshot(
            List<GameAchievementData> allData,
            Dictionary<Guid, GameInfo> gameInfo,
            Action<Guid> openGameWindowCallback,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var snapshot = new AllGamesSnapshot();

            allData ??= new List<GameAchievementData>();
            gameInfo ??= new Dictionary<Guid, GameInfo>();

            var items = new List<GameAchievementSummary>();
            foreach (var data in allData)
            {
                token.ThrowIfCancellationRequested();

                if (data?.PlayniteGameId == null || data.NoAchievements) continue;
                var total = data.Achievements?.Count ?? 0;
                if (total <= 0) continue;

                var id = data.PlayniteGameId.Value;
                if (!gameInfo.TryGetValue(id, out var info) || info == null)
                {
                    info = new GameInfo
                    {
                        Name = data.GameName ?? string.Empty,
                        Platform = "Unknown",
                        CoverImagePath = string.Empty
                    };
                }

                var unlocked = 0;
                var latestUnlockUtc = DateTime.MinValue;
                for (int i = 0; i < data.Achievements.Count; i++)
                {
                    var a = data.Achievements[i];
                    if (a?.Unlocked != true)
                    {
                        continue;
                    }

                    unlocked++;

                    var t = a.UnlockTimeUtc;
                    if (!t.HasValue)
                    {
                        continue;
                    }

                    var utc = t.Value;
                    if (utc.Kind == DateTimeKind.Unspecified)
                    {
                        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
                    }

                    if (utc > latestUnlockUtc)
                    {
                        latestUnlockUtc = utc;
                    }
                }

                var progress = unlocked == total ? 100 : (int)Math.Floor(100.0 * unlocked / total);
                var latestUnlockLocal = latestUnlockUtc == DateTime.MinValue ? DateTime.MinValue : latestUnlockUtc.ToLocalTime();

                GetTrophyCounts(data, out var gold, out var silver, out var bronze);

                var openCmd = new RelayCommand(_ => openGameWindowCallback(id));

                items.Add(new GameAchievementSummary(
                    id,
                    info.Name,
                    info.Platform,
                    info.CoverImagePath,
                    progress,
                    gold,
                    silver,
                    bronze,
                    latestUnlockLocal,
                    openCmd));
            }

            items = items
                .OrderByDescending(i => i.LastUnlockDate)
                .ThenByDescending(i => i.Progress)
                .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            snapshot.All = items;
            snapshot.Platinum = items
                .Where(i => i.Progress >= 100)
                .Where(i => i.LastUnlockDate != DateTime.MinValue)
                .OrderByDescending(i => i.LastUnlockDate)
                .ToList();

            snapshot.PlatinumAscending = snapshot.Platinum
                .OrderBy(i => i.LastUnlockDate)
                .ToList();

            snapshot.PlatCount = snapshot.Platinum.Count;
            snapshot.GoldCount = items.Sum(i => i.GoldCount);
            snapshot.SilverCount = items.Sum(i => i.SilverCount);
            snapshot.BronzeCount = items.Sum(i => i.BronzeCount);

            if (snapshot.TotalCount > 0)
            {
                snapshot.Score = snapshot.PlatCount * 300 + snapshot.GoldCount * 90 + snapshot.SilverCount * 30 + snapshot.BronzeCount * 15;
                ComputeLevel(snapshot.Score, out var level, out var progressPercent);
                snapshot.Level = level;
                snapshot.LevelProgress = progressPercent;
                snapshot.Rank = RankFromLevel(snapshot.Level);
            }

            // Build all-games achievement lists (flattened from all games)
            var allAchievements = new List<AchievementDetail>();
            foreach (var data in allData)
            {
                if (data?.Achievements == null) continue;
                foreach (var achievement in data.Achievements)
                {
                    if (achievement != null)
                    {
                        allAchievements.Add(achievement);
                    }
                }
            }

            // Sort by unlock date (ascending = oldest first, descending = newest first)
            snapshot.AllAchievementsUnlockAsc = allAchievements
                .OrderBy(a => a?.UnlockTimeUtc)
                .ThenBy(a => a?.DisplayName)
                .ToList();

            snapshot.AllAchievementsUnlockDesc = allAchievements
                .OrderByDescending(a => a?.UnlockTimeUtc)
                .ThenBy(a => a?.DisplayName)
                .ToList();

            // Sort by rarity (ascending = rarest first, descending = common first)
            snapshot.AllAchievementsRarityAsc = allAchievements
                .OrderBy(a => a?.GlobalPercentUnlocked ?? 100)
                .ThenBy(a => a?.DisplayName)
                .ToList();

            snapshot.AllAchievementsRarityDesc = allAchievements
                .OrderByDescending(a => a?.GlobalPercentUnlocked ?? 100)
                .ThenBy(a => a?.DisplayName)
                .ToList();

            var unlockedAchievements = allAchievements
                .Where(a => a != null && a.UnlockTimeUtc.HasValue && a.UnlockTimeUtc.Value != DateTime.MinValue)
                .ToList();

            DateTime NormalizeUtc(DateTime timestamp)
            {
                if (timestamp.Kind == DateTimeKind.Unspecified)
                {
                    return DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
                }

                return timestamp.Kind == DateTimeKind.Local ? timestamp.ToUniversalTime() : timestamp;
            }

            snapshot.MostRecentUnlocks = unlockedAchievements
                .OrderByDescending(a => NormalizeUtc(a.UnlockTimeUtc.Value))
                .ThenBy(a => a.DisplayName)
                .ToList();

            var rareRecentCutoffUtc = DateTime.UtcNow.AddDays(-180);
            snapshot.RarestRecentUnlocks = unlockedAchievements
                .Where(a => NormalizeUtc(a.UnlockTimeUtc.Value) >= rareRecentCutoffUtc)
                .OrderBy(a => a.GlobalPercentUnlocked ?? 100)
                .ThenByDescending(a => NormalizeUtc(a.UnlockTimeUtc.Value))
                .ThenBy(a => a.DisplayName)
                .ToList();

            snapshot.MostRecentUnlocksTop3 = snapshot.MostRecentUnlocks.Take(3).ToList();
            snapshot.MostRecentUnlocksTop5 = snapshot.MostRecentUnlocks.Take(5).ToList();
            snapshot.MostRecentUnlocksTop10 = snapshot.MostRecentUnlocks.Take(10).ToList();

            snapshot.RarestRecentUnlocksTop3 = snapshot.RarestRecentUnlocks.Take(3).ToList();
            snapshot.RarestRecentUnlocksTop5 = snapshot.RarestRecentUnlocks.Take(5).ToList();
            snapshot.RarestRecentUnlocksTop10 = snapshot.RarestRecentUnlocks.Take(10).ToList();

            return snapshot;
        }

        private static void GetTrophyCounts(GameAchievementData data, out int gold, out int silver, out int bronze)
        {
            gold = 0;
            silver = 0;
            bronze = 0;

            if (data?.Achievements == null || data.Achievements.Count == 0)
            {
                return;
            }

            foreach (var a in data.Achievements)
            {
                if (a?.Unlocked != true)
                {
                    continue;
                }

                var percent = a.GlobalPercentUnlocked ?? 100;
                var tier = RarityHelper.GetRarityTier(percent);

                if (tier == RarityTier.Uncommon)
                {
                    silver++;
                }
                else if (tier == RarityTier.Common)
                {
                    bronze++;
                }
                else
                {
                    gold++;
                }
            }
        }

        private static void ComputeLevel(int score, out int level, out int levelProgressPercent)
        {
            level = 0;
            levelProgressPercent = 0;

            if (score <= 0)
            {
                return;
            }

            int rangeMin = 0;
            int rangeMax = 100;
            int step = 100;

            while (score > rangeMax)
            {
                level++;
                rangeMin = rangeMax + 1;
                step += 100;
                rangeMax = rangeMin + step - 1;
            }

            int rangeSpan = rangeMax - rangeMin + 1;
            int progress = (int)(((double)(score - rangeMin) / rangeSpan) * 100);
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

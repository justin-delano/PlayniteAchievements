using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PlayniteAchievements.Models.Achievements;

namespace PlayniteAchievements.Models.ThemeIntegration
{
    /// <summary>
    /// Snapshot data for all-games achievement overview.
    /// Contains game lists, trophy counts, player level/rank, and all-games achievement lists.
    /// Used by both desktop and fullscreen themes for all-games overview display.
    /// </summary>
    public sealed class AllGamesSnapshot
    {
        #region Game Lists

        /// <summary>
        /// All games with achievements, sorted by last unlock date.
        /// </summary>
        public List<GameAchievementSummary> All { get; set; } = new List<GameAchievementSummary>();

        /// <summary>
        /// Provider-completed games (for example, PSN platinum earned) sorted by last unlock date (newest first).
        /// </summary>
        public List<GameAchievementSummary> Platinum { get; set; } = new List<GameAchievementSummary>();

        /// <summary>
        /// Platinum games sorted by last unlock date (oldest first).
        /// </summary>
        public List<GameAchievementSummary> PlatinumAscending { get; set; } = new List<GameAchievementSummary>();

        /// <summary>
        /// Completed games sorted by last unlock date (newest first).
        /// Completion is provider-aware or 100% unlocked.
        /// </summary>
        public List<GameAchievementSummary> CompletedGamesDesc { get; set; } = new List<GameAchievementSummary>();

        /// <summary>
        /// Completed games sorted by last unlock date (oldest first).
        /// Completion is provider-aware or 100% unlocked.
        /// </summary>
        public List<GameAchievementSummary> CompletedGamesAsc { get; set; } = new List<GameAchievementSummary>();

        /// <summary>
        /// All game summaries sorted by last unlock date (newest first).
        /// </summary>
        public List<GameAchievementSummary> GameSummariesDesc { get; set; } = new List<GameAchievementSummary>();

        /// <summary>
        /// All game summaries sorted by last unlock date (oldest first).
        /// </summary>
        public List<GameAchievementSummary> GameSummariesAsc { get; set; } = new List<GameAchievementSummary>();

        #endregion

        #region Trophy Counts

        /// <summary>
        /// Number of provider-completed games in the platinum list.
        /// </summary>
        public int PlatCount { get; set; }

        /// <summary>
        /// Number of ultra-rare achievements unlocked (gold trophies).
        /// </summary>
        public int GoldCount { get; set; }

        /// <summary>
        /// Number of uncommon achievements unlocked (silver trophies).
        /// </summary>
        public int SilverCount { get; set; }

        /// <summary>
        /// Number of common achievements unlocked (bronze trophies).
        /// </summary>
        public int BronzeCount { get; set; }

        /// <summary>
        /// Total trophy count across all games.
        /// </summary>
        public int TotalCount => PlatCount + GoldCount + SilverCount + BronzeCount;

        #endregion

        #region Level and Rank

        /// <summary>
        /// Total score calculated from trophy counts.
        /// </summary>
        public int Score { get; set; }

        /// <summary>
        /// Player level calculated from total score.
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// Progress toward next level (0-100).
        /// </summary>
        public double LevelProgress { get; set; }

        /// <summary>
        /// Player rank based on level (Bronze1 through Plat).
        /// </summary>
        public string Rank { get; set; } = "Bronze1";

        #endregion

        #region All-Games Achievement Lists

        /// <summary>
        /// All achievements from all games, sorted by unlock date ascending (oldest first).
        /// </summary>
        public List<AchievementDetail> AllAchievementsUnlockAsc { get; set; } = new List<AchievementDetail>();

        /// <summary>
        /// All achievements from all games, sorted by unlock date descending (newest first).
        /// </summary>
        public List<AchievementDetail> AllAchievementsUnlockDesc { get; set; } = new List<AchievementDetail>();

        /// <summary>
        /// All achievements from all games, sorted by rarity ascending (rarest first).
        /// </summary>
        public List<AchievementDetail> AllAchievementsRarityAsc { get; set; } = new List<AchievementDetail>();

        /// <summary>
        /// All achievements from all games, sorted by rarity descending (common first).
        /// </summary>
        public List<AchievementDetail> AllAchievementsRarityDesc { get; set; } = new List<AchievementDetail>();

        /// <summary>
        /// Unlocked achievements from all games, sorted by unlock date descending (newest first).
        /// </summary>
        public List<AchievementDetail> MostRecentUnlocks { get; set; } = new List<AchievementDetail>();

        /// <summary>
        /// Unlocked achievements from all games, sorted by rarity ascending (rarest first),
        /// then by unlock date descending.
        /// </summary>
        public List<AchievementDetail> RarestRecentUnlocks { get; set; } = new List<AchievementDetail>();

        /// <summary>
        /// Top 3 unlocked achievements from all games, sorted by unlock date descending.
        /// </summary>
        public List<AchievementDetail> MostRecentUnlocksTop3 { get; set; } = new List<AchievementDetail>();

        /// <summary>
        /// Top 5 unlocked achievements from all games, sorted by unlock date descending.
        /// </summary>
        public List<AchievementDetail> MostRecentUnlocksTop5 { get; set; } = new List<AchievementDetail>();

        /// <summary>
        /// Top 10 unlocked achievements from all games, sorted by unlock date descending.
        /// </summary>
        public List<AchievementDetail> MostRecentUnlocksTop10 { get; set; } = new List<AchievementDetail>();

        /// <summary>
        /// Top 3 rare unlocked achievements from all games, filtered to recent window and sorted by rarity.
        /// </summary>
        public List<AchievementDetail> RarestRecentUnlocksTop3 { get; set; } = new List<AchievementDetail>();

        /// <summary>
        /// Top 5 rare unlocked achievements from all games, filtered to recent window and sorted by rarity.
        /// </summary>
        public List<AchievementDetail> RarestRecentUnlocksTop5 { get; set; } = new List<AchievementDetail>();

        /// <summary>
        /// Top 10 rare unlocked achievements from all games, filtered to recent window and sorted by rarity.
        /// </summary>
        public List<AchievementDetail> RarestRecentUnlocksTop10 { get; set; } = new List<AchievementDetail>();

        /// <summary>
        /// Indicates whether full all-games heavy list surfaces were built in this snapshot.
        /// </summary>
        public bool HeavyListsBuilt { get; set; } = true;

        #endregion

        #region Helper Methods for Theme Binding

        /// <summary>
        /// Creates an observable collection of all games for theme binding.
        /// </summary>
        public ObservableCollection<GameAchievementSummary> CreateAllGamesObservable()
        {
            return new ObservableCollection<GameAchievementSummary>(All ?? new List<GameAchievementSummary>());
        }

        /// <summary>
        /// Creates an observable collection of platinum games for theme binding.
        /// </summary>
        public ObservableCollection<GameAchievementSummary> CreatePlatinumObservable()
        {
            return new ObservableCollection<GameAchievementSummary>(Platinum ?? new List<GameAchievementSummary>());
        }

        /// <summary>
        /// Creates an observable collection of platinum games in ascending order for theme binding.
        /// </summary>
        public ObservableCollection<GameAchievementSummary> CreatePlatinumAscendingObservable()
        {
            return new ObservableCollection<GameAchievementSummary>(PlatinumAscending ?? new List<GameAchievementSummary>());
        }

        /// <summary>
        /// Creates an observable collection of completed games in descending order for theme binding.
        /// </summary>
        public ObservableCollection<GameAchievementSummary> CreateCompletedGamesDescObservable()
        {
            return new ObservableCollection<GameAchievementSummary>(CompletedGamesDesc ?? new List<GameAchievementSummary>());
        }

        /// <summary>
        /// Creates an observable collection of completed games in ascending order for theme binding.
        /// </summary>
        public ObservableCollection<GameAchievementSummary> CreateCompletedGamesAscObservable()
        {
            return new ObservableCollection<GameAchievementSummary>(CompletedGamesAsc ?? new List<GameAchievementSummary>());
        }

        /// <summary>
        /// Creates an observable collection of all game summaries in descending order for theme binding.
        /// </summary>
        public ObservableCollection<GameAchievementSummary> CreateGameSummariesDescObservable()
        {
            return new ObservableCollection<GameAchievementSummary>(GameSummariesDesc ?? new List<GameAchievementSummary>());
        }

        /// <summary>
        /// Creates an observable collection of all game summaries in ascending order for theme binding.
        /// </summary>
        public ObservableCollection<GameAchievementSummary> CreateGameSummariesAscObservable()
        {
            return new ObservableCollection<GameAchievementSummary>(GameSummariesAsc ?? new List<GameAchievementSummary>());
        }

        #endregion
    }
}

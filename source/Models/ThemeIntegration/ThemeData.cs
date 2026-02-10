using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Playnite.SDK;
using Playnite.SDK.Data;
using PlayniteAchievements.Models.Achievements;

namespace PlayniteAchievements.Models.ThemeIntegration
{
    /// <summary>
    /// Unified theme integration data for PlayniteAchievements.
    /// Contains both per-game achievement data and all-games overview data.
    /// All properties are runtime-only and should not be serialized.
    /// </summary>
    public class ThemeData : ObservableObject
    {
        #region Backing Fields - Per-Game Data

        [DontSerialize]
        private bool _hasAchievements;
        [DontSerialize]
        private bool _allUnlocked;
        [DontSerialize]
        private int _achievementCount;
        [DontSerialize]
        private int _unlockedCount;
        [DontSerialize]
        private int _lockedCount;
        [DontSerialize]
        private double _progressPercentage;
        [DontSerialize]
        private AchievementRarityStats _commonStats = new AchievementRarityStats();
        [DontSerialize]
        private AchievementRarityStats _uncommonStats = new AchievementRarityStats();
        [DontSerialize]
        private AchievementRarityStats _rareStats = new AchievementRarityStats();
        [DontSerialize]
        private AchievementRarityStats _ultraRareStats = new AchievementRarityStats();
        [DontSerialize]
        private List<AchievementDetail> _allAchievements = new List<AchievementDetail>();
        [DontSerialize]
        private List<AchievementDetail> _achievementsNewestFirst = new List<AchievementDetail>();
        [DontSerialize]
        private List<AchievementDetail> _achievementsOldestFirst = new List<AchievementDetail>();
        [DontSerialize]
        private List<AchievementDetail> _achievementsRarityAsc = new List<AchievementDetail>();
        [DontSerialize]
        private List<AchievementDetail> _achievementsRarityDesc = new List<AchievementDetail>();

        #endregion

        #region Backing Fields - All-Games Overview Data

        [DontSerialize]
        private bool _hasData;
        [DontSerialize]
        private ObservableCollection<GameAchievementSummary> _gamesWithAchievements = new ObservableCollection<GameAchievementSummary>();
        [DontSerialize]
        private ObservableCollection<GameAchievementSummary> _platinumGames = new ObservableCollection<GameAchievementSummary>();
        [DontSerialize]
        private int _totalTrophies;
        [DontSerialize]
        private int _platinumTrophies;
        [DontSerialize]
        private int _goldTrophies;
        [DontSerialize]
        private int _silverTrophies;
        [DontSerialize]
        private int _bronzeTrophies;
        [DontSerialize]
        private int _level;
        [DontSerialize]
        private double _levelProgress;
        [DontSerialize]
        private string _rank = "Bronze1";

        [DontSerialize]
        private List<AchievementDetail> _allAchievementsUnlockAsc = new List<AchievementDetail>();
        [DontSerialize]
        private List<AchievementDetail> _allAchievementsUnlockDesc = new List<AchievementDetail>();
        [DontSerialize]
        private List<AchievementDetail> _allAchievementsRarityAsc = new List<AchievementDetail>();
        [DontSerialize]
        private List<AchievementDetail> _allAchievementsRarityDesc = new List<AchievementDetail>();

        #endregion

        #region Per-Game Achievement Properties

        /// <summary>
        /// Whether achievement data is available for the currently selected game.
        /// </summary>
        [DontSerialize]
        public bool HasAchievements
        {
            get => _hasAchievements;
            set => SetValue(ref _hasAchievements, value);
        }

        /// <summary>
        /// Whether all achievements are unlocked for the currently selected game.
        /// </summary>
        [DontSerialize]
        public bool AllUnlocked
        {
            get => _allUnlocked;
            set => SetValue(ref _allUnlocked, value);
        }

        /// <summary>
        /// Total number of achievements for the currently selected game.
        /// </summary>
        [DontSerialize]
        public int AchievementCount
        {
            get => _achievementCount;
            set => SetValue(ref _achievementCount, value);
        }

        /// <summary>
        /// Number of unlocked achievements for the currently selected game.
        /// </summary>
        [DontSerialize]
        public int UnlockedCount
        {
            get => _unlockedCount;
            set => SetValue(ref _unlockedCount, value);
        }

        /// <summary>
        /// Number of locked achievements for the currently selected game.
        /// </summary>
        [DontSerialize]
        public int LockedCount
        {
            get => _lockedCount;
            set => SetValue(ref _lockedCount, value);
        }

        /// <summary>
        /// Percentage of achievements unlocked for the currently selected game.
        /// </summary>
        [DontSerialize]
        public double ProgressPercentage
        {
            get => _progressPercentage;
            set => SetValue(ref _progressPercentage, value);
        }

        /// <summary>
        /// Common achievement statistics for the currently selected game.
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats CommonStats
        {
            get => _commonStats;
            set => SetValue(ref _commonStats, value);
        }

        /// <summary>
        /// Uncommon achievement statistics for the currently selected game.
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats UncommonStats
        {
            get => _uncommonStats;
            set => SetValue(ref _uncommonStats, value);
        }

        /// <summary>
        /// Rare achievement statistics for the currently selected game.
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats RareStats
        {
            get => _rareStats;
            set => SetValue(ref _rareStats, value);
        }

        /// <summary>
        /// Ultra Rare achievement statistics for the currently selected game.
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats UltraRareStats
        {
            get => _ultraRareStats;
            set => SetValue(ref _ultraRareStats, value);
        }

        /// <summary>
        /// All achievements for the currently selected game.
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> AllAchievements
        {
            get => _allAchievements;
            set => SetValue(ref _allAchievements, value);
        }

        /// <summary>
        /// Achievements sorted by unlock date descending (newest first) for the currently selected game.
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> AchievementsNewestFirst
        {
            get => _achievementsNewestFirst;
            set => SetValue(ref _achievementsNewestFirst, value);
        }

        /// <summary>
        /// Achievements sorted by unlock date ascending (oldest first) for the currently selected game.
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> AchievementsOldestFirst
        {
            get => _achievementsOldestFirst;
            set => SetValue(ref _achievementsOldestFirst, value);
        }

        /// <summary>
        /// Achievements sorted by rarity ascending (rarest first) for the currently selected game.
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> AchievementsRarityAsc
        {
            get => _achievementsRarityAsc;
            set => SetValue(ref _achievementsRarityAsc, value);
        }

        /// <summary>
        /// Achievements sorted by rarity descending (common first) for the currently selected game.
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> AchievementsRarityDesc
        {
            get => _achievementsRarityDesc;
            set => SetValue(ref _achievementsRarityDesc, value);
        }

        #endregion

        #region All-Games Overview Properties

        /// <summary>
        /// Whether all-games achievement overview data is available.
        /// </summary>
        [DontSerialize]
        public bool HasData
        {
            get => _hasData;
            set => SetValue(ref _hasData, value);
        }

        /// <summary>
        /// All games with achievements, sorted by last unlock date.
        /// </summary>
        [DontSerialize]
        public ObservableCollection<GameAchievementSummary> GamesWithAchievements
        {
            get => _gamesWithAchievements;
            set => SetValue(ref _gamesWithAchievements, value);
        }

        /// <summary>
        /// Games with 100% achievement completion, sorted by last unlock date (newest first).
        /// </summary>
        [DontSerialize]
        public ObservableCollection<GameAchievementSummary> PlatinumGames
        {
            get => _platinumGames;
            set => SetValue(ref _platinumGames, value);
        }

        /// <summary>
        /// Total trophy count across all games.
        /// </summary>
        [DontSerialize]
        public int TotalTrophies
        {
            get => _totalTrophies;
            set => SetValue(ref _totalTrophies, Math.Max(0, value));
        }

        /// <summary>
        /// Platinum trophy count (100% completed games).
        /// </summary>
        [DontSerialize]
        public int PlatinumTrophies
        {
            get => _platinumTrophies;
            set => SetValue(ref _platinumTrophies, Math.Max(0, value));
        }

        /// <summary>
        /// Gold trophy count (ultra-rare achievements).
        /// </summary>
        [DontSerialize]
        public int GoldTrophies
        {
            get => _goldTrophies;
            set => SetValue(ref _goldTrophies, Math.Max(0, value));
        }

        /// <summary>
        /// Silver trophy count (uncommon achievements).
        /// </summary>
        [DontSerialize]
        public int SilverTrophies
        {
            get => _silverTrophies;
            set => SetValue(ref _silverTrophies, Math.Max(0, value));
        }

        /// <summary>
        /// Bronze trophy count (common achievements).
        /// </summary>
        [DontSerialize]
        public int BronzeTrophies
        {
            get => _bronzeTrophies;
            set => SetValue(ref _bronzeTrophies, Math.Max(0, value));
        }

        /// <summary>
        /// Player level calculated from total score.
        /// </summary>
        [DontSerialize]
        public int Level
        {
            get => _level;
            set => SetValue(ref _level, Math.Max(0, value));
        }

        /// <summary>
        /// Progress toward next level (0-100).
        /// </summary>
        [DontSerialize]
        public double LevelProgress
        {
            get => _levelProgress;
            set => SetValue(ref _levelProgress, value);
        }

        /// <summary>
        /// Player rank based on level (Bronze1 through Plat).
        /// </summary>
        [DontSerialize]
        public string Rank
        {
            get => _rank;
            set => SetValue(ref _rank, value ?? "Bronze1");
        }

        /// <summary>
        /// All achievements from all games, sorted by unlock date ascending (oldest first).
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> AllAchievementsUnlockAsc
        {
            get => _allAchievementsUnlockAsc;
            set => SetValue(ref _allAchievementsUnlockAsc, value);
        }

        /// <summary>
        /// All achievements from all games, sorted by unlock date descending (newest first).
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> AllAchievementsUnlockDesc
        {
            get => _allAchievementsUnlockDesc;
            set => SetValue(ref _allAchievementsUnlockDesc, value);
        }

        /// <summary>
        /// All achievements from all games, sorted by rarity ascending (rarest first).
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> AllAchievementsRarityAsc
        {
            get => _allAchievementsRarityAsc;
            set => SetValue(ref _allAchievementsRarityAsc, value);
        }

        /// <summary>
        /// All achievements from all games, sorted by rarity descending (common first).
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> AllAchievementsRarityDesc
        {
            get => _allAchievementsRarityDesc;
            set => SetValue(ref _allAchievementsRarityDesc, value);
        }

        #endregion
    }
}

using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Data;
using PlayniteAchievements.Models.Achievements;

namespace PlayniteAchievements.Models.ThemeIntegration
{
    /// <summary>
    /// Native theme integration data for PlayniteAchievements.
    /// These properties use descriptive names and are always populated for themes
    /// that want native PlayniteAchievements integration without compatibility mode.
    /// These are runtime properties and should not be serialized.
    /// </summary>
    public class NativeThemeData : ObservableObject
    {
        #region Backing Fields

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

        #region Properties

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
        /// Whether all achievements are unlocked.
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
        /// Common achievement statistics.
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats CommonStats
        {
            get => _commonStats;
            set => SetValue(ref _commonStats, value);
        }

        /// <summary>
        /// Uncommon achievement statistics.
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats UncommonStats
        {
            get => _uncommonStats;
            set => SetValue(ref _uncommonStats, value);
        }

        /// <summary>
        /// Rare achievement statistics.
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats RareStats
        {
            get => _rareStats;
            set => SetValue(ref _rareStats, value);
        }

        /// <summary>
        /// Ultra Rare achievement statistics.
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats UltraRareStats
        {
            get => _ultraRareStats;
            set => SetValue(ref _ultraRareStats, value);
        }

        /// <summary>
        /// All achievements.
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> AllAchievements
        {
            get => _allAchievements;
            set => SetValue(ref _allAchievements, value);
        }

        /// <summary>
        /// Achievements sorted by unlock date descending (newest first).
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> AchievementsNewestFirst
        {
            get => _achievementsNewestFirst;
            set => SetValue(ref _achievementsNewestFirst, value);
        }

        /// <summary>
        /// Achievements sorted by unlock date ascending (oldest first).
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> AchievementsOldestFirst
        {
            get => _achievementsOldestFirst;
            set => SetValue(ref _achievementsOldestFirst, value);
        }

        /// <summary>
        /// Achievements sorted by rarity ascending (rarest first).
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> AchievementsRarityAsc
        {
            get => _achievementsRarityAsc;
            set => SetValue(ref _achievementsRarityAsc, value);
        }

        /// <summary>
        /// Achievements sorted by rarity descending (common first).
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> AchievementsRarityDesc
        {
            get => _achievementsRarityDesc;
            set => SetValue(ref _achievementsRarityDesc, value);
        }

        #endregion
    }
}

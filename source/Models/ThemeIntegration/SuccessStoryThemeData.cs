using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Data;
using PlayniteAchievements.Models.Achievements;

namespace PlayniteAchievements.Models.ThemeIntegration
{
    /// <summary>
    /// SuccessStory theme compatibility data.
    /// These properties match SuccessStory's naming convention for drop-in theme compatibility.
    /// They are runtime properties only and should not be serialized.
    /// </summary>
    public class SuccessStoryThemeData : ObservableObject
    {
        #region Backing Fields

        [DontSerialize]
        private bool _is100Percent;
        [DontSerialize]
        private AchievementRarityStats _common = new AchievementRarityStats();
        [DontSerialize]
        private AchievementRarityStats _noCommon = new AchievementRarityStats();
        [DontSerialize]
        private AchievementRarityStats _rare = new AchievementRarityStats();
        [DontSerialize]
        private AchievementRarityStats _ultraRare = new AchievementRarityStats();
        [DontSerialize]
        private int _locked;
        [DontSerialize]
        private int _unlocked;
        [DontSerialize]
        private int _totalGamerScore;
        [DontSerialize]
        private string _estimateTimeToUnlock;
        [DontSerialize]
        private List<AchievementDetail> _listAchievements = new List<AchievementDetail>();
        [DontSerialize]
        private List<AchievementDetail> _listAchUnlockDateAsc = new List<AchievementDetail>();
        [DontSerialize]
        private List<AchievementDetail> _listAchUnlockDateDesc = new List<AchievementDetail>();

        #endregion

        /// <summary>
        /// Whether all achievements are unlocked (SuccessStory compatible).
        /// </summary>
        [DontSerialize]
        public bool Is100Percent
        {
            get => _is100Percent;
            set => SetValue(ref _is100Percent, value);
        }

        /// <summary>
        /// Common achievement statistics (SuccessStory compatible).
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats Common
        {
            get => _common;
            set => SetValue(ref _common, value);
        }

        /// <summary>
        /// Uncommon achievement statistics (SuccessStory compatible, named NoCommon in SS).
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats NoCommon
        {
            get => _noCommon;
            set => SetValue(ref _noCommon, value);
        }

        /// <summary>
        /// Rare achievement statistics (SuccessStory compatible).
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats Rare
        {
            get => _rare;
            set => SetValue(ref _rare, value);
        }

        /// <summary>
        /// Ultra Rare achievement statistics (SuccessStory compatible).
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats UltraRare
        {
            get => _ultraRare;
            set => SetValue(ref _ultraRare, value);
        }

        /// <summary>
        /// Number of locked achievements (SuccessStory compatible).
        /// </summary>
        [DontSerialize]
        public int Locked
        {
            get => _locked;
            set => SetValue(ref _locked, value);
        }

        /// <summary>
        /// Number of unlocked achievements (SuccessStory compatible).
        /// </summary>
        [DontSerialize]
        public int Unlocked
        {
            get => _unlocked;
            set => SetValue(ref _unlocked, value);
        }

        /// <summary>
        /// Total gamerscore for Xbox achievements (SuccessStory compatible).
        /// </summary>
        [DontSerialize]
        public int TotalGamerScore
        {
            get => _totalGamerScore;
            set => SetValue(ref _totalGamerScore, value);
        }

        /// <summary>
        /// Estimated time to unlock all achievements (SuccessStory compatible).
        /// </summary>
        [DontSerialize]
        public string EstimateTimeToUnlock
        {
            get => _estimateTimeToUnlock;
            set => SetValue(ref _estimateTimeToUnlock, value);
        }

        /// <summary>
        /// All achievements (SuccessStory compatible).
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> ListAchievements
        {
            get => _listAchievements;
            set => SetValue(ref _listAchievements, value);
        }

        /// <summary>
        /// Achievements sorted by unlock date ascending (SuccessStory compatible).
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> ListAchUnlockDateAsc
        {
            get => _listAchUnlockDateAsc;
            set => SetValue(ref _listAchUnlockDateAsc, value);
        }

        /// <summary>
        /// Achievements sorted by unlock date descending (SuccessStory compatible).
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> ListAchUnlockDateDesc
        {
            get => _listAchUnlockDateDesc;
            set => SetValue(ref _listAchUnlockDateDesc, value);
        }
    }
}

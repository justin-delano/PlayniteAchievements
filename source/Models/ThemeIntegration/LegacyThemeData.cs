using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Data;
using PlayniteAchievements.Models.Achievements;

namespace PlayniteAchievements.Models.ThemeIntegration
{
    /// <summary>
    /// Legacy theme compatibility data for backward compatibility.
    /// Contains SuccessStory compatibility, Aniki ReMake compatibility, and old inline properties.
    /// New themes should prefer the unified ThemeData class.
    /// All properties are runtime-only and should not be serialized.
    /// </summary>
    public class LegacyThemeData : ObservableObject
    {
        #region Backing Fields - Old Inline Properties

        [DontSerialize]
        private bool _hasData;
        [DontSerialize]
        private int _total;
        [DontSerialize]
        private int _unlocked;
        [DontSerialize]
        private double _percent;

        #endregion

        #region Backing Fields - SuccessStory Compatibility

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

        #region Backing Fields - Aniki ReMake Compatibility

        [DontSerialize]
        private ObservableCollection<GameAchievementSummary> _allGamesWithAchievements = new ObservableCollection<GameAchievementSummary>();
        [DontSerialize]
        private ObservableCollection<GameAchievementSummary> _platinumGamesAscending = new ObservableCollection<GameAchievementSummary>();
        [DontSerialize]
        private string _gsTotal = "0";
        [DontSerialize]
        private string _gsPlat = "0";
        [DontSerialize]
        private string _gs90 = "0";
        [DontSerialize]
        private string _gs30 = "0";
        [DontSerialize]
        private string _gs15 = "0";
        [DontSerialize]
        private string _gsScore = "0";
        [DontSerialize]
        private string _gsLevel = "0";
        [DontSerialize]
        private double _gsLevelProgress = 0;
        [DontSerialize]
        private string _gsRank = "Bronze1";

        #endregion

        #region Old Inline Properties

        /// <summary>
        /// Whether achievement data is available (legacy inline property).
        /// </summary>
        [DontSerialize]
        public bool HasData
        {
            get => _hasData;
            set => SetValue(ref _hasData, value);
        }

        /// <summary>
        /// Total achievement count (legacy inline property).
        /// </summary>
        [DontSerialize]
        public int Total
        {
            get => _total;
            set => SetValue(ref _total, value);
        }

        /// <summary>
        /// Unlocked achievement count (legacy inline property).
        /// </summary>
        [DontSerialize]
        public int Unlocked
        {
            get => _unlocked;
            set => SetValue(ref _unlocked, value);
        }

        /// <summary>
        /// Achievement progress percentage (legacy inline property).
        /// </summary>
        [DontSerialize]
        public double Percent
        {
            get => _percent;
            set => SetValue(ref _percent, value);
        }

        #endregion

        #region SuccessStory Compatibility Properties

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

        #endregion

        #region Aniki ReMake Compatibility Properties

        /// <summary>
        /// All games with achievements (Aniki ReMake compatibility).
        /// </summary>
        [DontSerialize]
        public ObservableCollection<GameAchievementSummary> AllGamesWithAchievements
        {
            get => _allGamesWithAchievements;
            set => SetValue(ref _allGamesWithAchievements, value);
        }

        /// <summary>
        /// Games with 100% completion sorted ascending (Aniki ReMake compatibility).
        /// </summary>
        [DontSerialize]
        public ObservableCollection<GameAchievementSummary> PlatinumGamesAscending
        {
            get => _platinumGamesAscending;
            set => SetValue(ref _platinumGamesAscending, value);
        }

        /// <summary>
        /// Total trophy count (string format for Aniki ReMake compatibility).
        /// </summary>
        [DontSerialize]
        public string GSTotal
        {
            get => _gsTotal;
            set => SetValue(ref _gsTotal, value ?? "0");
        }

        /// <summary>
        /// Platinum trophy count (string format for Aniki ReMake compatibility).
        /// </summary>
        [DontSerialize]
        public string GSPlat
        {
            get => _gsPlat;
            set => SetValue(ref _gsPlat, value ?? "0");
        }

        /// <summary>
        /// Gold trophy count (string format for Aniki ReMake compatibility).
        /// </summary>
        [DontSerialize]
        public string GS90
        {
            get => _gs90;
            set => SetValue(ref _gs90, value ?? "0");
        }

        /// <summary>
        /// Silver trophy count (string format for Aniki ReMake compatibility).
        /// </summary>
        [DontSerialize]
        public string GS30
        {
            get => _gs30;
            set => SetValue(ref _gs30, value ?? "0");
        }

        /// <summary>
        /// Bronze trophy count (string format for Aniki ReMake compatibility).
        /// </summary>
        [DontSerialize]
        public string GS15
        {
            get => _gs15;
            set => SetValue(ref _gs15, value ?? "0");
        }

        /// <summary>
        /// Total gamer score (for Aniki ReMake compatibility).
        /// </summary>
        [DontSerialize]
        public string GSScore
        {
            get => _gsScore;
            set => SetValue(ref _gsScore, value ?? "0");
        }

        /// <summary>
        /// Player level (string format for Aniki ReMake compatibility).
        /// </summary>
        [DontSerialize]
        public string GSLevel
        {
            get => _gsLevel;
            set => SetValue(ref _gsLevel, value ?? "0");
        }

        /// <summary>
        /// Progress toward next level (for Aniki ReMake compatibility).
        /// </summary>
        [DontSerialize]
        public double GSLevelProgress
        {
            get => _gsLevelProgress;
            set => SetValue(ref _gsLevelProgress, value);
        }

        /// <summary>
        /// Player rank (for Aniki ReMake compatibility).
        /// </summary>
        [DontSerialize]
        public string GSRank
        {
            get => _gsRank;
            set => SetValue(ref _gsRank, value ?? "Bronze1");
        }

        #endregion
    }
}

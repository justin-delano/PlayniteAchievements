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
        private ObservableCollection<GameAchievementSummary> _gamesWithAchievements = new ObservableCollection<GameAchievementSummary>();
        [DontSerialize]
        private ObservableCollection<GameAchievementSummary> _platinumGames = new ObservableCollection<GameAchievementSummary>();
        [DontSerialize]
        private ObservableCollection<GameAchievementSummary> _platinumGamesAscending = new ObservableCollection<GameAchievementSummary>();
        [DontSerialize]
        private bool _hasDataAllGames;
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
        /// Whether all-games achievement overview data is available.
        /// </summary>
        [DontSerialize]
        public bool HasDataAllGames
        {
            get => _hasDataAllGames;
            set => SetValue(ref _hasDataAllGames, value);
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
        /// Provider-completed games (for example, PSN platinum earned), sorted by last unlock date (newest first).
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
            set => SetValue(ref _totalTrophies, value < 0 ? 0 : value);
        }

        /// <summary>
        /// Number of provider-completed games in the platinum list.
        /// </summary>
        [DontSerialize]
        public int PlatinumTrophies
        {
            get => _platinumTrophies;
            set => SetValue(ref _platinumTrophies, value < 0 ? 0 : value);
        }

        /// <summary>
        /// Gold trophy count (ultra-rare achievements).
        /// </summary>
        [DontSerialize]
        public int GoldTrophies
        {
            get => _goldTrophies;
            set => SetValue(ref _goldTrophies, value < 0 ? 0 : value);
        }

        /// <summary>
        /// Silver trophy count (uncommon achievements).
        /// </summary>
        [DontSerialize]
        public int SilverTrophies
        {
            get => _silverTrophies;
            set => SetValue(ref _silverTrophies, value < 0 ? 0 : value);
        }

        /// <summary>
        /// Bronze trophy count (common achievements).
        /// </summary>
        [DontSerialize]
        public int BronzeTrophies
        {
            get => _bronzeTrophies;
            set => SetValue(ref _bronzeTrophies, value < 0 ? 0 : value);
        }

        /// <summary>
        /// Player level calculated from total score.
        /// </summary>
        [DontSerialize]
        public int Level
        {
            get => _level;
            set => SetValue(ref _level, value < 0 ? 0 : value);
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
        /// Provider-completed games sorted ascending (Aniki ReMake compatibility).
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

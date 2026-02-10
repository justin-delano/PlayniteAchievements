using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Playnite.SDK;
using Playnite.SDK.Data;
using PlayniteAchievements.Models.Achievements;

namespace PlayniteAchievements.Models.ThemeIntegration
{
    /// <summary>
    /// Fullscreen theme integration data for all-games overview.
    /// Contains both Aniki ReMake compatibility properties and native fullscreen properties.
    /// All properties are runtime-only and should not be serialized.
    /// </summary>
    public class FullscreenThemeData : ObservableObject
    {
        #region Backing Fields

        [DontSerialize]
        private ObservableCollection<FullscreenAchievementGameItem> _allGamesWithAchievements = new ObservableCollection<FullscreenAchievementGameItem>();
        [DontSerialize]
        private ObservableCollection<FullscreenAchievementGameItem> _platinumGames = new ObservableCollection<FullscreenAchievementGameItem>();
        [DontSerialize]
        private ObservableCollection<FullscreenAchievementGameItem> _platinumGamesAscending = new ObservableCollection<FullscreenAchievementGameItem>();
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

        [DontSerialize]
        private bool _fullscreenHasData;
        [DontSerialize]
        private ObservableCollection<FullscreenAchievementGameItem> _fullscreenGamesWithAchievements = new ObservableCollection<FullscreenAchievementGameItem>();
        [DontSerialize]
        private ObservableCollection<FullscreenAchievementGameItem> _fullscreenPlatinumGames = new ObservableCollection<FullscreenAchievementGameItem>();
        [DontSerialize]
        private int _fullscreenTotalTrophies;
        [DontSerialize]
        private int _fullscreenPlatinumTrophies;
        [DontSerialize]
        private int _fullscreenGoldTrophies;
        [DontSerialize]
        private int _fullscreenSilverTrophies;
        [DontSerialize]
        private int _fullscreenBronzeTrophies;
        [DontSerialize]
        private int _fullscreenLevel;
        [DontSerialize]
        private double _fullscreenLevelProgress;
        [DontSerialize]
        private string _fullscreenRank = "Bronze1";

        [DontSerialize]
        private List<AchievementDetail> _allAchievementsUnlockAsc = new List<AchievementDetail>();
        [DontSerialize]
        private List<AchievementDetail> _allAchievementsUnlockDesc = new List<AchievementDetail>();
        [DontSerialize]
        private List<AchievementDetail> _allAchievementsRarityAsc = new List<AchievementDetail>();
        [DontSerialize]
        private List<AchievementDetail> _allAchievementsRarityDesc = new List<AchievementDetail>();

        #endregion

        #region Aniki ReMake Compatibility (SuccessStory Fullscreen)

        /// <summary>
        /// All games with achievements, sorted by last unlock date.
        /// Aniki ReMake compatibility property.
        /// </summary>
        [DontSerialize]
        public ObservableCollection<FullscreenAchievementGameItem> AllGamesWithAchievements
        {
            get => _allGamesWithAchievements;
            set => SetValue(ref _allGamesWithAchievements, value);
        }

        /// <summary>
        /// Games with 100% achievement completion, sorted by last unlock date (newest first).
        /// Aniki ReMake compatibility property.
        /// </summary>
        [DontSerialize]
        public ObservableCollection<FullscreenAchievementGameItem> PlatinumGames
        {
            get => _platinumGames;
            set => SetValue(ref _platinumGames, value);
        }

        /// <summary>
        /// Games with 100% achievement completion, sorted by last unlock date (oldest first).
        /// Aniki ReMake compatibility property.
        /// </summary>
        [DontSerialize]
        public ObservableCollection<FullscreenAchievementGameItem> PlatinumGamesAscending
        {
            get => _platinumGamesAscending;
            set => SetValue(ref _platinumGamesAscending, value);
        }

        /// <summary>
        /// Total trophy count across all games.
        /// Aniki ReMake compatibility property.
        /// </summary>
        [DontSerialize]
        public string GSTotal
        {
            get => _gsTotal;
            set => SetValue(ref _gsTotal, value ?? "0");
        }

        /// <summary>
        /// Platinum trophy count (100% completed games).
        /// Aniki ReMake compatibility property.
        /// </summary>
        [DontSerialize]
        public string GSPlat
        {
            get => _gsPlat;
            set => SetValue(ref _gsPlat, value ?? "0");
        }

        /// <summary>
        /// Gold trophy count (ultra-rare achievements).
        /// Aniki ReMake compatibility property.
        /// </summary>
        [DontSerialize]
        public string GS90
        {
            get => _gs90;
            set => SetValue(ref _gs90, value ?? "0");
        }

        /// <summary>
        /// Silver trophy count (uncommon achievements).
        /// Aniki ReMake compatibility property.
        /// </summary>
        [DontSerialize]
        public string GS30
        {
            get => _gs30;
            set => SetValue(ref _gs30, value ?? "0");
        }

        /// <summary>
        /// Bronze trophy count (common achievements).
        /// Aniki ReMake compatibility property.
        /// </summary>
        [DontSerialize]
        public string GS15
        {
            get => _gs15;
            set => SetValue(ref _gs15, value ?? "0");
        }

        /// <summary>
        /// Total gamer score calculated from trophy counts.
        /// Aniki ReMake compatibility property.
        /// </summary>
        [DontSerialize]
        public string GSScore
        {
            get => _gsScore;
            set => SetValue(ref _gsScore, value ?? "0");
        }

        /// <summary>
        /// Player level calculated from total score.
        /// Aniki ReMake compatibility property.
        /// </summary>
        [DontSerialize]
        public string GSLevel
        {
            get => _gsLevel;
            set => SetValue(ref _gsLevel, value ?? "0");
        }

        /// <summary>
        /// Progress toward next level (0-100).
        /// Aniki ReMake compatibility property.
        /// </summary>
        [DontSerialize]
        public double GSLevelProgress
        {
            get => _gsLevelProgress;
            set => SetValue(ref _gsLevelProgress, value);
        }

        /// <summary>
        /// Player rank based on level (Bronze1 through Plat).
        /// Aniki ReMake compatibility property.
        /// </summary>
        [DontSerialize]
        public string GSRank
        {
            get => _gsRank;
            set => SetValue(ref _gsRank, value ?? "Bronze1");
        }

        #endregion

        #region Native Fullscreen Properties

        /// <summary>
        /// Whether fullscreen achievement data is available.
        /// </summary>
        [DontSerialize]
        public bool FullscreenHasData
        {
            get => _fullscreenHasData;
            set => SetValue(ref _fullscreenHasData, value);
        }

        /// <summary>
        /// All games with achievements, sorted by last unlock date.
        /// Native fullscreen theme property.
        /// </summary>
        [DontSerialize]
        public ObservableCollection<FullscreenAchievementGameItem> FullscreenGamesWithAchievements
        {
            get => _fullscreenGamesWithAchievements;
            set => SetValue(ref _fullscreenGamesWithAchievements, value);
        }

        /// <summary>
        /// Games with 100% achievement completion, sorted by last unlock date (newest first).
        /// Native fullscreen theme property.
        /// </summary>
        [DontSerialize]
        public ObservableCollection<FullscreenAchievementGameItem> FullscreenPlatinumGames
        {
            get => _fullscreenPlatinumGames;
            set => SetValue(ref _fullscreenPlatinumGames, value);
        }

        /// <summary>
        /// Total trophy count across all games.
        /// </summary>
        [DontSerialize]
        public int FullscreenTotalTrophies
        {
            get => _fullscreenTotalTrophies;
            set => SetValue(ref _fullscreenTotalTrophies, Math.Max(0, value));
        }

        /// <summary>
        /// Platinum trophy count (100% completed games).
        /// </summary>
        [DontSerialize]
        public int FullscreenPlatinumTrophies
        {
            get => _fullscreenPlatinumTrophies;
            set => SetValue(ref _fullscreenPlatinumTrophies, Math.Max(0, value));
        }

        /// <summary>
        /// Gold trophy count (ultra-rare achievements).
        /// </summary>
        [DontSerialize]
        public int FullscreenGoldTrophies
        {
            get => _fullscreenGoldTrophies;
            set => SetValue(ref _fullscreenGoldTrophies, Math.Max(0, value));
        }

        /// <summary>
        /// Silver trophy count (uncommon achievements).
        /// </summary>
        [DontSerialize]
        public int FullscreenSilverTrophies
        {
            get => _fullscreenSilverTrophies;
            set => SetValue(ref _fullscreenSilverTrophies, Math.Max(0, value));
        }

        /// <summary>
        /// Bronze trophy count (common achievements).
        /// </summary>
        [DontSerialize]
        public int FullscreenBronzeTrophies
        {
            get => _fullscreenBronzeTrophies;
            set => SetValue(ref _fullscreenBronzeTrophies, Math.Max(0, value));
        }

        /// <summary>
        /// Player level calculated from total score.
        /// </summary>
        [DontSerialize]
        public int FullscreenLevel
        {
            get => _fullscreenLevel;
            set => SetValue(ref _fullscreenLevel, Math.Max(0, value));
        }

        /// <summary>
        /// Progress toward next level (0-100).
        /// </summary>
        [DontSerialize]
        public double FullscreenLevelProgress
        {
            get => _fullscreenLevelProgress;
            set => SetValue(ref _fullscreenLevelProgress, value);
        }

        /// <summary>
        /// Player rank based on level (Bronze1 through Plat).
        /// </summary>
        [DontSerialize]
        public string FullscreenRank
        {
            get => _fullscreenRank;
            set => SetValue(ref _fullscreenRank, value ?? "Bronze1");
        }

        #endregion

        #region All-Games Achievement Lists

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

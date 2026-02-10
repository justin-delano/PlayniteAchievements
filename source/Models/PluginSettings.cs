using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Models.ThemeIntegration;

using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.Models
{
    /// <summary>
    /// Main settings class for PlayniteAchievements plugin.
    /// This class contains two kinds of members:
    /// 1) Persisted configuration (via PersistedSettings property, saved via Playnite's plugin settings JSON).
    /// 2) Runtime/theme integration surfaces (updated live, never serialized).
    ///
    /// Theme integration data is organized into:
    /// - Persisted: User-configurable settings
    /// - Theme: Unified modern theme integration data (per-game + all-games overview)
    /// - LegacyTheme: Legacy compatibility data (SuccessStory, Aniki ReMake, old inline properties)
    ///
    /// Keep runtime/theme members clearly marked with [DontSerialize] on both the backing field
    /// and the public property to prevent settings bloat and improve performance.
    /// </summary>
    public class PlayniteAchievementsSettings : ObservableObject
    {
        private static readonly List<AchievementDetail> EmptyAchievementList = new List<AchievementDetail>();
        private static readonly AchievementRarityStats EmptyRarityStats = new AchievementRarityStats();

        #region Composition Properties

        /// <summary>
        /// Persisted user settings (serialized to plugin settings JSON).
        /// </summary>
        public PersistedSettings Persisted { get; private set; }

        /// <summary>
        /// Unified modern theme integration data (runtime, not serialized).
        /// Contains both per-game achievement data and all-games overview data.
        /// </summary>
        [DontSerialize]
        public ThemeData Theme { get; private set; }

        /// <summary>
        /// Legacy theme compatibility data (runtime, not serialized).
        /// Contains SuccessStory compatibility, Aniki ReMake compatibility, and old inline properties.
        /// </summary>
        [DontSerialize]
        public LegacyThemeData LegacyTheme { get; private set; }

        #endregion

        #region Commands

        [DontSerialize]
        private ICommand _openFullscreenAchievementWindow;
        [DontSerialize]
        private ICommand _singleGameRefreshCommand;
        [DontSerialize]
        private ICommand _quickRefreshCommand;
        [DontSerialize]
        private ICommand _favoritesRefreshCommand;
        [DontSerialize]
        private ICommand _fullRefreshCommand;
        [DontSerialize]
        private ICommand _installedRefreshCommand;
        [DontSerialize]
        private ICommand _openAchievementWindow;
        [DontSerialize]
        private ICommand _openGameAchievementWindow;
        [DontSerialize]
        private ICommand _refreshSelectedGameCommand;

        [DontSerialize]
        public ICommand OpenFullscreenAchievementWindow
        {
            get => _openFullscreenAchievementWindow;
            set => SetValue(ref _openFullscreenAchievementWindow, value);
        }

        [DontSerialize]
        public ICommand SingleGameRefreshCommand
        {
            get => _singleGameRefreshCommand;
            set => SetValue(ref _singleGameRefreshCommand, value);
        }

        [DontSerialize]
        public ICommand QuickRefreshCommand
        {
            get => _quickRefreshCommand;
            set => SetValue(ref _quickRefreshCommand, value);
        }

        [DontSerialize]
        public ICommand FavoritesRefreshCommand
        {
            get => _favoritesRefreshCommand;
            set => SetValue(ref _favoritesRefreshCommand, value);
        }

        [DontSerialize]
        public ICommand FullRefreshCommand
        {
            get => _fullRefreshCommand;
            set => SetValue(ref _fullRefreshCommand, value);
        }

        [DontSerialize]
        public ICommand InstalledRefreshCommand
        {
            get => _installedRefreshCommand;
            set => SetValue(ref _installedRefreshCommand, value);
        }

        [DontSerialize]
        public ICommand OpenAchievementWindow
        {
            get => _openAchievementWindow;
            set => SetValue(ref _openAchievementWindow, value);
        }

        [DontSerialize]
        public ICommand OpenGameAchievementWindow
        {
            get => _openGameAchievementWindow;
            set => SetValue(ref _openGameAchievementWindow, value);
        }

        [DontSerialize]
        public ICommand RefreshSelectedGameCommand
        {
            get => _refreshSelectedGameCommand;
            set => SetValue(ref _refreshSelectedGameCommand, value);
        }

        #endregion

        #region Modern Theme Integration (delegates to Theme)

        // === Per-Game Achievement Data ===

        [DontSerialize]
        public bool HasAchievements => Theme.HasAchievements;

        [DontSerialize]
        public int AchievementCount => Theme.AchievementCount;

        [DontSerialize]
        public int UnlockedCount => Theme.UnlockedCount;

        [DontSerialize]
        public int LockedCount => Theme.LockedCount;

        [DontSerialize]
        public double ProgressPercentage => Theme.ProgressPercentage;

        [DontSerialize]
        public bool AllUnlocked => Theme.AllUnlocked;

        [DontSerialize]
        public List<AchievementDetail> Achievements => Theme.AllAchievements ?? EmptyAchievementList;

        [DontSerialize]
        public List<AchievementDetail> AchievementsNewestFirst => Theme.AchievementsNewestFirst ?? EmptyAchievementList;

        [DontSerialize]
        public List<AchievementDetail> AchievementsOldestFirst => Theme.AchievementsOldestFirst ?? EmptyAchievementList;

        [DontSerialize]
        public List<AchievementDetail> AchievementsRarityAsc => Theme.AchievementsRarityAsc ?? EmptyAchievementList;

        [DontSerialize]
        public List<AchievementDetail> AchievementsRarityDesc => Theme.AchievementsRarityDesc ?? EmptyAchievementList;

        [DontSerialize]
        public AchievementRarityStats CommonStats => Theme.CommonStats ?? EmptyRarityStats;

        [DontSerialize]
        public AchievementRarityStats UncommonStats => Theme.UncommonStats ?? EmptyRarityStats;

        [DontSerialize]
        public AchievementRarityStats RareStats => Theme.RareStats ?? EmptyRarityStats;

        [DontSerialize]
        public AchievementRarityStats UltraRareStats => Theme.UltraRareStats ?? EmptyRarityStats;

        // === All-Games Overview Data ===

        [DontSerialize]
        public bool HasData
        {
            get => Theme.HasData;
            set => Theme.HasData = value;
        }

        [DontSerialize]
        public ObservableCollection<GameAchievementSummary> GamesWithAchievements
        {
            get => Theme.GamesWithAchievements;
            set => Theme.GamesWithAchievements = value;
        }

        [DontSerialize]
        public ObservableCollection<GameAchievementSummary> PlatinumGames
        {
            get => Theme.PlatinumGames;
            set => Theme.PlatinumGames = value;
        }

        [DontSerialize]
        public int TotalTrophies
        {
            get => Theme.TotalTrophies;
            set => Theme.TotalTrophies = value;
        }

        [DontSerialize]
        public int PlatinumTrophies
        {
            get => Theme.PlatinumTrophies;
            set => Theme.PlatinumTrophies = value;
        }

        [DontSerialize]
        public int GoldTrophies
        {
            get => Theme.GoldTrophies;
            set => Theme.GoldTrophies = value;
        }

        [DontSerialize]
        public int SilverTrophies
        {
            get => Theme.SilverTrophies;
            set => Theme.SilverTrophies = value;
        }

        [DontSerialize]
        public int BronzeTrophies
        {
            get => Theme.BronzeTrophies;
            set => Theme.BronzeTrophies = value;
        }

        [DontSerialize]
        public int Level
        {
            get => Theme.Level;
            set => Theme.Level = value;
        }

        [DontSerialize]
        public double LevelProgress
        {
            get => Theme.LevelProgress;
            set => Theme.LevelProgress = value;
        }

        [DontSerialize]
        public string Rank
        {
            get => Theme.Rank;
            set => Theme.Rank = value;
        }

        [DontSerialize]
        public List<AchievementDetail> AllAchievementsUnlockAsc
        {
            get => Theme.AllAchievementsUnlockAsc ?? EmptyAchievementList;
            set => Theme.AllAchievementsUnlockAsc = value;
        }

        [DontSerialize]
        public List<AchievementDetail> AllAchievementsUnlockDesc
        {
            get => Theme.AllAchievementsUnlockDesc ?? EmptyAchievementList;
            set => Theme.AllAchievementsUnlockDesc = value;
        }

        [DontSerialize]
        public List<AchievementDetail> AllAchievementsRarityAsc
        {
            get => Theme.AllAchievementsRarityAsc ?? EmptyAchievementList;
            set => Theme.AllAchievementsRarityAsc = value;
        }

        [DontSerialize]
        public List<AchievementDetail> AllAchievementsRarityDesc
        {
            get => Theme.AllAchievementsRarityDesc ?? EmptyAchievementList;
            set => Theme.AllAchievementsRarityDesc = value;
        }

        #endregion

        #region Legacy Theme Compatibility (delegates to LegacyTheme)

        // === SuccessStory Compatibility ===

        [DontSerialize]
        public bool EnableIntegrationCompactUnlocked => true;

        [DontSerialize]
        public bool EnableIntegrationCompactLocked => true;

        [DontSerialize]
        public bool Is100Percent => LegacyTheme.Is100Percent;

        [DontSerialize]
        public int Locked => LegacyTheme.Locked;

        [DontSerialize]
        public AchievementRarityStats Common => LegacyTheme.Common ?? EmptyRarityStats;

        [DontSerialize]
        public AchievementRarityStats NoCommon => LegacyTheme.NoCommon ?? EmptyRarityStats;

        [DontSerialize]
        public AchievementRarityStats Rare => LegacyTheme.Rare ?? EmptyRarityStats;

        [DontSerialize]
        public AchievementRarityStats UltraRare => LegacyTheme.UltraRare ?? EmptyRarityStats;

        [DontSerialize]
        public List<AchievementDetail> ListAchievements => LegacyTheme.ListAchievements ?? EmptyAchievementList;

        [DontSerialize]
        public List<AchievementDetail> ListAchUnlockDateAsc => LegacyTheme.ListAchUnlockDateAsc ?? EmptyAchievementList;

        [DontSerialize]
        public List<AchievementDetail> ListAchUnlockDateDesc => LegacyTheme.ListAchUnlockDateDesc ?? EmptyAchievementList;

        // === Old Inline Properties (delegates to LegacyTheme) ===

        [DontSerialize]
        public bool HasDataLegacy
        {
            get => LegacyTheme.HasData;
            set => LegacyTheme.HasData = value;
        }

        [DontSerialize]
        public int Total
        {
            get => LegacyTheme.Total;
            set => LegacyTheme.Total = value;
        }

        [DontSerialize]
        public int Unlocked
        {
            get => LegacyTheme.Unlocked;
            set => LegacyTheme.Unlocked = value;
        }

        [DontSerialize]
        public double Percent
        {
            get => LegacyTheme.Percent;
            set => LegacyTheme.Percent = value;
        }

        // === Aniki ReMake Compatibility (delegates to LegacyTheme) ===

        [DontSerialize]
        public ObservableCollection<GameAchievementSummary> AllGamesWithAchievements
        {
            get => LegacyTheme.AllGamesWithAchievements;
            set => LegacyTheme.AllGamesWithAchievements = value;
        }

        [DontSerialize]
        public ObservableCollection<GameAchievementSummary> PlatinumGamesAscending
        {
            get => LegacyTheme.PlatinumGamesAscending;
            set => LegacyTheme.PlatinumGamesAscending = value;
        }

        [DontSerialize]
        public string GSTotal
        {
            get => LegacyTheme.GSTotal;
            set => LegacyTheme.GSTotal = value;
        }

        [DontSerialize]
        public string GSPlat
        {
            get => LegacyTheme.GSPlat;
            set => LegacyTheme.GSPlat = value;
        }

        [DontSerialize]
        public string GS90
        {
            get => LegacyTheme.GS90;
            set => LegacyTheme.GS90 = value;
        }

        [DontSerialize]
        public string GS30
        {
            get => LegacyTheme.GS30;
            set => LegacyTheme.GS30 = value;
        }

        [DontSerialize]
        public string GS15
        {
            get => LegacyTheme.GS15;
            set => LegacyTheme.GS15 = value;
        }

        [DontSerialize]
        public string GSScore
        {
            get => LegacyTheme.GSScore;
            set => LegacyTheme.GSScore = value;
        }

        [DontSerialize]
        public string GSLevel
        {
            get => LegacyTheme.GSLevel;
            set => LegacyTheme.GSLevel = value;
        }

        [DontSerialize]
        public double GSLevelProgress
        {
            get => LegacyTheme.GSLevelProgress;
            set => LegacyTheme.GSLevelProgress = value;
        }

        [DontSerialize]
        public string GSRank
        {
            get => LegacyTheme.GSRank;
            set => LegacyTheme.GSRank = value;
        }

        #endregion

        #region Internal Fields and Methods

        [DontSerialize]
        internal PlayniteAchievementsPlugin _plugin;

        /// <summary>
        /// Copies persisted settings from another settings instance.
        /// Used by the ViewModel when applying settings changes.
        /// </summary>
        internal void CopyPersistedFrom(PlayniteAchievementsSettings other)
        {
            if (other == null)
            {
                return;
            }

            // Copy the entire PersistedSettings object
            Persisted = other.Persisted?.Clone() ?? new PersistedSettings();
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Parameterless constructor for deserialization.
        /// Initializes all composition properties with default instances.
        /// </summary>
        public PlayniteAchievementsSettings()
        {
            Persisted = new PersistedSettings();
            Theme = new ThemeData();
            LegacyTheme = new LegacyThemeData();
        }

        /// <summary>
        /// Constructor for initialization with plugin reference.
        /// Initializes all composition properties with default instances.
        /// </summary>
        public PlayniteAchievementsSettings(PlayniteAchievementsPlugin plugin) : this()
        {
            _plugin = plugin;
        }

        #endregion

        #region Game Context

        [DontSerialize]
        private Game _selectedGame;

        /// <summary>
        /// The currently selected game in Playnite's main view.
        /// Exposed for fullscreen themes to bind backgrounds, covers, logos, etc.
        /// Similar to SuccessStory's GameContext property.
        ///
        /// IMPORTANT: This is a stored property that gets set when OnGameSelected is called,
        /// not a computed property that queries MainView.SelectedGames. This ensures that
        /// fullscreen themes receive the correct game context even when MainView.SelectedGames
        /// may not be synchronized with the game context passed to GameContextChanged.
        /// </summary>
        [DontSerialize]
        public Game SelectedGame
        {
            get => _selectedGame;
            set
            {
                if (value != _selectedGame)
                {
                    _selectedGame = value;
                    OnPropertyChanged(nameof(SelectedGame));
                    OnPropertyChanged(nameof(SelectedGameCoverPath));
                    OnPropertyChanged(nameof(SelectedGameBackgroundPath));
                }
            }
        }

        /// <summary>
        /// Full file path to the selected game's cover image.
        /// Themes can bind to this property to get the cover image path.
        /// </summary>
        [DontSerialize]
        public string SelectedGameCoverPath => _selectedGame != null && !string.IsNullOrWhiteSpace(_selectedGame.CoverImage)
            ? _plugin?.PlayniteApi?.Database?.GetFullFilePath(_selectedGame.CoverImage)
            : null;

        [DontSerialize]
        public string SelectedGameBackgroundPath => _selectedGame != null && !string.IsNullOrWhiteSpace(_selectedGame.BackgroundImage)
            ? _plugin?.PlayniteApi?.Database?.GetFullFilePath(_selectedGame.BackgroundImage)
            : null;

        #endregion
    }
}

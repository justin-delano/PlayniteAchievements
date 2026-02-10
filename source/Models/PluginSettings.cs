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
    /// - SuccessStoryTheme: SuccessStory-compatible theme data
    /// - NativeTheme: Native theme integration data
    /// - Fullscreen properties: Inline properties for legacy fullscreen themes (Aniki ReMake compatibility)
    ///
    /// Keep runtime/theme members clearly marked with [DontSerialize] on both the backing field
    /// and the public property to prevent settings bloat and improve performance.
    /// </summary>
    public class PlayniteAchievementsSettings : ObservableObject
    {
        private static readonly List<AchievementDetail> EmptyAchievementList = new List<AchievementDetail>();
        private static readonly AchievementRarityStats EmptyRarityStats = new AchievementRarityStats();
        private static readonly string[] SuccessStoryForwardAll = new[]
        {
            nameof(Is100Percent),
            nameof(Locked),
            nameof(Common),
            nameof(NoCommon),
            nameof(Rare),
            nameof(UltraRare),
            nameof(ListAchievements),
            nameof(ListAchUnlockDateAsc),
            nameof(ListAchUnlockDateDesc)
        };

        private static readonly IReadOnlyDictionary<string, string[]> SuccessStoryForwardMap =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { nameof(SuccessStoryThemeData.Is100Percent), new[] { nameof(Is100Percent) } },
                { nameof(SuccessStoryThemeData.Locked), new[] { nameof(Locked) } },
                { nameof(SuccessStoryThemeData.Common), new[] { nameof(Common) } },
                { nameof(SuccessStoryThemeData.NoCommon), new[] { nameof(NoCommon) } },
                { nameof(SuccessStoryThemeData.Rare), new[] { nameof(Rare) } },
                { nameof(SuccessStoryThemeData.UltraRare), new[] { nameof(UltraRare) } },
                { nameof(SuccessStoryThemeData.ListAchievements), new[] { nameof(ListAchievements) } },
                { nameof(SuccessStoryThemeData.ListAchUnlockDateAsc), new[] { nameof(ListAchUnlockDateAsc) } },
                { nameof(SuccessStoryThemeData.ListAchUnlockDateDesc), new[] { nameof(ListAchUnlockDateDesc) } }
            };

        #region Composition Properties

        /// <summary>
        /// Persisted user settings (serialized to plugin settings JSON).
        /// </summary>
        public PersistedSettings Persisted { get; private set; }

        /// <summary>
        /// SuccessStory theme compatibility data (runtime, not serialized).
        /// </summary>
        [DontSerialize]
        public SuccessStoryThemeData SuccessStoryTheme { get; private set; }

        /// <summary>
        /// Native theme integration data (runtime, not serialized).
        /// </summary>
        [DontSerialize]
        public NativeThemeData NativeTheme { get; private set; }

        /// <summary>
        /// Fullscreen theme integration data (runtime, not serialized).
        /// </summary>
        [DontSerialize]
        public FullscreenThemeData FullscreenTheme { get; private set; }

        #endregion

        #region Desktop Theme Integration

        /// <summary>
        /// Modern desktop theme integration properties.
        /// These properties delegate to NativeThemeData for per-game achievement data.
        /// Use these for new desktop themes targeting PlayniteAchievements native integration.
        /// </summary>

        // Per-game achievement counts
        [DontSerialize]
        public bool HasAchievements => NativeTheme.HasAchievements;

        [DontSerialize]
        public int AchievementCount => NativeTheme.AchievementCount;

        [DontSerialize]
        public int UnlockedCount => NativeTheme.UnlockedCount;

        [DontSerialize]
        public int LockedCount => NativeTheme.LockedCount;

        [DontSerialize]
        public double ProgressPercentage => NativeTheme.ProgressPercentage;

        [DontSerialize]
        public bool AllUnlocked => NativeTheme.AllUnlocked;

        // Per-game achievement lists
        [DontSerialize]
        public List<AchievementDetail> Achievements => NativeTheme.AllAchievements ?? EmptyAchievementList;

        [DontSerialize]
        public List<AchievementDetail> AchievementsNewestFirst => NativeTheme.AchievementsNewestFirst ?? EmptyAchievementList;

        [DontSerialize]
        public List<AchievementDetail> AchievementsOldestFirst => NativeTheme.AchievementsOldestFirst ?? EmptyAchievementList;

        [DontSerialize]
        public List<AchievementDetail> AchievementsRarityAsc => NativeTheme.AchievementsRarityAsc ?? EmptyAchievementList;

        [DontSerialize]
        public List<AchievementDetail> AchievementsRarityDesc => NativeTheme.AchievementsRarityDesc ?? EmptyAchievementList;

        // Per-game rarity stats
        [DontSerialize]
        public AchievementRarityStats CommonStats => NativeTheme.CommonStats ?? EmptyRarityStats;

        [DontSerialize]
        public AchievementRarityStats UncommonStats => NativeTheme.UncommonStats ?? EmptyRarityStats;

        [DontSerialize]
        public AchievementRarityStats RareStats => NativeTheme.RareStats ?? EmptyRarityStats;

        [DontSerialize]
        public AchievementRarityStats UltraRareStats => NativeTheme.UltraRareStats ?? EmptyRarityStats;

        #endregion

        #region Fullscreen Theme Integration

        /// <summary>
        /// Modern fullscreen theme integration properties.
        /// These properties delegate to FullscreenThemeData for all-games/overview data
        /// and NativeThemeData for per-game data.
        /// Use these for new fullscreen themes targeting PlayniteAchievements native integration.
        /// </summary>

        // Command surfaces
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

        // Fullscreen overview data - delegates to FullscreenThemeData
        [DontSerialize]
        public bool FullscreenHasData
        {
            get => FullscreenTheme.FullscreenHasData;
            set => FullscreenTheme.FullscreenHasData = value;
        }

        [DontSerialize]
        public ObservableCollection<FullscreenAchievementGameItem> FullscreenGamesWithAchievements
        {
            get => FullscreenTheme.FullscreenGamesWithAchievements;
            set => FullscreenTheme.FullscreenGamesWithAchievements = value;
        }

        [DontSerialize]
        public ObservableCollection<FullscreenAchievementGameItem> FullscreenPlatinumGames
        {
            get => FullscreenTheme.FullscreenPlatinumGames;
            set => FullscreenTheme.FullscreenPlatinumGames = value;
        }

        [DontSerialize]
        public int FullscreenTotalTrophies
        {
            get => FullscreenTheme.FullscreenTotalTrophies;
            set => FullscreenTheme.FullscreenTotalTrophies = value;
        }

        [DontSerialize]
        public int FullscreenPlatinumTrophies
        {
            get => FullscreenTheme.FullscreenPlatinumTrophies;
            set => FullscreenTheme.FullscreenPlatinumTrophies = value;
        }

        [DontSerialize]
        public int FullscreenGoldTrophies
        {
            get => FullscreenTheme.FullscreenGoldTrophies;
            set => FullscreenTheme.FullscreenGoldTrophies = value;
        }

        [DontSerialize]
        public int FullscreenSilverTrophies
        {
            get => FullscreenTheme.FullscreenSilverTrophies;
            set => FullscreenTheme.FullscreenSilverTrophies = value;
        }

        [DontSerialize]
        public int FullscreenBronzeTrophies
        {
            get => FullscreenTheme.FullscreenBronzeTrophies;
            set => FullscreenTheme.FullscreenBronzeTrophies = value;
        }

        [DontSerialize]
        public int FullscreenLevel
        {
            get => FullscreenTheme.FullscreenLevel;
            set => FullscreenTheme.FullscreenLevel = value;
        }

        [DontSerialize]
        public double FullscreenLevelProgress
        {
            get => FullscreenTheme.FullscreenLevelProgress;
            set => FullscreenTheme.FullscreenLevelProgress = value;
        }

        [DontSerialize]
        public string FullscreenRank
        {
            get => FullscreenTheme.FullscreenRank;
            set => FullscreenTheme.FullscreenRank = value;
        }

        // Commands
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

        // Single-game achievement lists - delegates to NativeThemeData
        [DontSerialize]
        public List<AchievementDetail> FullscreenSingleGameUnlockAsc => NativeTheme.AchievementsOldestFirst ?? EmptyAchievementList;

        [DontSerialize]
        public List<AchievementDetail> FullscreenSingleGameUnlockDesc => NativeTheme.AchievementsNewestFirst ?? EmptyAchievementList;

        [DontSerialize]
        public List<AchievementDetail> FullscreenSingleGameRarityAsc => NativeTheme.AchievementsRarityAsc ?? EmptyAchievementList;

        [DontSerialize]
        public List<AchievementDetail> FullscreenSingleGameRarityDesc => NativeTheme.AchievementsRarityDesc ?? EmptyAchievementList;

        // All-games achievement lists - delegates to FullscreenThemeData
        [DontSerialize]
        public List<AchievementDetail> FullscreenAllAchievementsUnlockAsc
        {
            get => FullscreenTheme.AllAchievementsUnlockAsc ?? EmptyAchievementList;
            set => FullscreenTheme.AllAchievementsUnlockAsc = value;
        }

        [DontSerialize]
        public List<AchievementDetail> FullscreenAllAchievementsUnlockDesc
        {
            get => FullscreenTheme.AllAchievementsUnlockDesc ?? EmptyAchievementList;
            set => FullscreenTheme.AllAchievementsUnlockDesc = value;
        }

        [DontSerialize]
        public List<AchievementDetail> FullscreenAllAchievementsRarityAsc
        {
            get => FullscreenTheme.AllAchievementsRarityAsc ?? EmptyAchievementList;
            set => FullscreenTheme.AllAchievementsRarityAsc = value;
        }

        [DontSerialize]
        public List<AchievementDetail> FullscreenAllAchievementsRarityDesc
        {
            get => FullscreenTheme.AllAchievementsRarityDesc ?? EmptyAchievementList;
            set => FullscreenTheme.AllAchievementsRarityDesc = value;
        }

        #endregion

        #region Legacy Theme Properties

        /// <summary>
        /// Legacy theme compatibility properties for backward compatibility.
        /// Includes SuccessStory compatibility, Aniki ReMake compatibility, and old inline properties.
        /// New themes should prefer Desktop or Fullscreen sections above.
        /// </summary>

        // Old inline basic properties (for very old themes)
        [DontSerialize]
        private bool _hasData;
        [DontSerialize]
        private int _total;
        [DontSerialize]
        private int _unlocked;
        [DontSerialize]
        private double _percent;

        [DontSerialize]
        public bool HasData
        {
            get => _hasData;
            set => SetValue(ref _hasData, value);
        }

        [DontSerialize]
        public int Total
        {
            get => _total;
            set => SetValue(ref _total, value);
        }

        [DontSerialize]
        public int Unlocked
        {
            get => _unlocked;
            set => SetValue(ref _unlocked, value);
        }

        [DontSerialize]
        public double Percent
        {
            get => _percent;
            set => SetValue(ref _percent, value);
        }

        // SuccessStory compatibility command surfaces
        [DontSerialize]
        private ICommand _openAchievementWindow;
        [DontSerialize]
        private ICommand _openGameAchievementWindow;
        [DontSerialize]
        private ICommand _refreshSelectedGameCommand;

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

        // SuccessStory compatibility properties - delegates to SuccessStoryTheme
        [DontSerialize]
        public bool EnableIntegrationCompactUnlocked => true;

        [DontSerialize]
        public bool EnableIntegrationCompactLocked => true;

        [DontSerialize]
        public bool Is100Percent => SuccessStoryTheme?.Is100Percent ?? false;

        [DontSerialize]
        public int Locked => SuccessStoryTheme?.Locked ?? 0;

        [DontSerialize]
        public AchievementRarityStats Common => SuccessStoryTheme?.Common ?? EmptyRarityStats;

        [DontSerialize]
        public AchievementRarityStats NoCommon => SuccessStoryTheme?.NoCommon ?? EmptyRarityStats;

        [DontSerialize]
        public AchievementRarityStats Rare => SuccessStoryTheme?.Rare ?? EmptyRarityStats;

        [DontSerialize]
        public AchievementRarityStats UltraRare => SuccessStoryTheme?.UltraRare ?? EmptyRarityStats;

        [DontSerialize]
        public List<AchievementDetail> ListAchievements => SuccessStoryTheme?.ListAchievements ?? EmptyAchievementList;

        [DontSerialize]
        public List<AchievementDetail> ListAchUnlockDateAsc => SuccessStoryTheme?.ListAchUnlockDateAsc ?? EmptyAchievementList;

        [DontSerialize]
        public List<AchievementDetail> ListAchUnlockDateDesc => SuccessStoryTheme?.ListAchUnlockDateDesc ?? EmptyAchievementList;

        // Aniki ReMake compatibility properties - delegates to FullscreenTheme
        [DontSerialize]
        public ObservableCollection<FullscreenAchievementGameItem> AllGamesWithAchievements
        {
            get => FullscreenTheme.AllGamesWithAchievements;
            set => FullscreenTheme.AllGamesWithAchievements = value;
        }

        [DontSerialize]
        public ObservableCollection<FullscreenAchievementGameItem> PlatinumGames
        {
            get => FullscreenTheme.PlatinumGames;
            set => FullscreenTheme.PlatinumGames = value;
        }

        [DontSerialize]
        public ObservableCollection<FullscreenAchievementGameItem> PlatinumGamesAscending
        {
            get => FullscreenTheme.PlatinumGamesAscending;
            set => FullscreenTheme.PlatinumGamesAscending = value;
        }

        [DontSerialize]
        public string GSTotal
        {
            get => FullscreenTheme.GSTotal;
            set => FullscreenTheme.GSTotal = value;
        }

        [DontSerialize]
        public string GSPlat
        {
            get => FullscreenTheme.GSPlat;
            set => FullscreenTheme.GSPlat = value;
        }

        [DontSerialize]
        public string GS90
        {
            get => FullscreenTheme.GS90;
            set => FullscreenTheme.GS90 = value;
        }

        [DontSerialize]
        public string GS30
        {
            get => FullscreenTheme.GS30;
            set => FullscreenTheme.GS30 = value;
        }

        [DontSerialize]
        public string GS15
        {
            get => FullscreenTheme.GS15;
            set => FullscreenTheme.GS15 = value;
        }

        [DontSerialize]
        public string GSScore
        {
            get => FullscreenTheme.GSScore;
            set => FullscreenTheme.GSScore = value;
        }

        [DontSerialize]
        public string GSLevel
        {
            get => FullscreenTheme.GSLevel;
            set => FullscreenTheme.GSLevel = value;
        }

        [DontSerialize]
        public double GSLevelProgress
        {
            get => FullscreenTheme.GSLevelProgress;
            set => FullscreenTheme.GSLevelProgress = value;
        }

        [DontSerialize]
        public string GSRank
        {
            get => FullscreenTheme.GSRank;
            set => FullscreenTheme.GSRank = value;
        }

        #endregion

        #region Internal Fields and Methods

        [DontSerialize]
        internal PlayniteAchievementsPlugin _plugin;

        private void AttachSuccessStoryHandlers()
        {
            if (SuccessStoryTheme == null)
            {
                return;
            }

            SuccessStoryTheme.PropertyChanged -= SuccessStoryTheme_PropertyChanged;
            SuccessStoryTheme.PropertyChanged += SuccessStoryTheme_PropertyChanged;
        }

        private void SuccessStoryTheme_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            ForwardPropertyChanged(e, SuccessStoryForwardMap, SuccessStoryForwardAll);
        }

        private void ForwardPropertyChanged(
            PropertyChangedEventArgs e,
            IReadOnlyDictionary<string, string[]> map,
            string[] allTargets)
        {
            if (e == null || string.IsNullOrEmpty(e.PropertyName))
            {
                RaisePropertyChanged(allTargets);
                return;
            }

            if (map != null && map.TryGetValue(e.PropertyName, out var targets))
            {
                RaisePropertyChanged(targets);
            }
        }

        private void RaisePropertyChanged(IEnumerable<string> propertyNames)
        {
            if (propertyNames == null)
            {
                return;
            }

            foreach (var name in propertyNames)
            {
                OnPropertyChanged(name);
            }
        }

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
            SuccessStoryTheme = new SuccessStoryThemeData();
            NativeTheme = new NativeThemeData();
            FullscreenTheme = new FullscreenThemeData();
            AttachSuccessStoryHandlers();
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

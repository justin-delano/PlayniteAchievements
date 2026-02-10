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

        #endregion

        #region Legacy Theme Properties

        /// <summary>
        /// Basic theme properties kept inline for backward compatibility.
        /// These are runtime properties accessed directly by themes.
        /// </summary>
        [DontSerialize]
        private bool _hasData;
        [DontSerialize]
        private int _total;
        [DontSerialize]
        private int _unlocked;
        [DontSerialize]
        private double _percent;

        /// <summary>
        /// Whether achievement data is available for the currently selected game.
        /// </summary>
        [DontSerialize]
        public bool HasData
        {
            get => _hasData;
            set => SetValue(ref _hasData, value);
        }

        /// <summary>
        /// Total number of achievements for the currently selected game.
        /// </summary>
        [DontSerialize]
        public int Total
        {
            get => _total;
            set => SetValue(ref _total, value);
        }

        /// <summary>
        /// Number of unlocked achievements for the currently selected game.
        /// </summary>
        [DontSerialize]
        public int Unlocked
        {
            get => _unlocked;
            set => SetValue(ref _unlocked, value);
        }

        /// <summary>
        /// Percentage of achievements unlocked for the currently selected game.
        /// </summary>
        [DontSerialize]
        public double Percent
        {
            get => _percent;
            set => SetValue(ref _percent, value);
        }

        #endregion

        #region Fullscreen Theme Compatibility (SuccessStory Fullscreen Helper)

        /// <summary>
        /// Fullscreen theme compatibility surface expected by older fullscreen themes (e.g. Aniki ReMake).
        /// These properties are accessed via {PluginSettings Plugin=PlayniteAchievements, Path=...}
        /// All properties in this section are runtime-only and marked with [DontSerialize].
        /// </summary>

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
        private double _gsLevelProgress;
        [DontSerialize]
        private string _gsRank = "Bronze1";
        [DontSerialize]
        private ICommand _openAchievementWindow;
        [DontSerialize]
        private ICommand _openGameAchievementWindow;
        [DontSerialize]
        private ICommand _refreshSelectedGameCommand;

        [DontSerialize]
        public ObservableCollection<FullscreenAchievementGameItem> AllGamesWithAchievements
        {
            get => _allGamesWithAchievements;
            set => SetValue(ref _allGamesWithAchievements, value);
        }

        [DontSerialize]
        public ObservableCollection<FullscreenAchievementGameItem> PlatinumGames
        {
            get => _platinumGames;
            set => SetValue(ref _platinumGames, value);
        }

        [DontSerialize]
        public ObservableCollection<FullscreenAchievementGameItem> PlatinumGamesAscending
        {
            get => _platinumGamesAscending;
            set => SetValue(ref _platinumGamesAscending, value);
        }

        [DontSerialize]
        public string GSTotal
        {
            get => _gsTotal;
            set => SetValue(ref _gsTotal, value ?? "0");
        }

        [DontSerialize]
        public string GSPlat
        {
            get => _gsPlat;
            set => SetValue(ref _gsPlat, value ?? "0");
        }

        [DontSerialize]
        public string GS90
        {
            get => _gs90;
            set => SetValue(ref _gs90, value ?? "0");
        }

        [DontSerialize]
        public string GS30
        {
            get => _gs30;
            set => SetValue(ref _gs30, value ?? "0");
        }

        [DontSerialize]
        public string GS15
        {
            get => _gs15;
            set => SetValue(ref _gs15, value ?? "0");
        }

        [DontSerialize]
        public string GSScore
        {
            get => _gsScore;
            set => SetValue(ref _gsScore, value ?? "0");
        }

        [DontSerialize]
        public string GSLevel
        {
            get => _gsLevel;
            set => SetValue(ref _gsLevel, value ?? "0");
        }

        [DontSerialize]
        public double GSLevelProgress
        {
            get => _gsLevelProgress;
            set => SetValue(ref _gsLevelProgress, value);
        }

        [DontSerialize]
        public string GSRank
        {
            get => _gsRank;
            set => SetValue(ref _gsRank, value ?? "Bronze1");
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

        #region Fullscreen Theme Integration (Native)

        /// <summary>
        /// Native fullscreen theme integration properties.
        /// New fullscreen themes should prefer these properties over the Aniki ReMake compatibility ones.
        /// All properties in this section are runtime-only and marked with [DontSerialize].
        /// </summary>

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
        private List<AchievementDetail> _fullscreenSingleGameUnlockAsc;
        [DontSerialize]
        private List<AchievementDetail> _fullscreenSingleGameUnlockDesc;
        [DontSerialize]
        private List<AchievementDetail> _fullscreenSingleGameRarityAsc;
        [DontSerialize]
        private List<AchievementDetail> _fullscreenSingleGameRarityDesc;
        [DontSerialize]
        private List<AchievementDetail> _fullscreenAllAchievementsUnlockAsc;
        [DontSerialize]
        private List<AchievementDetail> _fullscreenAllAchievementsUnlockDesc;
        [DontSerialize]
        private List<AchievementDetail> _fullscreenAllAchievementsRarityAsc;
        [DontSerialize]
        private List<AchievementDetail> _fullscreenAllAchievementsRarityDesc;
        

        [DontSerialize]
        public bool FullscreenHasData
        {
            get => _fullscreenHasData;
            set => SetValue(ref _fullscreenHasData, value);
        }

        [DontSerialize]
        public ObservableCollection<FullscreenAchievementGameItem> FullscreenGamesWithAchievements
        {
            get => _fullscreenGamesWithAchievements;
            set => SetValue(ref _fullscreenGamesWithAchievements, value);
        }

        [DontSerialize]
        public ObservableCollection<FullscreenAchievementGameItem> FullscreenPlatinumGames
        {
            get => _fullscreenPlatinumGames;
            set => SetValue(ref _fullscreenPlatinumGames, value);
        }

        [DontSerialize]
        public int FullscreenTotalTrophies
        {
            get => _fullscreenTotalTrophies;
            set => SetValue(ref _fullscreenTotalTrophies, Math.Max(0, value));
        }

        [DontSerialize]
        public int FullscreenPlatinumTrophies
        {
            get => _fullscreenPlatinumTrophies;
            set => SetValue(ref _fullscreenPlatinumTrophies, Math.Max(0, value));
        }

        [DontSerialize]
        public int FullscreenGoldTrophies
        {
            get => _fullscreenGoldTrophies;
            set => SetValue(ref _fullscreenGoldTrophies, Math.Max(0, value));
        }

        [DontSerialize]
        public int FullscreenSilverTrophies
        {
            get => _fullscreenSilverTrophies;
            set => SetValue(ref _fullscreenSilverTrophies, Math.Max(0, value));
        }

        [DontSerialize]
        public int FullscreenBronzeTrophies
        {
            get => _fullscreenBronzeTrophies;
            set => SetValue(ref _fullscreenBronzeTrophies, Math.Max(0, value));
        }

        [DontSerialize]
        public int FullscreenLevel
        {
            get => _fullscreenLevel;
            set => SetValue(ref _fullscreenLevel, Math.Max(0, value));
        }

        [DontSerialize]
        public double FullscreenLevelProgress
        {
            get => _fullscreenLevelProgress;
            set => SetValue(ref _fullscreenLevelProgress, value);
        }

        [DontSerialize]
        public string FullscreenRank
        {
            get => _fullscreenRank;
            set => SetValue(ref _fullscreenRank, value ?? "Bronze1");
        }

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
        public List<AchievementDetail> FullscreenSingleGameUnlockAsc
        {
            get => _fullscreenSingleGameUnlockAsc ?? EmptyAchievementList;
            set => SetValue(ref _fullscreenSingleGameUnlockAsc, value);
        }

        [DontSerialize]
        public List<AchievementDetail> FullscreenSingleGameUnlockDesc
        {
            get => _fullscreenSingleGameUnlockDesc ?? EmptyAchievementList;
            set => SetValue(ref _fullscreenSingleGameUnlockDesc, value);
        }

        [DontSerialize]
        public List<AchievementDetail> FullscreenSingleGameRarityAsc
        {
            get => _fullscreenSingleGameRarityAsc ?? EmptyAchievementList;
            set => SetValue(ref _fullscreenSingleGameRarityAsc, value);
        }

        [DontSerialize]
        public List<AchievementDetail> FullscreenSingleGameRarityDesc
        {
            get => _fullscreenSingleGameRarityDesc ?? EmptyAchievementList;
            set => SetValue(ref _fullscreenSingleGameRarityDesc, value);
        }

        [DontSerialize]
        public List<AchievementDetail> FullscreenAllAchievementsUnlockAsc
        {
            get => _fullscreenAllAchievementsUnlockAsc ?? EmptyAchievementList;
            set => SetValue(ref _fullscreenAllAchievementsUnlockAsc, value);
        }

        [DontSerialize]
        public List<AchievementDetail> FullscreenAllAchievementsUnlockDesc
        {
            get => _fullscreenAllAchievementsUnlockDesc ?? EmptyAchievementList;
            set => SetValue(ref _fullscreenAllAchievementsUnlockDesc, value);
        }

        [DontSerialize]
        public List<AchievementDetail> FullscreenAllAchievementsRarityAsc
        {
            get => _fullscreenAllAchievementsRarityAsc ?? EmptyAchievementList;
            set => SetValue(ref _fullscreenAllAchievementsRarityAsc, value);
        }

        [DontSerialize]
        public List<AchievementDetail> FullscreenAllAchievementsRarityDesc
        {
            get => _fullscreenAllAchievementsRarityDesc ?? EmptyAchievementList;
            set => SetValue(ref _fullscreenAllAchievementsRarityDesc, value);
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

        #region  Additional SuccessStory Integration

        /// <summary>
        /// Whether to show compact unlocked achievements list (SuccessStory-compatible view).
        /// Exposed at the root settings level for theme bindings.
        /// </summary>
        [DontSerialize]
        public bool EnableIntegrationCompactUnlocked
        {
            get => true;
        }

        /// <summary>
        /// Whether to show compact locked achievements list (SuccessStory-compatible view).
        /// Exposed at the root settings level for theme bindings.
        /// </summary>
        [DontSerialize]
        public bool EnableIntegrationCompactLocked
        {
            get => true;
        }

        /// <summary>
        /// Whether all achievements are unlocked (SuccessStory-compatible).
        /// </summary>
        [DontSerialize]
        public bool Is100Percent => SuccessStoryTheme?.Is100Percent ?? false;

        /// <summary>
        /// Number of locked achievements (SuccessStory-compatible).
        /// </summary>
        [DontSerialize]
        public int Locked => SuccessStoryTheme?.Locked ?? 0;

        /// <summary>
        /// Common achievement stats (SuccessStory-compatible).
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats Common => SuccessStoryTheme?.Common ?? EmptyRarityStats;

        /// <summary>
        /// Uncommon achievement stats (SuccessStory-compatible, named NoCommon in SS).
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats NoCommon => SuccessStoryTheme?.NoCommon ?? EmptyRarityStats;

        /// <summary>
        /// Rare achievement stats (SuccessStory-compatible).
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats Rare => SuccessStoryTheme?.Rare ?? EmptyRarityStats;

        /// <summary>
        /// Ultra rare achievement stats (SuccessStory-compatible).
        /// </summary>
        [DontSerialize]
        public AchievementRarityStats UltraRare => SuccessStoryTheme?.UltraRare ?? EmptyRarityStats;

        /// <summary>
        /// All achievements (SuccessStory-compatible).
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> ListAchievements => SuccessStoryTheme?.ListAchievements ?? EmptyAchievementList;

        /// <summary>
        /// Achievements sorted by unlock date ascending (SuccessStory-compatible).
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> ListAchUnlockDateAsc => SuccessStoryTheme?.ListAchUnlockDateAsc ?? EmptyAchievementList;

        /// <summary>
        /// Achievements sorted by unlock date descending (SuccessStory-compatible).
        /// </summary>
        [DontSerialize]
        public List<AchievementDetail> ListAchUnlockDateDesc => SuccessStoryTheme?.ListAchUnlockDateDesc ?? EmptyAchievementList;
    
        #endregion
    }
}

using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Friends;
using PlayniteAchievements.Services.Summaries;
using PlayniteAchievements.Services.UI;
#if !TEST
using PlayniteAchievements.Services.Library;
#endif
using PlayniteAchievements.ViewModels;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.Services.ThemeIntegration
{
    /// <summary>
    /// Service for theme integration, handling both per-game and all-games
    /// achievement data for desktop and fullscreen themes. Manages snapshot
    /// building, refresh commands, and theme surface updates.
    /// </summary>
    public sealed class ThemeIntegrationService : IDisposable
    {
        private static readonly List<AchievementDetail> EmptyAchievementList = new List<AchievementDetail>();
        private static readonly AchievementRarityStats EmptyRarityStats = new AchievementRarityStats();

        private delegate bool CommandParameterNormalizer(object parameter, out string normalizedKey);

        private readonly ILogger _logger;
        private readonly IPlayniteAPI _api;
        private readonly RefreshRuntime _refreshService;
        private readonly AchievementDataService _achievementDataService;
#if !TEST
        private readonly LibraryProjectionService _libraryProjectionService;
#endif
        private readonly RefreshEntryPoint _refreshCoordinator;
        private readonly IFriendCacheManager _friendCache;
        private readonly FriendsOverviewDataCoordinator _friendsOverviewDataCoordinator;
        private readonly bool _ownsFriendsOverviewDataCoordinator;
        private readonly Func<RefreshRequest, string, bool, Action<bool>, Task> _runRefreshWithGlobalProgressAsync;
        private readonly Action<Guid> _openManageAchievementsView;
        private readonly Func<AchievementHotkeyTargetResolution> _resolveRunningGameTarget;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly FullscreenWindowService _windowService;
        private readonly ThemeRuntimeState _runtimeState = new ThemeRuntimeState();

        private readonly object _refreshLock = new object();
        private CancellationTokenSource _refreshCts;
        private static readonly TimeSpan LibraryRefreshDelay = TimeSpan.FromMilliseconds(500);
#if TEST
        private static readonly TimeSpan FriendRefreshDelay = TimeSpan.Zero;
#else
        private static readonly TimeSpan FriendRefreshDelay = TimeSpan.FromMilliseconds(500);
#endif
        private const int ThemeRecentUnlockSummaryLimit = 10;

        private readonly object _updateGate = new object();
        private Task _updateRunner;
        private int _requestVersion;
        private int _processedVersion;
        private Guid? _requestedGameId;
        private bool _requestedForceSelectedGameRefresh;
        private CancellationTokenSource _activeUpdateCts;
        private Guid? _appliedGameId;
        private DateTime _appliedLastUpdatedUtc;
        private readonly object _friendRefreshGate = new object();
        private Task _friendRefreshRunner;
        private CancellationTokenSource _friendRefreshCts;
        private int _friendRefreshRequestVersion;
        private int _friendRefreshAppliedVersion;

        private bool _fullscreenInitialized;
        private bool _hasLoadedLibraryState;
        private bool _lastLibraryRefreshIncludedHeavyAchievementLists = true;
        private readonly Dictionary<Guid, RelayCommand> _openViewAchievementsCommands =
            new Dictionary<Guid, RelayCommand>();
        private readonly Dictionary<Guid, RelayCommand> _openManageAchievementsCommands =
            new Dictionary<Guid, RelayCommand>();

        internal ThemeIntegrationService(
            IPlayniteAPI api,
            RefreshRuntime refreshRuntime,
            AchievementDataService achievementDataService,
#if !TEST
            LibraryProjectionService libraryProjectionService,
#endif
            RefreshEntryPoint refreshEntryPoint,
            PlayniteAchievementsSettings settings,
            FullscreenWindowService windowService,
            ILogger logger,
            Func<RefreshRequest, string, bool, Action<bool>, Task> runRefreshWithGlobalProgressAsync = null,
            Action<Guid> openManageAchievementsView = null,
            IFriendCacheManager friendCache = null,
            FriendsOverviewDataCoordinator friendsOverviewDataCoordinator = null,
            Func<AchievementHotkeyTargetResolution> resolveRunningGameTarget = null)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _refreshService = refreshRuntime ?? throw new ArgumentNullException(nameof(refreshRuntime));
            _achievementDataService = achievementDataService ?? throw new ArgumentNullException(nameof(achievementDataService));
#if !TEST
            _libraryProjectionService = libraryProjectionService;
#endif
            _refreshCoordinator = refreshEntryPoint ?? throw new ArgumentNullException(nameof(refreshEntryPoint));
            _friendCache = friendCache;
            _ownsFriendsOverviewDataCoordinator = friendsOverviewDataCoordinator == null && friendCache != null;
            _friendsOverviewDataCoordinator = friendsOverviewDataCoordinator ??
                (friendCache != null
                    ? new FriendsOverviewDataCoordinator(friendCache, () => _settings?.Persisted, logger)
                    : null);
            _runRefreshWithGlobalProgressAsync = runRefreshWithGlobalProgressAsync ?? RunRefreshWithoutGlobalProgressAsync;
            _openManageAchievementsView = openManageAchievementsView;
            _resolveRunningGameTarget = resolveRunningGameTarget ?? ResolveRunningGameTargetFromApi;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _windowService = windowService ?? throw new ArgumentNullException(nameof(windowService));
            _logger = logger;
            _settings.DynamicThemeDefaultsChanged += Settings_DynamicThemeDefaultsChanged;

            var openOverviewCommand = new RelayCommand(_ => OpenOverviewWindow());
            var openSelectedGameCommand = new RelayCommand(OpenSelectedGameWindowCommand);
            var openViewAchievementsCommand = new RelayCommand(OpenViewAchievementsWindowCommand);
            var openManageAchievementsCommand = new RelayCommand(OpenManageAchievementsWindow);

            // Command surfaces referenced by themes.
            _settings.OpenFullscreenAchievementWindow = openSelectedGameCommand;
            _settings.OpenAchievementWindow = openOverviewCommand;
            _settings.OpenGameAchievementWindow = openSelectedGameCommand;
            _settings.OpenViewAchievementsWindow = openViewAchievementsCommand;
            _settings.OpenManageAchievementsWindow = openManageAchievementsCommand;
            _settings.SetDynamicAchievementsGameCommand = new RelayCommand(SetDynamicAchievementsGame);
            _settings.FilterDynamicAchievementsByRunningGameCommand = new RelayCommand(_ => FilterDynamicAchievementsByRunningGame());
            _settings.SingleGameRefreshCommand = new RelayCommand(_ => RefreshWithMode(RefreshModeType.Single));
            _settings.RecentRefreshCommand = new RelayCommand(_ => RefreshWithMode(RefreshModeType.Recent));
            _settings.FavoritesRefreshCommand = new RelayCommand(_ => RefreshWithMode(RefreshModeType.Favorites));
            _settings.FullRefreshCommand = new RelayCommand(_ => RefreshWithMode(RefreshModeType.Full));
            _settings.InstalledRefreshCommand = new RelayCommand(_ => RefreshWithMode(RefreshModeType.Installed));
            _settings.SetDynamicAchievementsFilterCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SetDynamicAchievementsFilterCommand),
                (object parameter, out string key) => DynamicThemeFilterExpression.TryNormalize(
                    parameter,
                    DynamicThemeOptionGroups.AchievementFilterKeyMap,
                    out key),
                () => _runtimeState.SelectedGameAchievements.FilterKey,
                key =>
                {
                    _runtimeState.SelectedGameAchievements.HasUserSelection = true;
                    _runtimeState.SelectedGameAchievements.FilterKey = key;
                },
                ApplyDynamicSelectedGameBindings,
                ThemeDelegatedPropertyCatalog.SingleGameTheme);
            _settings.SortDynamicAchievementsCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SortDynamicAchievementsCommand),
                DynamicThemeOptionGroups.SelectedGameAchievementSortKeyMap,
                () => _runtimeState.SelectedGameAchievements.SortKey,
                key =>
                {
                    _runtimeState.SelectedGameAchievements.HasUserSelection = true;
                    _runtimeState.SelectedGameAchievements.SortKey = key;
                },
                ApplyDynamicSelectedGameBindings,
                ThemeDelegatedPropertyCatalog.SingleGameTheme);
            _settings.SetDynamicAchievementsSortDirectionCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SetDynamicAchievementsSortDirectionCommand),
                DynamicThemeOptionGroups.SortDirectionKeyMap,
                () => _runtimeState.SelectedGameAchievements.SortDirectionKey,
                key =>
                {
                    _runtimeState.SelectedGameAchievements.HasUserSelection = true;
                    _runtimeState.SelectedGameAchievements.SortDirectionKey = key;
                },
                ApplyDynamicSelectedGameBindings,
                ThemeDelegatedPropertyCatalog.SingleGameTheme);
            _settings.FilterDynamicLibraryAchievementsByProviderCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.FilterDynamicLibraryAchievementsByProviderCommand),
                TryNormalizeProviderKey,
                () => _runtimeState.LibraryAchievements.ProviderKey,
                key =>
                {
                    _runtimeState.LibraryAchievements.HasUserSelection = true;
                    _runtimeState.LibraryAchievements.ProviderKey = key;
                },
                ApplyDynamicLibraryAchievementBindings,
                ThemeDelegatedPropertyCatalog.DynamicAllGames);
            _settings.FilterDynamicLibraryAchievementsByRunningGameCommand = new RelayCommand(_ => FilterDynamicLibraryAchievementsByRunningGame());
            _settings.SetDynamicLibraryAchievementsFilterCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SetDynamicLibraryAchievementsFilterCommand),
                (object parameter, out string key) => DynamicThemeFilterExpression.TryNormalize(
                    parameter,
                    DynamicThemeOptionGroups.AchievementFilterKeyMap,
                    out key),
                () => _runtimeState.LibraryAchievements.FilterKey,
                key =>
                {
                    _runtimeState.LibraryAchievements.HasUserSelection = true;
                    _runtimeState.LibraryAchievements.FilterKey = key;
                },
                ApplyDynamicLibraryAchievementBindings,
                ThemeDelegatedPropertyCatalog.DynamicAllGames);
            _settings.SortDynamicLibraryAchievementsCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SortDynamicLibraryAchievementsCommand),
                DynamicThemeOptionGroups.LibraryAchievementSortKeyMap,
                () => _runtimeState.LibraryAchievements.SortKey,
                key =>
                {
                    _runtimeState.LibraryAchievements.HasUserSelection = true;
                    _runtimeState.LibraryAchievements.SortKey = key;
                },
                ApplyDynamicLibraryAchievementBindings,
                ThemeDelegatedPropertyCatalog.DynamicAllGames);
            _settings.SetDynamicLibraryAchievementsSortDirectionCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SetDynamicLibraryAchievementsSortDirectionCommand),
                DynamicThemeOptionGroups.SortDirectionKeyMap,
                () => _runtimeState.LibraryAchievements.SortDirectionKey,
                key =>
                {
                    _runtimeState.LibraryAchievements.HasUserSelection = true;
                    _runtimeState.LibraryAchievements.SortDirectionKey = key;
                },
                ApplyDynamicLibraryAchievementBindings,
                ThemeDelegatedPropertyCatalog.DynamicAllGames);
            _settings.FilterDynamicGameSummariesByProviderCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.FilterDynamicGameSummariesByProviderCommand),
                TryNormalizeProviderKey,
                () => _runtimeState.GameSummaries.ProviderKey,
                key =>
                {
                    _runtimeState.GameSummaries.HasUserSelection = true;
                    _runtimeState.GameSummaries.ProviderKey = key;
                },
                ApplyDynamicGameSummaryBindings,
                ThemeDelegatedPropertyCatalog.DynamicAllGames);
            _settings.FilterDynamicGameSummariesByRunningGameCommand = new RelayCommand(_ => FilterDynamicGameSummariesByRunningGame());
            _settings.SetDynamicGameSummariesFilterCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SetDynamicGameSummariesFilterCommand),
                (object parameter, out string key) => DynamicThemeFilterExpression.TryNormalize(
                    parameter,
                    DynamicThemeOptionGroups.GameSummaryFilterKeyMap,
                    out key),
                () => _runtimeState.GameSummaries.FilterKey,
                key =>
                {
                    _runtimeState.GameSummaries.HasUserSelection = true;
                    _runtimeState.GameSummaries.FilterKey = key;
                },
                ApplyDynamicGameSummaryBindings,
                ThemeDelegatedPropertyCatalog.DynamicAllGames);
            _settings.SortDynamicGameSummariesCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SortDynamicGameSummariesCommand),
                DynamicThemeOptionGroups.GameSummarySortKeyMap,
                () => _runtimeState.GameSummaries.SortKey,
                key =>
                {
                    _runtimeState.GameSummaries.HasUserSelection = true;
                    _runtimeState.GameSummaries.SortKey = key;
                },
                ApplyDynamicGameSummaryBindings,
                ThemeDelegatedPropertyCatalog.DynamicAllGames);
            _settings.SetDynamicGameSummariesSortDirectionCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SetDynamicGameSummariesSortDirectionCommand),
                DynamicThemeOptionGroups.SortDirectionKeyMap,
                () => _runtimeState.GameSummaries.SortDirectionKey,
                key =>
                {
                    _runtimeState.GameSummaries.HasUserSelection = true;
                    _runtimeState.GameSummaries.SortDirectionKey = key;
                },
                ApplyDynamicGameSummaryBindings,
                ThemeDelegatedPropertyCatalog.DynamicAllGames);
            _settings.SetDynamicFriendScopeProviderCommand = new RelayCommand(SetDynamicFriendScopeProvider);
            _settings.SetDynamicFriendScopeUserCommand = new RelayCommand(SetDynamicFriendScopeUser);
            _settings.SetDynamicFriendScopeGameCommand = new RelayCommand(SetDynamicFriendScopeGame);
            _settings.ResetDynamicFriendScopeCommand = new RelayCommand(_ => ResetDynamicFriendScope());
            _settings.FilterDynamicFriendSummariesByRunningGameCommand = new RelayCommand(_ => FilterDynamicFriendScopeByRunningGame(nameof(PlayniteAchievementsSettings.FilterDynamicFriendSummariesByRunningGameCommand)));
            _settings.SetDynamicFriendSummariesFilterCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SetDynamicFriendSummariesFilterCommand),
                (object parameter, out string key) => DynamicThemeFilterExpression.TryNormalize(
                    parameter,
                    DynamicThemeOptionGroups.FriendSummaryFilterKeyMap,
                    out key),
                () => _runtimeState.FriendSummaries.FilterKey,
                key =>
                {
                    _runtimeState.FriendSummaries.HasUserSelection = true;
                    _runtimeState.FriendSummaries.FilterKey = key;
                },
                ApplyDynamicFriendBindings,
                ThemeDelegatedPropertyCatalog.DynamicFriends);
            _settings.SortDynamicFriendSummariesCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SortDynamicFriendSummariesCommand),
                DynamicThemeOptionGroups.FriendSummarySortKeyMap,
                () => _runtimeState.FriendSummaries.SortKey,
                key =>
                {
                    _runtimeState.FriendSummaries.HasUserSelection = true;
                    _runtimeState.FriendSummaries.SortKey = key;
                },
                ApplyDynamicFriendBindings,
                ThemeDelegatedPropertyCatalog.DynamicFriends);
            _settings.SetDynamicFriendSummariesSortDirectionCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SetDynamicFriendSummariesSortDirectionCommand),
                DynamicThemeOptionGroups.SortDirectionKeyMap,
                () => _runtimeState.FriendSummaries.SortDirectionKey,
                key =>
                {
                    _runtimeState.FriendSummaries.HasUserSelection = true;
                    _runtimeState.FriendSummaries.SortDirectionKey = key;
                },
                ApplyDynamicFriendBindings,
                ThemeDelegatedPropertyCatalog.DynamicFriends);
            _settings.FilterDynamicFriendGameSummariesByRunningGameCommand = new RelayCommand(_ => FilterDynamicFriendScopeByRunningGame(nameof(PlayniteAchievementsSettings.FilterDynamicFriendGameSummariesByRunningGameCommand)));
            _settings.SetDynamicFriendGameSummariesFilterCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SetDynamicFriendGameSummariesFilterCommand),
                (object parameter, out string key) => DynamicThemeFilterExpression.TryNormalize(
                    parameter,
                    DynamicThemeOptionGroups.GameSummaryFilterKeyMap,
                    out key),
                () => _runtimeState.FriendGameSummaries.FilterKey,
                key =>
                {
                    _runtimeState.FriendGameSummaries.HasUserSelection = true;
                    _runtimeState.FriendGameSummaries.FilterKey = key;
                },
                ApplyDynamicFriendBindings,
                ThemeDelegatedPropertyCatalog.DynamicFriends);
            _settings.SortDynamicFriendGameSummariesCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SortDynamicFriendGameSummariesCommand),
                DynamicThemeOptionGroups.GameSummarySortKeyMap,
                () => _runtimeState.FriendGameSummaries.SortKey,
                key =>
                {
                    _runtimeState.FriendGameSummaries.HasUserSelection = true;
                    _runtimeState.FriendGameSummaries.SortKey = key;
                },
                ApplyDynamicFriendBindings,
                ThemeDelegatedPropertyCatalog.DynamicFriends);
            _settings.SetDynamicFriendGameSummariesSortDirectionCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SetDynamicFriendGameSummariesSortDirectionCommand),
                DynamicThemeOptionGroups.SortDirectionKeyMap,
                () => _runtimeState.FriendGameSummaries.SortDirectionKey,
                key =>
                {
                    _runtimeState.FriendGameSummaries.HasUserSelection = true;
                    _runtimeState.FriendGameSummaries.SortDirectionKey = key;
                },
                ApplyDynamicFriendBindings,
                ThemeDelegatedPropertyCatalog.DynamicFriends);
            _settings.FilterDynamicFriendAchievementsByRunningGameCommand = new RelayCommand(_ => FilterDynamicFriendScopeByRunningGame(nameof(PlayniteAchievementsSettings.FilterDynamicFriendAchievementsByRunningGameCommand)));
            _settings.SetDynamicFriendAchievementsFilterCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SetDynamicFriendAchievementsFilterCommand),
                (object parameter, out string key) => DynamicThemeFilterExpression.TryNormalize(
                    parameter,
                    DynamicThemeOptionGroups.AchievementFilterKeyMap,
                    out key),
                () => _runtimeState.FriendAchievements.FilterKey,
                key =>
                {
                    _runtimeState.FriendAchievements.HasUserSelection = true;
                    _runtimeState.FriendAchievements.FilterKey = key;
                },
                ApplyDynamicFriendBindings,
                ThemeDelegatedPropertyCatalog.DynamicFriends);
            _settings.SortDynamicFriendAchievementsCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SortDynamicFriendAchievementsCommand),
                DynamicThemeOptionGroups.LibraryAchievementSortKeyMap,
                () => _runtimeState.FriendAchievements.SortKey,
                key =>
                {
                    _runtimeState.FriendAchievements.HasUserSelection = true;
                    _runtimeState.FriendAchievements.SortKey = key;
                },
                ApplyDynamicFriendBindings,
                ThemeDelegatedPropertyCatalog.DynamicFriends);
            _settings.SetDynamicFriendAchievementsSortDirectionCommand = CreateDynamicCommand(
                nameof(PlayniteAchievementsSettings.SetDynamicFriendAchievementsSortDirectionCommand),
                DynamicThemeOptionGroups.SortDirectionKeyMap,
                () => _runtimeState.FriendAchievements.SortDirectionKey,
                key =>
                {
                    _runtimeState.FriendAchievements.HasUserSelection = true;
                    _runtimeState.FriendAchievements.SortDirectionKey = key;
                },
                ApplyDynamicFriendBindings,
                ThemeDelegatedPropertyCatalog.DynamicFriends);
            _settings.ResetDynamicAchievementsCommand = new RelayCommand(_ => ResetDynamicAchievementsToDefaults());
            _settings.ResetDynamicLibraryAchievementsCommand = new RelayCommand(_ => ResetDynamicLibraryAchievementsToDefaults());
            _settings.ResetDynamicGameSummariesCommand = new RelayCommand(_ => ResetDynamicGameSummariesToDefaults());

            _runtimeState.Friends = FriendRuntimeState.Empty;
            ApplyDynamicThemeDefaultsFromSettings(notify: false);
            ApplyDynamicSelectedGameBindings(updateOptions: false);
            ApplyDynamicLibraryAchievementBindings(updateOptions: false);
            ApplyDynamicGameSummaryBindings(updateOptions: false);
            ApplyDynamicFriendBindings(updateOptions: false);
            ApplyDynamicOptionBindings();

            _refreshService.CacheInvalidated += RefreshService_CacheInvalidated;
            if (_friendCache != null)
            {
                _friendCache.FriendCacheInvalidated += FriendCache_FriendCacheInvalidated;
                RequestFriendStateRefresh();
            }
        }

        public void Dispose()
        {
            try { _settings.DynamicThemeDefaultsChanged -= Settings_DynamicThemeDefaultsChanged; } catch { }
            try { _refreshService.CacheInvalidated -= RefreshService_CacheInvalidated; } catch { }
            try
            {
                if (_friendCache != null)
                {
                    _friendCache.FriendCacheInvalidated -= FriendCache_FriendCacheInvalidated;
                }
            }
            catch { }

            lock (_refreshLock)
            {
                try { _refreshCts?.Cancel(); } catch { }
                try { _refreshCts?.Dispose(); } catch { }
                _refreshCts = null;
            }

            lock (_updateGate)
            {
                try { _activeUpdateCts?.Cancel(); } catch { }
                try { _activeUpdateCts?.Dispose(); } catch { }
                _activeUpdateCts = null;
            }

            lock (_friendRefreshGate)
            {
                try { _friendRefreshCts?.Cancel(); } catch { }
                try { _friendRefreshCts?.Dispose(); } catch { }
                _friendRefreshCts = null;
            }

            if (_ownsFriendsOverviewDataCoordinator)
            {
                try { _friendsOverviewDataCoordinator?.Dispose(); } catch { }
            }
        }

        public void NotifySelectionChanged(Guid? selectedGameId)
        {
            if (!selectedGameId.HasValue)
            {
                return;
            }

            EnsureFullscreenInitialized();
        }

        public void RequestUpdate(Guid? gameId, bool forceRefresh = false)
        {
            lock (_updateGate)
            {
                _requestVersion++;
                _requestedGameId = gameId;
                _requestedForceSelectedGameRefresh |= forceRefresh;
                if (_updateRunner == null || _updateRunner.IsCompleted)
                {
                    _updateRunner = RunUpdateLoopAsync();
                }
            }
        }

        public void EnsureAllGamesThemeDataLoaded(
            bool includeHeavyAchievementLists = true,
            bool forceRefresh = false)
        {
            try
            {
                if (!forceRefresh &&
                    _hasLoadedLibraryState &&
                    (!includeHeavyAchievementLists || _lastLibraryRefreshIncludedHeavyAchievementLists))
                {
                    ApplyDynamicOptionBindings();
                    return;
                }

                var dispatcher = _api?.MainView?.UIDispatcher ?? Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(
                        new Action(() => PopulateAllGamesDataSync(includeHeavyAchievementLists)),
                        DispatcherPriority.Background);
                    return;
                }

                PopulateAllGamesDataSync(includeHeavyAchievementLists);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to ensure all-games theme data is loaded.");
            }
        }

        public void NotifyCustomDataChanged(Guid? gameId)
        {
            try
            {
                var resolvedGameId = gameId.HasValue && gameId.Value != Guid.Empty
                    ? gameId
                    : ResolveSelectedGameIdForThemeUpdate();
                var shouldRefreshSelectedGame = resolvedGameId.HasValue &&
                    ((_appliedGameId.HasValue && _appliedGameId.Value == resolvedGameId.Value) ||
                     (_requestedGameId.HasValue && _requestedGameId.Value == resolvedGameId.Value) ||
                     (_settings?.SelectedGame?.Id == resolvedGameId.Value));

                if (shouldRefreshSelectedGame)
                {
                    RequestUpdate(resolvedGameId.Value, forceRefresh: true);
                }
                else if (resolvedGameId.HasValue)
                {
                    RequestUpdate(resolvedGameId.Value, forceRefresh: true);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to refresh selected-game theme state after custom-data change.");
            }

            try
            {
                if (_hasLoadedLibraryState)
                {
                    RequestLibraryRefresh(_lastLibraryRefreshIncludedHeavyAchievementLists);
                }
                else if (IsFullscreen() && _fullscreenInitialized)
                {
                    RequestRefresh();
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to refresh library theme state after custom-data change.");
            }
        }

        private bool IsFullscreen()
        {
            try
            {
                return _api?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureFullscreenInitialized()
        {
            if (!IsFullscreen() || _fullscreenInitialized)
            {
                return;
            }

            _fullscreenInitialized = true;
            RequestRefresh();
        }

        private void RefreshService_CacheInvalidated(object sender, EventArgs e)
        {
            if (IsFullscreen() && _fullscreenInitialized)
            {
                RequestRefresh();
            }

            try
            {
                var id = ResolveSelectedGameIdForThemeUpdate();
                if (id.HasValue)
                {
                    RequestUpdate(id);
                }
            }
            catch
            {
            }

            _friendsOverviewDataCoordinator?.Invalidate();
            RequestFriendStateRefresh();
        }

        private void FriendCache_FriendCacheInvalidated(object sender, EventArgs e)
        {
            _friendsOverviewDataCoordinator?.Invalidate();
            RequestFriendStateRefresh();
        }

        private Guid? ResolveSelectedGameIdForThemeUpdate()
        {
            try
            {
                var selectedGame = _settings?.SelectedGame;
                if (selectedGame != null && selectedGame.Id != Guid.Empty)
                {
                    return selectedGame.Id;
                }
            }
            catch
            {
            }

            return GetSingleSelectedGameId();
        }

        private Guid? GetSingleSelectedGameId()
        {
            try
            {
                var dispatcher = _api?.MainView?.UIDispatcher ?? Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    return dispatcher.Invoke(
                        new Func<Guid?>(GetSingleSelectedGameIdFromMainView),
                        DispatcherPriority.Background);
                }

                return GetSingleSelectedGameIdFromMainView();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to resolve selected game for fullscreen achievement window.");
                return null;
            }
        }

        private Guid? GetSingleSelectedGameIdFromMainView()
        {
            var selected = _api?.MainView?.SelectedGames?
                    .Where(g => g != null)
                    .Take(2)
                    .ToList();

            if (selected == null || selected.Count != 1)
            {
                return null;
            }

            return selected[0].Id;
        }

        private async Task RunUpdateLoopAsync()
        {
            while (true)
            {
                int version;
                Guid? gameId;
                bool forceRefresh;
                CancellationToken token;

                lock (_updateGate)
                {
                    if (_processedVersion == _requestVersion)
                    {
                        return;
                    }

                    version = _requestVersion;
                    gameId = _requestedGameId;
                    forceRefresh = _requestedForceSelectedGameRefresh;
                    _requestedForceSelectedGameRefresh = false;
                    _processedVersion = version;

                    try { _activeUpdateCts?.Cancel(); } catch { }
                    try { _activeUpdateCts?.Dispose(); } catch { }
                    _activeUpdateCts = new CancellationTokenSource();
                    token = _activeUpdateCts.Token;
                }

                if (!gameId.HasValue)
                {
                    await ApplyClearAsync(version).ConfigureAwait(false);
                    continue;
                }

                GameAchievementData gameData = null;
                try
                {
                    gameData = await Task.Run(() => _achievementDataService.GetVisibleGameAchievementData(gameId.Value), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Theme integration update failed to fetch game data.");
                }

                if (token.IsCancellationRequested)
                {
                    continue;
                }

                if (gameData == null || !gameData.HasAchievements)
                {
                    await ApplyClearAsync(version, gameId).ConfigureAwait(false);
                    continue;
                }

                if (!forceRefresh &&
                    _appliedGameId.HasValue &&
                    _appliedGameId.Value == gameId.Value &&
                    _appliedLastUpdatedUtc == gameData.LastUpdatedUtc)
                {
                    continue;
                }

                SelectedGameRuntimeState state = null;
                try
                {
                    state = await Task.Run(
                        () => SelectedGameRuntimeStateBuilder.Build(gameId.Value, gameData),
                        token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Theme integration update failed while building selected-game state.");
                }

                if (token.IsCancellationRequested)
                {
                    continue;
                }

                Action applyState = () =>
                {
                    if (!IsLatest(version))
                    {
                        return;
                    }

                    if (state == null || !state.HasAchievements)
                    {
                        ClearSingleGameThemeProperties(gameId);
                        return;
                    }

                    ApplySelectedGameState(state);
                    _appliedGameId = gameId;
                    _appliedLastUpdatedUtc = gameData.LastUpdatedUtc;
                };

                var dispatcher = _api?.MainView?.UIDispatcher ?? Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    applyState();
                }
                else
                {
                    await dispatcher.InvokeAsync(applyState, DispatcherPriority.Background).Task.ConfigureAwait(false);
                }
            }
        }

        private Task ApplyClearAsync(int version, Guid? selectedGameId = null)
        {
            var dispatcher = _api?.MainView?.UIDispatcher ?? Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                ClearSingleGameThemeProperties(selectedGameId);
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(() =>
            {
                if (!IsLatest(version))
                {
                    return;
                }

                ClearSingleGameThemeProperties(selectedGameId);
            }, DispatcherPriority.Background).Task;
        }

        private bool IsLatest(int version)
        {
            lock (_updateGate)
            {
                return version == _requestVersion;
            }
        }

        #region Window Operations (delegated to FullscreenWindowService)

        private void OpenOverviewWindow()
        {
            // Cancel any pending async refresh to prevent it from overwriting our data
            lock (_refreshLock)
            {
                try { _refreshCts?.Cancel(); } catch { }
                try { _refreshCts?.Dispose(); } catch { }
                _refreshCts = null;
            }

            // Mark as initialized to prevent other code paths from triggering refresh
            if (!_fullscreenInitialized)
            {
                _fullscreenInitialized = true;
            }

            // Immediately populate all-games data on UI thread before opening window
            PopulateAllGamesDataSync(includeHeavyAchievementLists: true);

            _windowService.OpenOverviewWindow();
        }

        private void OpenSelectedGameWindowCommand(object parameter)
        {
            if (TryResolveThemeCommandGameId(parameter, out var gameId))
            {
                OpenViewAchievementsWindow(gameId);
                return;
            }

            OpenSelectedGameWindow();
        }

        private void OpenSelectedGameWindow()
        {
            var id = GetSingleSelectedGameId();
            if (!id.HasValue)
            {
                return;
            }

            OpenViewAchievementsWindow(id.Value);
        }

        private void SetDynamicAchievementsGame(object parameter)
        {
            if (!TryResolveThemeCommandGameId(parameter, out var gameId))
            {
                LogInvalidCommandParameter(nameof(PlayniteAchievementsSettings.SetDynamicAchievementsGameCommand), parameter);
                return;
            }

            ApplyDynamicAchievementsGame(gameId);
        }

        private void FilterDynamicAchievementsByRunningGame()
        {
            if (!TryResolveRunningGameId(
                nameof(PlayniteAchievementsSettings.FilterDynamicAchievementsByRunningGameCommand),
                out var gameId))
            {
                return;
            }

            ApplyDynamicAchievementsGame(gameId);
        }

        private void ApplyDynamicAchievementsGame(Guid gameId)
        {
            _runtimeState.SelectedGameAchievements.GameKey = gameId.ToString("D");
            _runtimeState.SelectedGameAchievements.GameLabel = ResolveGameLabel(gameId);
            PopulateSingleGameDataSync(gameId);
        }

        private void FilterDynamicLibraryAchievementsByRunningGame()
        {
            if (!TryResolveRunningGameId(
                nameof(PlayniteAchievementsSettings.FilterDynamicLibraryAchievementsByRunningGameCommand),
                out var gameId))
            {
                return;
            }

            EnsureAllGamesThemeDataLoaded(includeHeavyAchievementLists: true);

            var state = _runtimeState.Library ?? new LibraryRuntimeState();
            var viewState = _runtimeState.LibraryAchievements;
            var providerKey = ResolveLibraryProviderKeyForGame(state, gameId);

            viewState.HasUserSelection = true;
            viewState.GameKey = gameId.ToString("D");
            viewState.GameLabel = ResolveGameLabel(gameId);
            if (!ProviderScopeMatches(viewState.ProviderKey, providerKey))
            {
                viewState.ProviderKey = string.IsNullOrWhiteSpace(providerKey)
                    ? DynamicThemeViewKeys.All
                    : providerKey;
            }

            ApplyDynamicLibraryAchievementBindings();
            NotifySettingProperties(ThemeDelegatedPropertyCatalog.DynamicAllGames);
        }

        private void FilterDynamicGameSummariesByRunningGame()
        {
            if (!TryResolveRunningGameId(
                nameof(PlayniteAchievementsSettings.FilterDynamicGameSummariesByRunningGameCommand),
                out var gameId))
            {
                return;
            }

            EnsureAllGamesThemeDataLoaded(includeHeavyAchievementLists: true);

            var state = _runtimeState.Library ?? new LibraryRuntimeState();
            var viewState = _runtimeState.GameSummaries;
            var providerKey = ResolveGameSummaryProviderKeyForGame(state, gameId);

            viewState.HasUserSelection = true;
            viewState.GameKey = gameId.ToString("D");
            viewState.GameLabel = ResolveGameLabel(gameId);
            if (!ProviderScopeMatches(viewState.ProviderKey, providerKey))
            {
                viewState.ProviderKey = string.IsNullOrWhiteSpace(providerKey)
                    ? DynamicThemeViewKeys.All
                    : providerKey;
            }

            ApplyDynamicGameSummaryBindings();
            NotifySettingProperties(ThemeDelegatedPropertyCatalog.DynamicAllGames);
        }

        private void FilterDynamicFriendScopeByRunningGame(string commandName)
        {
            if (!TryResolveRunningGameId(commandName, out var gameId))
            {
                return;
            }

            var state = _runtimeState.Friends ?? FriendRuntimeState.Empty;
            var game = FindFriendGameScopeCandidate(state, gameId);
            if (game == null)
            {
                _logger?.Debug($"Ignored {commandName}: running game '{gameId:D}' is not available in friend theme data.");
                return;
            }

            var targetGameKey = FriendOverviewProjection.GetGameScopeKey(game);
            if (FriendOverviewProjection.IsAllScope(targetGameKey))
            {
                _logger?.Debug($"Ignored {commandName}: running game '{gameId:D}' has no friend game scope key.");
                return;
            }

            var scope = _runtimeState.FriendScope;
            if (!ProviderScopeMatches(scope.ProviderKey, game.ProviderKey))
            {
                scope.ProviderKey = string.IsNullOrWhiteSpace(game.ProviderKey)
                    ? DynamicThemeViewKeys.All
                    : game.ProviderKey;
            }

            if (!FriendOverviewProjection.IsAllScope(scope.UserKey) &&
                !GetFriendGameScopeCandidates(state, scope.ProviderKey, scope.UserKey)
                    .Any(candidate => string.Equals(
                        FriendOverviewProjection.GetGameScopeKey(candidate),
                        targetGameKey,
                        StringComparison.OrdinalIgnoreCase)))
            {
                scope.UserKey = DynamicThemeViewKeys.All;
            }

            scope.GameKey = targetGameKey;
            ApplyDynamicFriendBindings();
            NotifySettingProperties(ThemeDelegatedPropertyCatalog.DynamicFriends);
        }

        private void OpenManageAchievementsWindow(object parameter)
        {
            if (!TryResolveThemeCommandGameId(parameter, out var gameId))
            {
                return;
            }

            _openManageAchievementsView?.Invoke(gameId);
        }

        private void OpenViewAchievementsWindowCommand(object parameter)
        {
            if (!TryResolveThemeCommandGameId(parameter, out var gameId))
            {
                return;
            }

            OpenViewAchievementsWindow(gameId);
        }

        private bool TryResolveThemeCommandGameId(object parameter, out Guid gameId)
        {
            gameId = Guid.Empty;

            if (parameter == DependencyProperty.UnsetValue)
            {
                parameter = null;
            }

            switch (parameter)
            {
                case DynamicThemeOption option when Guid.TryParse(option.Key, out var optionId) && optionId != Guid.Empty:
                    gameId = optionId;
                    return true;
                case GameAchievementSummary summary when summary.GameId != Guid.Empty:
                    gameId = summary.GameId;
                    return true;
                case AchievementDetail achievement when achievement.Game?.Id != Guid.Empty:
                    gameId = achievement.Game.Id;
                    return true;
                case AchievementDisplayItem item when item.PlayniteGameId.HasValue && item.PlayniteGameId.Value != Guid.Empty:
                    gameId = item.PlayniteGameId.Value;
                    return true;
                case GameSummaryItem gameItem when gameItem.PlayniteGameId.HasValue && gameItem.PlayniteGameId.Value != Guid.Empty:
                    gameId = gameItem.PlayniteGameId.Value;
                    return true;
                case Game game when game.Id != Guid.Empty:
                    gameId = game.Id;
                    return true;
                case Guid id when id != Guid.Empty:
                    gameId = id;
                    return true;
                case string idText when Guid.TryParse(idText, out var parsedId) && parsedId != Guid.Empty:
                    gameId = parsedId;
                    return true;
            }

            var selectedId = ResolveSelectedGameIdForThemeUpdate();
            if (!selectedId.HasValue || selectedId.Value == Guid.Empty)
            {
                return false;
            }

            gameId = selectedId.Value;
            return true;
        }

        private bool TryResolveRunningGameId(string commandName, out Guid gameId)
        {
            gameId = Guid.Empty;

            try
            {
                var target = _resolveRunningGameTarget?.Invoke() ?? AchievementHotkeyTargetResolution.NoTarget;
                if (target.HasTarget && target.GameId != Guid.Empty)
                {
                    gameId = target.GameId;
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed to resolve running game for {commandName}.");
            }

            _logger?.Debug($"Ignored {commandName}: no running Playnite game.");
            return false;
        }

        private AchievementHotkeyTargetResolution ResolveRunningGameTargetFromApi()
        {
            try
            {
                var runningGames = _api?.Database?.Games?
                    .Where(game => game != null && game.Id != Guid.Empty && game.IsRunning)
                    .ToList() ?? new List<Game>();

                return AchievementHotkeyTargetResolver.ResolveRunningGame(runningGames, Array.Empty<Guid>());
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to resolve running game from Playnite API.");
                return AchievementHotkeyTargetResolution.NoTarget;
            }
        }

        private string ResolveGameLabel(Guid gameId)
        {
            var summary = _runtimeState.Library?.AllGamesWithAchievements?
                .FirstOrDefault(item => item != null && item.GameId == gameId);
            if (!string.IsNullOrWhiteSpace(summary?.Name))
            {
                return summary.Name;
            }

            var selectedGame = _settings.SelectedGame;
            if (selectedGame != null && selectedGame.Id == gameId && !string.IsNullOrWhiteSpace(selectedGame.Name))
            {
                return selectedGame.Name;
            }

            var achievementGameName = _runtimeState.SelectedGame?.AllAchievements?
                .Select(item => item?.Game)
                .FirstOrDefault(game => game != null && game.Id == gameId && !string.IsNullOrWhiteSpace(game.Name))
                ?.Name;
            return !string.IsNullOrWhiteSpace(achievementGameName)
                ? achievementGameName
                : gameId.ToString("D");
        }

        public void OpenViewAchievementsWindow(Guid gameId)
        {
            EnsureFullscreenInitialized();

            // Synchronously populate single-game data before opening the window.
            // This prevents the race condition where the window opens before
            // async theme updates complete, showing stale data from the previous selection.
            PopulateSingleGameDataSync(gameId);

            _windowService.OpenViewAchievementsWindow(gameId);
        }

        #endregion

        #region Refresh Operations

        public Task RunFullscreenRefreshRequestAsync(
            RefreshRequest request,
            string errorLogMessage,
            bool validateAuthentication,
            Action<bool> onCompleted = null)
        {
            request ??= new RefreshRequest();

            EnsureFullscreenInitialized();
            _logger?.Info($"RunFullscreenRefreshRequestAsync: Starting mode={request.Mode}, singleGameId={request.SingleGameId}, explicitGameCount={request.GameIds?.Count ?? 0}, validateAuthentication={validateAuthentication}");

            var singleGameId = request.SingleGameId;
            if (!singleGameId.HasValue && request.Mode == RefreshModeType.Single)
            {
                singleGameId = GetSingleSelectedGameId();
            }

            var resolvedErrorMessage = !string.IsNullOrWhiteSpace(errorLogMessage)
                ? errorLogMessage
                : request.Mode.HasValue
                    ? GetLocalizedRefreshFailureMessage(request.Mode.Value)
                    : ResourceProvider.GetString("LOCPlayAch_Error_RebuildFailed");

            return _runRefreshWithGlobalProgressAsync(
                request,
                resolvedErrorMessage,
                validateAuthentication,
                success =>
                {
                    if (success)
                    {
                        _logger?.Info($"RunFullscreenRefreshRequestAsync: Completed, success={success}, mode={request.Mode}, singleGameId={singleGameId}");
                    }

                    if (singleGameId.HasValue)
                    {
                        try { RequestUpdate(singleGameId.Value); } catch { }
                    }

                    try { if (IsFullscreen() && _fullscreenInitialized) RequestRefresh(); } catch { }

                    try { onCompleted?.Invoke(success); } catch { }
                });
        }

        private void RefreshWithMode(RefreshModeType mode)
        {
            Guid? gameIdForThemeUpdate = null;

            if (mode == RefreshModeType.Single)
            {
                var id = GetSingleSelectedGameId();
                if (!id.HasValue)
                {
                    return;
                }
                gameIdForThemeUpdate = id;
            }

            EnsureFullscreenInitialized();
            RunAchievementRefresh(mode, gameIdForThemeUpdate);
        }

        private void RunAchievementRefresh(RefreshModeType mode, Guid? gameIdForThemeUpdate)
        {
            var errorLogMessage = GetLocalizedRefreshFailureMessage(mode);

            var isFullscreen = IsFullscreen();
            _logger?.Info($"RunAchievementRefresh: mode={mode}, isFullscreen={isFullscreen}, gameId={gameIdForThemeUpdate}");

            if (isFullscreen)
            {
                RunAchievementRefreshWithGlobalProgress(mode, gameIdForThemeUpdate, errorLogMessage);
            }
            else
            {
                RunAchievementRefreshWithProgressWindow(mode, gameIdForThemeUpdate, errorLogMessage);
            }
        }

        private void RunAchievementRefreshWithGlobalProgress(
            RefreshModeType mode,
            Guid? gameIdForThemeUpdate,
            string errorLogMessage)
        {
            var request = new RefreshRequest
            {
                Mode = mode,
                SingleGameId = mode == RefreshModeType.Single ? gameIdForThemeUpdate : null
            };

            _ = _runRefreshWithGlobalProgressAsync(
                request,
                errorLogMessage,
                false,
                success =>
                {
                    if (success)
                    {
                        _logger?.Info($"RunAchievementRefreshWithGlobalProgress: Completed, success={success}, gameId={gameIdForThemeUpdate}");
                    }

                    if (gameIdForThemeUpdate.HasValue)
                    {
                        try { RequestUpdate(gameIdForThemeUpdate.Value); } catch { }
                    }

                    try { if (IsFullscreen() && _fullscreenInitialized) RequestRefresh(); } catch { }
                });
        }

        private Task RunRefreshWithoutGlobalProgressAsync(
            RefreshRequest request,
            string errorLogMessage,
            bool validateAuthentication,
            Action<bool> onCompleted)
        {
            request ??= new RefreshRequest();

            return _refreshCoordinator.ExecuteAsync(
                request,
                new RefreshExecutionPolicy
                {
                    ValidateAuthentication = validateAuthentication,
                    SwallowExceptions = true,
                    ErrorLogMessage = errorLogMessage,
                    OnRefreshCompleted = success =>
                    {
                        try
                        {
                            onCompleted?.Invoke(success);
                        }
                        catch (Exception ex)
                        {
                            _logger?.Debug(ex, "Refresh completion callback failed.");
                        }
                    }
                });
        }

        private static string GetLocalizedRefreshFailureMessage(RefreshModeType mode)
        {
            var modeName = ResourceProvider.GetString(mode.GetResourceKey());
            var format = ResourceProvider.GetString("LOCPlayAch_Error_RefreshFailed");
            if (!string.IsNullOrWhiteSpace(format) && !string.IsNullOrWhiteSpace(modeName))
            {
                return string.Format(format, modeName);
            }

            return ResourceProvider.GetString("LOCPlayAch_Error_RebuildFailed");
        }

        private void RunAchievementRefreshWithProgressWindow(
            RefreshModeType mode,
            Guid? gameIdForThemeUpdate,
            string errorLogMessage)
        {
            _logger?.Info($"RunAchievementRefreshWithProgressWindow: Starting fullscreen refresh, mode={mode}, gameId={gameIdForThemeUpdate}");

            var request = new RefreshRequest
            {
                Mode = mode,
                SingleGameId = mode == RefreshModeType.Single ? gameIdForThemeUpdate : null
            };

            _ = _refreshCoordinator.ExecuteAsync(
                request,
                new RefreshExecutionPolicy
                {
                    ValidateAuthentication = false,
                    UseProgressWindow = true,
                    SwallowExceptions = true,
                    ProgressSingleGameId = gameIdForThemeUpdate,
                    ErrorLogMessage = errorLogMessage,
                    OnRefreshCompleted = (success) =>
                    {
                        _logger?.Info($"RunAchievementRefreshWithProgressWindow: Completed, success={success}, gameId={gameIdForThemeUpdate}");
                        if (success)
                        {
                            if (gameIdForThemeUpdate.HasValue)
                            {
                                try { RequestUpdate(gameIdForThemeUpdate.Value); } catch { }
                            }
                            try { if (IsFullscreen()) RequestRefresh(); } catch { }
                        }
                    }
                });
        }

        #endregion

        #region Snapshot Building and Refresh

        private void PopulateAllGamesDataSync(bool includeHeavyAchievementLists)
        {
            try
            {
                _logger?.Info("PopulateAllGamesDataSync: Starting to populate all-games achievement data.");

                var state = BuildLibraryState(
                    includeHeavyAchievementLists,
                    CancellationToken.None,
                    out var usedCachedSummary,
                    out var hydratedCount);
                if (usedCachedSummary)
                {
                    _logger?.Info($"PopulateAllGamesDataSync: Built from cached summary data. AllGamesWithAchievements count={state.AllGamesWithAchievements?.Count ?? 0}.");
                }
                else
                {
                    _logger?.Info($"PopulateAllGamesDataSync: Found {hydratedCount.GetValueOrDefault()} total game data entries.");
                }

                _logger?.Info($"PopulateAllGamesDataSync: State created - TotalTrophies={state.TotalTrophies}, PlatinumTrophies={state.PlatinumTrophies}, GoldTrophies={state.GoldTrophies}, Rank={state.Rank}");

                _hasLoadedLibraryState = true;
                _lastLibraryRefreshIncludedHeavyAchievementLists = includeHeavyAchievementLists;
                ApplyLibraryState(state);

                _logger?.Info($"PopulateAllGamesDataSync: Applied snapshot. AllGamesWithAchievements count={_settings.LegacyTheme.AllGamesWithAchievements?.Count ?? 0}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to populate all-games data synchronously.");
            }
        }

        /// <summary>
        /// Synchronously populates single-game achievement data for the specified game.
        /// Uses existing cached data without triggering a refresh.
        /// Called on desktop game selection changes to populate modern theme bindings for theme controls.
        /// </summary>
        /// <param name="gameId">The ID of the game to populate data for.</param>
        internal void PopulateSingleGameDataSync(Guid gameId)
        {
            try
            {
                var gameData = _achievementDataService.GetVisibleGameAchievementData(gameId);
                var state = SelectedGameRuntimeStateBuilder.Build(
                    gameId,
                    gameData);

                if (state != null && state.HasAchievements)
                {
                    ApplySelectedGameState(state);
                    _appliedGameId = gameId;
                    _appliedLastUpdatedUtc = gameData?.LastUpdatedUtc ?? default;
                }
                else
                {
                    ClearSingleGameThemeProperties(gameId);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to populate single-game data synchronously for game {gameId}.");
                ClearSingleGameThemeProperties(gameId);
            }
        }

        private void RequestRefresh()
        {
            RequestLibraryRefresh(includeHeavyAchievementLists: false, requireFullscreen: true);
        }

        private void RequestLibraryRefresh(
            bool includeHeavyAchievementLists,
            bool requireFullscreen = false)
        {
            if (requireFullscreen && !IsFullscreen())
            {
                return;
            }

            CancellationToken token;
            lock (_refreshLock)
            {
                try { _refreshCts?.Cancel(); } catch { }
                try { _refreshCts?.Dispose(); } catch { }
                _refreshCts = new CancellationTokenSource();
                token = _refreshCts.Token;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(LibraryRefreshDelay, token).ConfigureAwait(false);

                    var state = BuildLibraryState(
                        includeHeavyAchievementLists,
                        token,
                        out _,
                        out _);

                    Action applyState = () =>
                    {
                        _hasLoadedLibraryState = true;
                        _lastLibraryRefreshIncludedHeavyAchievementLists = includeHeavyAchievementLists;
                        ApplyLibraryState(state);
                    };

                    var uiDispatcher = _api.MainView?.UIDispatcher ?? Application.Current?.Dispatcher;
                    if (uiDispatcher == null)
                    {
                        applyState();
                    }
                    else
                    {
                        uiDispatcher.InvokeIfNeeded(applyState, DispatcherPriority.Background);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Failed to refresh theme integration snapshot.");
                }
            }, token);
        }

        private LibraryRuntimeState BuildLibraryState(
            bool includeHeavyAchievementLists,
            CancellationToken token,
            out bool usedCachedSummary,
            out int? hydratedCount)
        {
            token.ThrowIfCancellationRequested();

            usedCachedSummary = false;
            hydratedCount = null;

#if !TEST
            if (_libraryProjectionService != null)
            {
                if (!includeHeavyAchievementLists)
                {
                    return _libraryProjectionService.GetThemeLightState(
                        ThemeRecentUnlockSummaryLimit,
                        token,
                        out usedCachedSummary,
                        out hydratedCount);
                }

                usedCachedSummary = false;
                return _libraryProjectionService.GetThemeFullState(token, out hydratedCount);
            }
#endif

            if (!includeHeavyAchievementLists)
            {
                var summaryData = _achievementDataService.GetCachedSummaryDataForTheme(ThemeRecentUnlockSummaryLimit);
                if (summaryData != null)
                {
                    usedCachedSummary = true;
                    return LibraryRuntimeStateBuilder.BuildFromCachedSummary(summaryData, _api, token);
                }
            }

            var allData = _achievementDataService.GetAllVisibleGameAchievementDataForTheme() ?? new List<GameAchievementData>();
            hydratedCount = allData.Count;
            token.ThrowIfCancellationRequested();

            return LibraryRuntimeStateBuilder.Build(
                allData,
                _api,
                token,
                includeHeavyAchievementLists);
        }

        private void ApplyLibraryState(LibraryRuntimeState state)
        {
            _runtimeState.Library = state ?? new LibraryRuntimeState();
            var library = _runtimeState.Library;
            PruneGameCommandCaches(library.AllGamesWithAchievements);

            _settings.ModernTheme.CompletedGamesAsc = ProjectGameSummaries(library.CompletedGamesAsc);
            _settings.ModernTheme.CompletedGamesDesc = ProjectGameSummaries(library.CompletedGamesDesc);
            _settings.ModernTheme.GameSummariesAsc = ProjectGameSummaries(library.GameSummariesAsc);
            _settings.ModernTheme.GameSummariesDesc = ProjectGameSummaries(library.GameSummariesDesc);
            _settings.ModernTheme.TotalCommon = library.TotalCommon;
            _settings.ModernTheme.TotalUncommon = library.TotalUncommon;
            _settings.ModernTheme.TotalRare = library.TotalRare;
            _settings.ModernTheme.TotalUltraRare = library.TotalUltraRare;
            _settings.ModernTheme.TotalRareAndUltraRare = library.TotalRareAndUltraRare;
            _settings.ModernTheme.TotalOverall = library.TotalOverall;
            _settings.ModernTheme.CollectorScore = library.CollectorScore;
            _settings.ModernTheme.CollectorLevel = library.CollectorLevel;
            _settings.ModernTheme.CollectorLevelProgress = library.CollectorLevelProgress;
            _settings.ModernTheme.CollectorRank = library.CollectorRank;
            _settings.ModernTheme.PrestigeScore = library.PrestigeScore;
            _settings.ModernTheme.PrestigeLevel = library.PrestigeLevel;
            _settings.ModernTheme.PrestigeLevelProgress = library.PrestigeLevelProgress;
            _settings.ModernTheme.PrestigeRank = library.PrestigeRank;
            _settings.ModernTheme.SteamGames = ProjectGameSummaries(library.SteamGames);
            _settings.ModernTheme.GOGGames = ProjectGameSummaries(library.GOGGames);
            _settings.ModernTheme.EpicGames = ProjectGameSummaries(library.EpicGames);
            _settings.ModernTheme.BattleNetGames = ProjectGameSummaries(library.BattleNetGames);
            _settings.ModernTheme.EAGames = ProjectGameSummaries(library.EAGames);
            _settings.ModernTheme.XboxGames = ProjectGameSummaries(library.XboxGames);
            _settings.ModernTheme.PSNGames = ProjectGameSummaries(library.PSNGames);
            _settings.ModernTheme.RetroAchievementsGames = ProjectGameSummaries(library.RetroAchievementsGames);
            _settings.ModernTheme.AppleGames = ProjectGameSummaries(library.AppleGames);
            _settings.ModernTheme.GooglePlayGames = ProjectGameSummaries(library.GooglePlayGames);
            _settings.ModernTheme.HoyoverseGames = ProjectGameSummaries(library.HoyoverseGames);
            _settings.ModernTheme.UbisoftGames = ProjectGameSummaries(library.UbisoftGames);
            _settings.ModernTheme.RPCS3Games = ProjectGameSummaries(library.RPCS3Games);
            _settings.ModernTheme.XeniaGames = ProjectGameSummaries(library.XeniaGames);
            _settings.ModernTheme.ShadPS4Games = ProjectGameSummaries(library.ShadPS4Games);
            _settings.ModernTheme.ManualGames = ProjectGameSummaries(library.ManualGames);
            _settings.ModernTheme.MostRecentUnlocksTop3 = library.MostRecentUnlocksTop3;
            _settings.ModernTheme.MostRecentUnlocksTop5 = library.MostRecentUnlocksTop5;
            _settings.ModernTheme.MostRecentUnlocksTop10 = library.MostRecentUnlocksTop10;
            _settings.ModernTheme.RarestRecentUnlocksTop3 = library.RarestRecentUnlocksTop3;
            _settings.ModernTheme.RarestRecentUnlocksTop5 = library.RarestRecentUnlocksTop5;
            _settings.ModernTheme.RarestRecentUnlocksTop10 = library.RarestRecentUnlocksTop10;

            _settings.LegacyTheme.HasDataAllGames = library.HasData;
            _settings.LegacyTheme.GamesWithAchievements = ProjectGameSummaries(library.GameSummariesDesc);
            _settings.LegacyTheme.TotalTrophies = library.TotalTrophies;
            _settings.LegacyTheme.PlatinumTrophies = library.PlatinumTrophies;
            _settings.LegacyTheme.GoldTrophies = library.GoldTrophies;
            _settings.LegacyTheme.SilverTrophies = library.SilverTrophies;
            _settings.LegacyTheme.BronzeTrophies = library.BronzeTrophies;
            _settings.LegacyTheme.Level = library.Level;
            _settings.LegacyTheme.LevelProgress = library.LevelProgress;
            _settings.LegacyTheme.Rank = !string.IsNullOrWhiteSpace(library.Rank) ? library.Rank : "Bronze5";

            _settings.LegacyTheme.AllGamesWithAchievements = ProjectGameSummaries(library.AllGamesWithAchievements);
            _settings.PlatinumGames = ProjectGameSummaries(library.PlatinumGames);
            _settings.LegacyTheme.PlatinumGamesAscending = ProjectGameSummaries(library.PlatinumGamesAscending);
            _settings.LegacyTheme.GSTotal = library.TotalTrophies > 0 ? library.TotalTrophies.ToString() : "0";
            _settings.LegacyTheme.GSPlat = library.TotalTrophies > 0 ? library.PlatinumTrophies.ToString() : "0";
            _settings.LegacyTheme.GS90 = library.TotalTrophies > 0 ? library.GoldTrophies.ToString() : "0";
            _settings.LegacyTheme.GS30 = library.TotalTrophies > 0 ? library.SilverTrophies.ToString() : "0";
            _settings.LegacyTheme.GS15 = library.TotalTrophies > 0 ? library.BronzeTrophies.ToString() : "0";
            _settings.LegacyTheme.GSScore = library.TotalTrophies > 0 ? library.Score.ToString("N0") : "0";
            _settings.LegacyTheme.GSLevel = library.TotalTrophies > 0 ? library.Level.ToString() : "0";
            _settings.LegacyTheme.GSLevelProgress = library.TotalTrophies > 0 ? library.LevelProgress : 0;
            _settings.LegacyTheme.GSRank = library.TotalTrophies > 0 && !string.IsNullOrWhiteSpace(library.Rank) ? library.Rank : "Bronze5";

            var shouldUpdateHeavyLists = library.HeavyListsBuilt || !library.HasData;
            if (shouldUpdateHeavyLists)
            {
                _settings.ModernTheme.AllAchievementsUnlockAsc = library.AllAchievementsUnlockAsc;
                _settings.ModernTheme.AllAchievementsUnlockDesc = library.AllAchievementsUnlockDesc;
                _settings.ModernTheme.AllAchievementsRarityAsc = library.AllAchievementsRarityAsc;
                _settings.ModernTheme.AllAchievementsRarityDesc = library.AllAchievementsRarityDesc;
                _settings.ModernTheme.MostRecentUnlocks = library.MostRecentUnlocks;
                _settings.ModernTheme.RarestRecentUnlocks = library.RarestRecentUnlocks;
            }

            ApplyDynamicLibraryAchievementBindings(updateOptions: false);
            ApplyDynamicGameSummaryBindings(updateOptions: false);
            ApplyDynamicOptionBindings();

            NotifySettingProperties(ThemeDelegatedPropertyCatalog.CompatibilityAllGames);
            NotifySettingProperties(ThemeDelegatedPropertyCatalog.ModernAllGamesCore);
            if (shouldUpdateHeavyLists)
            {
                NotifySettingProperties(ThemeDelegatedPropertyCatalog.ModernAllGamesHeavy);
            }
            NotifySettingProperties(ThemeDelegatedPropertyCatalog.DynamicAllGames);
            NotifySettingProperties(ThemeDelegatedPropertyCatalog.DynamicFriends);
        }

        #endregion

        #region Per-Game Theme Integration

        /// <summary>
        /// Apply a single-game snapshot to theme-exposed settings properties.
        /// Intended to be executed on the UI thread.
        /// </summary>
        private void ApplySelectedGameState(SelectedGameRuntimeState state)
        {
            if (state == null || !state.HasAchievements)
            {
                ClearSingleGameThemeProperties();
                return;
            }

            _runtimeState.SelectedGame = state;
            _runtimeState.SelectedGameAchievements.GameKey = state.GameId.ToString("D");
            _runtimeState.SelectedGameAchievements.GameLabel = ResolveGameLabel(state.GameId);
            _settings.ModernTheme.HasAchievements = true;
            _settings.ModernTheme.SelectedGameId = state.GameId;
            _settings.ModernTheme.HasCustomAchievementOrder = state.HasCustomAchievementOrder;
            _settings.ModernTheme.IsCompleted = state.IsCompleted;
            _settings.ModernTheme.AchievementCount = state.AchievementCount;
            _settings.ModernTheme.UnlockedCount = state.UnlockedCount;
            _settings.ModernTheme.LockedCount = state.LockedCount;
            _settings.ModernTheme.ProgressPercentage = state.ProgressPercentage;
            _settings.ModernTheme.Common = state.Common;
            _settings.ModernTheme.Uncommon = state.Uncommon;
            _settings.ModernTheme.Rare = state.Rare;
            _settings.ModernTheme.UltraRare = state.UltraRare;
            _settings.ModernTheme.RareAndUltraRare = state.RareAndUltraRare;
            _settings.ModernTheme.AchievementDefaultOrder = state.AchievementDefaultOrder;
            _settings.ModernTheme.AllAchievements = state.AllAchievements;
            _settings.ModernTheme.AchievementsNewestFirst = state.AchievementsNewestFirst;
            _settings.ModernTheme.AchievementsOldestFirst = state.AchievementsOldestFirst;
            _settings.ModernTheme.AchievementsRarityAsc = state.AchievementsRarityAsc;
            _settings.ModernTheme.AchievementsRarityDesc = state.AchievementsRarityDesc;

            _settings.LegacyTheme.HasData = true;
            _settings.LegacyTheme.Total = state.AchievementCount;
            _settings.LegacyTheme.Unlocked = state.UnlockedCount;
            _settings.LegacyTheme.Percent = state.ProgressPercentage;
            _settings.LegacyTheme.Is100Percent = state.UnlockedCount == state.AchievementCount && state.AchievementCount > 0;
            _settings.LegacyTheme.Locked = state.LockedCount;
            _settings.LegacyTheme.TotalGamerScore = 0;
            _settings.LegacyTheme.EstimateTimeToUnlock = string.Empty;
            _settings.LegacyTheme.ListAchievements = state.AllAchievements;
            _settings.LegacyTheme.ListAchUnlockDateAsc = state.AchievementsOldestFirst;
            _settings.LegacyTheme.ListAchUnlockDateDesc = state.AchievementsNewestFirst;

            ApplyDynamicSelectedGameBindings();

            NotifySettingProperties(ThemeDelegatedPropertyCatalog.SingleGameTheme);
            NotifySettingProperties(ThemeDelegatedPropertyCatalog.SingleGameLegacy);
        }

        /// <summary>
        /// Clear per-game theme properties when no game is selected or game has no achievements.
        /// </summary>
        public void ClearSingleGameThemeProperties(Guid? selectedGameId = null)
        {
            _runtimeState.SelectedGame = SelectedGameRuntimeState.Empty;
            _runtimeState.SelectedGameAchievements.GameKey = selectedGameId?.ToString("D") ?? string.Empty;
            _runtimeState.SelectedGameAchievements.GameLabel = selectedGameId.HasValue
                ? ResolveGameLabel(selectedGameId.Value)
                : string.Empty;
            _settings.ModernTheme.HasAchievements = false;
            _settings.ModernTheme.SelectedGameId = selectedGameId;
            _settings.ModernTheme.HasCustomAchievementOrder = false;
            _settings.ModernTheme.IsCompleted = false;
            _settings.ModernTheme.AchievementCount = 0;
            _settings.ModernTheme.UnlockedCount = 0;
            _settings.ModernTheme.LockedCount = 0;
            _settings.ModernTheme.ProgressPercentage = 0;

            _settings.ModernTheme.AchievementDefaultOrder = EmptyAchievementList;
            _settings.ModernTheme.AllAchievements = EmptyAchievementList;
            _settings.ModernTheme.AchievementsNewestFirst = EmptyAchievementList;
            _settings.ModernTheme.AchievementsOldestFirst = EmptyAchievementList;
            _settings.ModernTheme.AchievementsRarityAsc = EmptyAchievementList;
            _settings.ModernTheme.AchievementsRarityDesc = EmptyAchievementList;

            _settings.ModernTheme.Common = EmptyRarityStats;
            _settings.ModernTheme.Uncommon = EmptyRarityStats;
            _settings.ModernTheme.Rare = EmptyRarityStats;
            _settings.ModernTheme.UltraRare = EmptyRarityStats;
            _settings.ModernTheme.RareAndUltraRare = EmptyRarityStats;
            _settings.LegacyTheme.HasData = false;
            _settings.LegacyTheme.Total = 0;
            _settings.LegacyTheme.Unlocked = 0;
            _settings.LegacyTheme.Percent = 0;

            _settings.LegacyTheme.Is100Percent = false;
            _settings.LegacyTheme.Locked = 0;
            _settings.LegacyTheme.TotalGamerScore = 0;
            _settings.LegacyTheme.EstimateTimeToUnlock = string.Empty;

            _settings.LegacyTheme.ListAchievements = EmptyAchievementList;
            _settings.LegacyTheme.ListAchUnlockDateAsc = EmptyAchievementList;
            _settings.LegacyTheme.ListAchUnlockDateDesc = EmptyAchievementList;

            _appliedGameId = null;
            _appliedLastUpdatedUtc = default;

            ApplyDynamicSelectedGameBindings();

            NotifySettingProperties(ThemeDelegatedPropertyCatalog.SingleGameTheme);
            NotifySettingProperties(ThemeDelegatedPropertyCatalog.SingleGameLegacy);
        }

        #endregion

        private ObservableCollection<GameAchievementSummary> ProjectGameSummaries(IEnumerable<GameAchievementSummary> items)
        {
            var projected = (items ?? Enumerable.Empty<GameAchievementSummary>())
                .Select(item => new GameAchievementSummary(
                    item.GameId,
                    item.Name,
                    item.Platform,
                    item.CoverImagePath,
                    item.Progress,
                    item.GoldCount,
                    item.SilverCount,
                    item.BronzeCount,
                    item.IsCompleted,
                    item.LastUnlockDate,
                    GetOpenViewAchievementsCommand(item.GameId),
                    item.Common,
                    item.Uncommon,
                    item.Rare,
                    item.UltraRare,
                    item.RareAndUltraRare,
                    item.Overall,
                    item.ProviderKey,
                    item.ProviderName,
                    item.LastPlayed,
                    item.UnlockedCount,
                    item.AchievementCount,
                    openManageAchievementsWindow: GetOpenManageAchievementsCommand(item.GameId)))
                .ToList();

            AttachGameSummaryCommands(projected);
            return new ObservableCollection<GameAchievementSummary>(projected);
        }

        private ObservableCollection<FriendGameAchievementSummary> ProjectFriendGameSummaries(
            IEnumerable<FriendGameSummaryItem> items)
        {
            var projected = (items ?? Enumerable.Empty<FriendGameSummaryItem>())
                .Where(item => item != null)
                .Select(item =>
                {
                    var gameId = item.PlayniteGameId ?? Guid.Empty;
                    var hasLocalGame = gameId != Guid.Empty;
                    var common = AchievementGameStats.CreateRarityStats(item.CommonCount, item.TotalCommonPossible);
                    var uncommon = AchievementGameStats.CreateRarityStats(item.UncommonCount, item.TotalUncommonPossible);
                    var rare = AchievementGameStats.CreateRarityStats(item.RareCount, item.TotalRarePossible);
                    var ultraRare = AchievementGameStats.CreateRarityStats(item.UltraRareCount, item.TotalUltraRarePossible);

                    return new FriendGameAchievementSummary(
                        gameId,
                        item.GameName,
                        item.Provider,
                        item.GameCoverPath,
                        item.FriendCompletionPercent,
                        item.RareCount + item.UltraRareCount,
                        item.UncommonCount,
                        item.CommonCount,
                        item.IsCompleted,
                        item.LastFriendUnlockUtc?.ToLocalTime() ?? DateTime.MinValue,
                        hasLocalGame ? GetOpenViewAchievementsCommand(gameId) : null,
                        common,
                        uncommon,
                        rare,
                        ultraRare,
                        null,
                        null,
                        item.ProviderKey,
                        item.Provider,
                        item.LastFriendPlayedUtc,
                        item.UniqueFriendUnlockedAchievementsCount,
                        item.TotalAchievements,
                        openManageAchievementsWindow: hasLocalGame ? GetOpenManageAchievementsCommand(gameId) : null)
                    {
                        AppId = item.AppId,
                        ProviderGameKey = item.ProviderGameKey,
                        FriendCount = item.FriendCount,
                        FriendsWithUnlocksCount = item.FriendsWithUnlocksCount,
                        FriendUnlockedAchievementsCount = item.FriendUnlockedAchievementsCount,
                        UniqueFriendUnlockedAchievementsCount = item.UniqueFriendUnlockedAchievementsCount,
                        CollectionScore = item.CollectionScore,
                        CollectionScoreTotal = item.CollectionScoreTotal,
                        PrestigeScore = item.PrestigeScore,
                        PrestigeScoreTotal = item.PrestigeScoreTotal,
                        Points = item.Points,
                        TrophyPlatinumCount = item.TrophyPlatinumCount,
                        TrophyGoldCount = item.TrophyGoldCount,
                        TrophySilverCount = item.TrophySilverCount,
                        TrophyBronzeCount = item.TrophyBronzeCount,
                        TrophyPlatinumTotal = item.TrophyPlatinumTotal,
                        TrophyGoldTotal = item.TrophyGoldTotal,
                        TrophySilverTotal = item.TrophySilverTotal,
                        TrophyBronzeTotal = item.TrophyBronzeTotal,
                        LastFriendUnlockUtc = item.LastFriendUnlockUtc,
                        TotalFriendPlaytimeMinutes = item.TotalFriendPlaytimeMinutes,
                        AverageFriendPlaytimeMinutes = item.AverageFriendPlaytimeMinutes,
                        LastFriendPlayedUtc = item.LastFriendPlayedUtc,
                        LastFriendScrapedUtc = item.LastFriendScrapedUtc,
                        LastFriendScrapeStatus = item.LastFriendScrapeStatus
                    };
                })
                .ToList();

            AttachFriendGameSummaryCommands(projected);
            return new ObservableCollection<FriendGameAchievementSummary>(projected);
        }

        private RelayCommand GetOpenViewAchievementsCommand(Guid gameId)
        {
            if (!_openViewAchievementsCommands.TryGetValue(gameId, out var command))
            {
                command = new RelayCommand(_ => OpenViewAchievementsWindow(gameId));
                _openViewAchievementsCommands[gameId] = command;
            }

            return command;
        }

        private RelayCommand GetOpenManageAchievementsCommand(Guid gameId)
        {
            if (!_openManageAchievementsCommands.TryGetValue(gameId, out var command))
            {
                command = new RelayCommand(_ => OpenManageAchievementsWindow(gameId));
                _openManageAchievementsCommands[gameId] = command;
            }

            return command;
        }

        private void PruneGameCommandCaches(IEnumerable<GameAchievementSummary> currentGames)
        {
            var currentGameIds = new HashSet<Guid>(
                (currentGames ?? Enumerable.Empty<GameAchievementSummary>())
                    .Where(item => item != null && item.GameId != Guid.Empty)
                    .Select(item => item.GameId));
            PruneCommandCache(_openViewAchievementsCommands, currentGameIds);
            PruneCommandCache(_openManageAchievementsCommands, currentGameIds);
        }

        private static void PruneCommandCache(
            Dictionary<Guid, RelayCommand> commands,
            HashSet<Guid> currentGameIds)
        {
            if (commands == null || commands.Count == 0)
            {
                return;
            }

            var staleKeys = commands.Keys
                .Where(key => !currentGameIds.Contains(key))
                .ToList();
            for (var i = 0; i < staleKeys.Count; i++)
            {
                commands.Remove(staleKeys[i]);
            }
        }

        private void Settings_DynamicThemeDefaultsChanged(object sender, EventArgs e)
        {
            ApplyDynamicThemeDefaultsFromSettings(notify: true);
        }

        private void ApplyDynamicThemeDefaultsFromSettings(bool notify)
        {
            var selectedChanged = ApplySelectedGameAchievementDefaultsFromSettings();
            var libraryChanged = ApplyLibraryAchievementDefaultsFromSettings();
            var summariesChanged = ApplyGameSummaryDefaultsFromSettings();

            if (selectedChanged)
            {
                ApplyDynamicSelectedGameBindings();
                if (notify)
                {
                    NotifySettingProperties(ThemeDelegatedPropertyCatalog.SingleGameTheme);
                }
            }

            if (libraryChanged || summariesChanged)
            {
                if (libraryChanged)
                {
                    ApplyDynamicLibraryAchievementBindings(updateOptions: false);
                }

                if (summariesChanged)
                {
                    ApplyDynamicGameSummaryBindings(updateOptions: false);
                }

                ApplyDynamicOptionBindings();

                if (notify)
                {
                    NotifySettingProperties(ThemeDelegatedPropertyCatalog.DynamicAllGames);
                }
            }
        }

        private bool ApplySelectedGameAchievementDefaultsFromSettings()
        {
            var state = _runtimeState.SelectedGameAchievements;
            var filterKey = NormalizeDefaultFilterKey(
                _settings.ModernTheme.DynamicAchievementsDefaultFilterKey,
                DynamicThemeOptionGroups.AchievementFilterKeyMap,
                state.DefaultFilterKey,
                nameof(PlayniteAchievementsSettings.DynamicAchievementsDefaultFilterKey));
            var sortKey = NormalizeDefaultKey(
                _settings.ModernTheme.DynamicAchievementsDefaultSortKey,
                DynamicThemeOptionGroups.SelectedGameAchievementSortKeyMap,
                state.DefaultSortKey,
                DynamicThemeViewKeys.Default,
                nameof(PlayniteAchievementsSettings.DynamicAchievementsDefaultSortKey));
            var directionKey = NormalizeDefaultKey(
                _settings.ModernTheme.DynamicAchievementsDefaultSortDirectionKey,
                DynamicThemeOptionGroups.SortDirectionKeyMap,
                state.DefaultSortDirectionKey,
                DynamicThemeViewKeys.Descending,
                nameof(PlayniteAchievementsSettings.DynamicAchievementsDefaultSortDirectionKey));

            var changed = state.ApplyDefaults(DynamicThemeViewKeys.All, filterKey, sortKey, directionKey);
            changed |= !KeysEqual(_settings.ModernTheme.DynamicAchievementsDefaultFilterKey, filterKey);
            changed |= !KeysEqual(_settings.ModernTheme.DynamicAchievementsDefaultSortKey, sortKey);
            changed |= !KeysEqual(_settings.ModernTheme.DynamicAchievementsDefaultSortDirectionKey, directionKey);
            return changed;
        }

        private bool ApplyLibraryAchievementDefaultsFromSettings()
        {
            var state = _runtimeState.LibraryAchievements;
            var providerKey = NormalizeProviderKeyValue(
                _settings.ModernTheme.DynamicLibraryAchievementsDefaultProviderKey,
                state.DefaultProviderKey);
            var filterKey = NormalizeDefaultFilterKey(
                _settings.ModernTheme.DynamicLibraryAchievementsDefaultFilterKey,
                DynamicThemeOptionGroups.AchievementFilterKeyMap,
                state.DefaultFilterKey,
                nameof(PlayniteAchievementsSettings.DynamicLibraryAchievementsDefaultFilterKey));
            var sortKey = NormalizeDefaultKey(
                _settings.ModernTheme.DynamicLibraryAchievementsDefaultSortKey,
                DynamicThemeOptionGroups.LibraryAchievementSortKeyMap,
                state.DefaultSortKey,
                DynamicThemeViewKeys.UnlockTime,
                nameof(PlayniteAchievementsSettings.DynamicLibraryAchievementsDefaultSortKey));
            var directionKey = NormalizeDefaultKey(
                _settings.ModernTheme.DynamicLibraryAchievementsDefaultSortDirectionKey,
                DynamicThemeOptionGroups.SortDirectionKeyMap,
                state.DefaultSortDirectionKey,
                DynamicThemeViewKeys.Descending,
                nameof(PlayniteAchievementsSettings.DynamicLibraryAchievementsDefaultSortDirectionKey));

            var changed = state.ApplyDefaults(providerKey, filterKey, sortKey, directionKey);
            changed |= !KeysEqual(_settings.ModernTheme.DynamicLibraryAchievementsDefaultProviderKey, providerKey);
            changed |= !KeysEqual(_settings.ModernTheme.DynamicLibraryAchievementsDefaultFilterKey, filterKey);
            changed |= !KeysEqual(_settings.ModernTheme.DynamicLibraryAchievementsDefaultSortKey, sortKey);
            changed |= !KeysEqual(_settings.ModernTheme.DynamicLibraryAchievementsDefaultSortDirectionKey, directionKey);
            return changed;
        }

        private bool ApplyGameSummaryDefaultsFromSettings()
        {
            var state = _runtimeState.GameSummaries;
            var providerKey = NormalizeProviderKeyValue(
                _settings.ModernTheme.DynamicGameSummariesDefaultProviderKey,
                state.DefaultProviderKey);
            var filterKey = NormalizeDefaultFilterKey(
                _settings.ModernTheme.DynamicGameSummariesDefaultFilterKey,
                DynamicThemeOptionGroups.GameSummaryFilterKeyMap,
                state.DefaultFilterKey,
                nameof(PlayniteAchievementsSettings.DynamicGameSummariesDefaultFilterKey));
            var sortKey = NormalizeDefaultKey(
                _settings.ModernTheme.DynamicGameSummariesDefaultSortKey,
                DynamicThemeOptionGroups.GameSummarySortKeyMap,
                state.DefaultSortKey,
                DynamicThemeViewKeys.LastUnlock,
                nameof(PlayniteAchievementsSettings.DynamicGameSummariesDefaultSortKey));
            var directionKey = NormalizeDefaultKey(
                _settings.ModernTheme.DynamicGameSummariesDefaultSortDirectionKey,
                DynamicThemeOptionGroups.SortDirectionKeyMap,
                state.DefaultSortDirectionKey,
                DynamicThemeViewKeys.Descending,
                nameof(PlayniteAchievementsSettings.DynamicGameSummariesDefaultSortDirectionKey));

            var changed = state.ApplyDefaults(providerKey, filterKey, sortKey, directionKey);
            changed |= !KeysEqual(_settings.ModernTheme.DynamicGameSummariesDefaultProviderKey, providerKey);
            changed |= !KeysEqual(_settings.ModernTheme.DynamicGameSummariesDefaultFilterKey, filterKey);
            changed |= !KeysEqual(_settings.ModernTheme.DynamicGameSummariesDefaultSortKey, sortKey);
            changed |= !KeysEqual(_settings.ModernTheme.DynamicGameSummariesDefaultSortDirectionKey, directionKey);
            return changed;
        }

        private void ResetDynamicAchievementsToDefaults()
        {
            var state = _runtimeState.SelectedGameAchievements;
            state.ResetToDefault();
            ApplyDynamicSelectedGameBindings();
            NotifySettingProperties(ThemeDelegatedPropertyCatalog.SingleGameTheme);
        }

        private void ResetDynamicLibraryAchievementsToDefaults()
        {
            var state = _runtimeState.LibraryAchievements;
            state.ResetToDefault();
            state.ResetGameScope();
            ApplyDynamicLibraryAchievementBindings();
            NotifySettingProperties(ThemeDelegatedPropertyCatalog.DynamicAllGames);
        }

        private void ResetDynamicGameSummariesToDefaults()
        {
            var state = _runtimeState.GameSummaries;
            state.ResetToDefault();
            state.ResetGameScope();
            ApplyDynamicGameSummaryBindings();
            NotifySettingProperties(ThemeDelegatedPropertyCatalog.DynamicAllGames);
        }

        private void SetDynamicFriendScopeProvider(object parameter)
        {
            var key = NormalizeFriendProviderScopeKey(parameter);
            if (string.IsNullOrWhiteSpace(key))
            {
                LogInvalidCommandParameter(nameof(PlayniteAchievementsSettings.SetDynamicFriendScopeProviderCommand), parameter);
                return;
            }

            var scope = _runtimeState.FriendScope;
            if (string.Equals(scope.ProviderKey, key, StringComparison.Ordinal))
            {
                return;
            }

            scope.ProviderKey = key;
            scope.UserKey = DynamicThemeViewKeys.All;
            scope.GameKey = DynamicThemeViewKeys.All;
            ApplyDynamicFriendBindings();
            NotifySettingProperties(ThemeDelegatedPropertyCatalog.DynamicFriends);
        }

        private void SetDynamicFriendScopeUser(object parameter)
        {
            var key = NormalizeFriendUserScopeKey(parameter);
            if (string.IsNullOrWhiteSpace(key))
            {
                LogInvalidCommandParameter(nameof(PlayniteAchievementsSettings.SetDynamicFriendScopeUserCommand), parameter);
                return;
            }

            var scope = _runtimeState.FriendScope;
            if (string.Equals(scope.UserKey, key, StringComparison.Ordinal))
            {
                return;
            }

            scope.UserKey = key;
            scope.GameKey = DynamicThemeViewKeys.All;
            ApplyDynamicFriendBindings();
            NotifySettingProperties(ThemeDelegatedPropertyCatalog.DynamicFriends);
        }

        private void SetDynamicFriendScopeGame(object parameter)
        {
            var key = NormalizeFriendGameScopeKey(parameter);
            if (string.IsNullOrWhiteSpace(key))
            {
                LogInvalidCommandParameter(nameof(PlayniteAchievementsSettings.SetDynamicFriendScopeGameCommand), parameter);
                return;
            }

            var scope = _runtimeState.FriendScope;
            if (string.Equals(scope.GameKey, key, StringComparison.Ordinal))
            {
                return;
            }

            scope.GameKey = key;
            ApplyDynamicFriendBindings();
            NotifySettingProperties(ThemeDelegatedPropertyCatalog.DynamicFriends);
        }

        private void ResetDynamicFriendScope()
        {
            _runtimeState.FriendScope.Reset();
            ApplyDynamicFriendBindings();
            NotifySettingProperties(ThemeDelegatedPropertyCatalog.DynamicFriends);
        }

        private void RequestFriendStateRefresh()
        {
            if (_friendCache == null)
            {
                return;
            }

            lock (_friendRefreshGate)
            {
                _friendRefreshRequestVersion++;
                try { _friendRefreshCts?.Cancel(); } catch { }
                try { _friendRefreshCts?.Dispose(); } catch { }
                _friendRefreshCts = new CancellationTokenSource();

                if (_friendRefreshRunner == null || _friendRefreshRunner.IsCompleted)
                {
                    _friendRefreshRunner = Task.Run(RunFriendStateRefreshLoop);
                }
            }
        }

        private async Task RunFriendStateRefreshLoop()
        {
            while (true)
            {
                int version;
                CancellationToken token;
                lock (_friendRefreshGate)
                {
                    version = _friendRefreshRequestVersion;
                    token = _friendRefreshCts?.Token ?? CancellationToken.None;
                }

                try
                {
                    await Task.Delay(FriendRefreshDelay, token).ConfigureAwait(false);
                    var state = await BuildFriendStateAsync(token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();

                    void Apply()
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        lock (_friendRefreshGate)
                        {
                            if (version < _friendRefreshRequestVersion ||
                                version <= _friendRefreshAppliedVersion)
                            {
                                return;
                            }

                            _friendRefreshAppliedVersion = version;
                        }

                        ApplyFriendState(state, notify: true);
                    }

                    var uiDispatcher = _api?.MainView?.UIDispatcher ?? Application.Current?.Dispatcher;
                    if (uiDispatcher == null)
                    {
                        Apply();
                    }
                    else
                    {
                        uiDispatcher.InvokeIfNeeded(Apply, DispatcherPriority.Background);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Failed to refresh friend theme runtime state.");
                }

                lock (_friendRefreshGate)
                {
                    if (version >= _friendRefreshRequestVersion)
                    {
                        return;
                    }
                }
            }
        }

        private async Task<FriendRuntimeState> BuildFriendStateAsync(CancellationToken token)
        {
            if (_friendsOverviewDataCoordinator == null)
            {
                return FriendRuntimeState.Empty;
            }

            try
            {
                token.ThrowIfCancellationRequested();
                FriendsOverviewSnapshot snapshot;
                using (PerfScope.Start(_logger, "ThemeFriends.BuildState", thresholdMs: 25))
                {
                    snapshot = await _friendsOverviewDataCoordinator
                        .GetSnapshotAsync(token)
                        .ConfigureAwait(false);
                }

                token.ThrowIfCancellationRequested();
                return new FriendRuntimeState(snapshot?.Projection);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to build friend theme runtime state.");
                return FriendRuntimeState.Empty;
            }
        }

        private void ApplyFriendState(FriendRuntimeState state, bool notify)
        {
            _runtimeState.Friends = state ?? FriendRuntimeState.Empty;
            ValidateDynamicFriendScope(_runtimeState.Friends);
            ApplyDynamicFriendBindings(updateOptions: false);
            if (notify)
            {
                NotifySettingProperties(ThemeDelegatedPropertyCatalog.DynamicFriends);
            }
        }

        private void ValidateDynamicFriendScope(FriendRuntimeState state)
        {
            state = state ?? FriendRuntimeState.Empty;
            var scope = _runtimeState.FriendScope;

            if (!FriendOverviewProjection.IsAllScope(scope.ProviderKey) &&
                !state.Friends.Any(friend => FriendMatchesProvider(friend, scope.ProviderKey)) &&
                !state.AggregateGames.Any(game => string.Equals(game?.ProviderKey, scope.ProviderKey, StringComparison.OrdinalIgnoreCase)))
            {
                scope.ProviderKey = DynamicThemeViewKeys.All;
                scope.UserKey = DynamicThemeViewKeys.All;
                scope.GameKey = DynamicThemeViewKeys.All;
            }

            if (!FriendOverviewProjection.IsAllScope(scope.UserKey))
            {
                var friend = state.Projection?.FindFriend(scope.UserKey);
                if (friend == null ||
                    !FriendMatchesProvider(friend, scope.ProviderKey))
                {
                    scope.UserKey = DynamicThemeViewKeys.All;
                    scope.GameKey = DynamicThemeViewKeys.All;
                }
            }

            if (!FriendOverviewProjection.IsAllScope(scope.GameKey) &&
                !GetFriendGameScopeCandidates(state, scope.ProviderKey, scope.UserKey)
                    .Any(game => string.Equals(
                        FriendOverviewProjection.GetGameScopeKey(game),
                        scope.GameKey,
                        StringComparison.OrdinalIgnoreCase)))
            {
                scope.GameKey = DynamicThemeViewKeys.All;
            }
        }

        private string NormalizeFriendProviderScopeKey(object parameter)
        {
            var raw = ExtractProviderKeyParameter(parameter);
            if (string.IsNullOrWhiteSpace(raw) ||
                string.Equals(raw, DynamicThemeViewKeys.All, StringComparison.OrdinalIgnoreCase))
            {
                return DynamicThemeViewKeys.All;
            }

            return FindCanonicalProviderKey(raw) ?? raw;
        }

        private string NormalizeFriendUserScopeKey(object parameter)
        {
            var raw = ExtractFriendUserScopeKey(parameter);
            if (string.IsNullOrWhiteSpace(raw) ||
                string.Equals(raw, DynamicThemeViewKeys.All, StringComparison.OrdinalIgnoreCase))
            {
                return DynamicThemeViewKeys.All;
            }

            return (_runtimeState.Friends?.Friends ?? Enumerable.Empty<FriendSummaryItem>())
                .Select(FriendOverviewProjection.GetFriendScopeKey)
                .FirstOrDefault(key => string.Equals(key, raw, StringComparison.OrdinalIgnoreCase)) ?? raw;
        }

        private string NormalizeFriendGameScopeKey(object parameter)
        {
            var raw = ExtractFriendGameScopeKey(parameter);
            if (string.IsNullOrWhiteSpace(raw) ||
                string.Equals(raw, DynamicThemeViewKeys.All, StringComparison.OrdinalIgnoreCase))
            {
                return DynamicThemeViewKeys.All;
            }

            var state = _runtimeState.Friends ?? FriendRuntimeState.Empty;
            var scope = _runtimeState.FriendScope;
            return GetFriendGameScopeCandidates(state, scope.ProviderKey, scope.UserKey)
                .Select(FriendOverviewProjection.GetGameScopeKey)
                .FirstOrDefault(key => string.Equals(key, raw, StringComparison.OrdinalIgnoreCase)) ?? raw;
        }

        private static string ExtractFriendUserScopeKey(object parameter)
        {
            if (parameter == DependencyProperty.UnsetValue)
            {
                return null;
            }

            switch (parameter)
            {
                case DynamicThemeOption option:
                    return option.Key;
                case FriendSummaryItem friend:
                    return FriendOverviewProjection.GetFriendScopeKey(friend);
                case FriendAchievementDisplayItem achievement:
                    return FriendOverviewProjection.GetFriendScopeKey(achievement);
                default:
                    return DynamicThemeFilterExpression.NormalizeParameter(parameter);
            }
        }

        private static string ExtractFriendGameScopeKey(object parameter)
        {
            if (parameter == DependencyProperty.UnsetValue)
            {
                return null;
            }

            switch (parameter)
            {
                case DynamicThemeOption option:
                    return option.Key;
                case FriendGameSummaryItem game:
                    return FriendOverviewProjection.BuildGameUnlockKey(game.ProviderKey, game.ProviderGameKey, game.AppId, game.PlayniteGameId);
                case FriendGameAchievementSummary summary:
                    return FriendOverviewProjection.BuildGameUnlockKey(
                        summary.ProviderKey,
                        summary.ProviderGameKey,
                        summary.AppId,
                        summary.GameId != Guid.Empty ? summary.GameId : (Guid?)null);
                case FriendAchievementDisplayItem achievement:
                    return FriendOverviewProjection.BuildGameUnlockKey(
                        achievement.ProviderKey,
                        achievement.ProviderGameKey,
                        achievement.AppId,
                        achievement.PlayniteGameId);
                default:
                    return DynamicThemeFilterExpression.NormalizeParameter(parameter);
            }
        }

        private static bool ProviderMatches(string itemProviderKey, string scopeProviderKey)
        {
            return FriendOverviewProjection.IsAllScope(scopeProviderKey) ||
                   string.Equals(itemProviderKey, scopeProviderKey, StringComparison.OrdinalIgnoreCase);
        }

        private string NormalizeDefaultKey(
            string rawKey,
            IReadOnlyDictionary<string, string> keyMap,
            string currentKey,
            string fallbackKey,
            string sourceName)
        {
            if (DynamicThemeFilterExpression.TryNormalizeOne(rawKey, keyMap, out var normalized))
            {
                return normalized;
            }

            if (!string.IsNullOrWhiteSpace(rawKey))
            {
                LogInvalidCommandParameter(sourceName, rawKey);
            }

            return !string.IsNullOrWhiteSpace(currentKey) ? currentKey : fallbackKey;
        }

        private string NormalizeDefaultFilterKey(
            string rawKey,
            IReadOnlyDictionary<string, string> keyMap,
            string currentKey,
            string sourceName)
        {
            if (DynamicThemeFilterExpression.TryNormalize(rawKey, keyMap, out var normalized))
            {
                return normalized;
            }

            if (!string.IsNullOrWhiteSpace(rawKey))
            {
                LogInvalidCommandParameter(sourceName, rawKey);
            }

            return !string.IsNullOrWhiteSpace(currentKey) ? currentKey : DynamicThemeViewKeys.All;
        }

        private string NormalizeProviderKeyValue(
            object parameter,
            string currentKey)
        {
            var raw = ExtractProviderKeyParameter(parameter);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return !string.IsNullOrWhiteSpace(currentKey) ? currentKey : DynamicThemeViewKeys.All;
            }

            if (string.Equals(raw, DynamicThemeViewKeys.All, StringComparison.OrdinalIgnoreCase))
            {
                return DynamicThemeViewKeys.All;
            }

            return FindCanonicalProviderKey(raw) ?? raw;
        }

        private static string ExtractProviderKeyParameter(object parameter)
        {
            if (parameter == DependencyProperty.UnsetValue)
            {
                return null;
            }

            switch (parameter)
            {
                case DynamicThemeOption option:
                    return option.Key;
                case AchievementDetail achievement:
                    return achievement.ProviderKey;
                case AchievementDisplayItem displayItem:
                    return displayItem.ProviderKey;
                case GameAchievementSummary summary:
                    return summary.ProviderKey;
                case GameSummaryItem game:
                    return game.ProviderKey;
                case FriendSummaryItem friend:
                    return GetFriendProviderScopeKeys(friend).FirstOrDefault() ?? friend.ProviderKey;
                default:
                    return DynamicThemeFilterExpression.NormalizeParameter(parameter);
            }
        }

        private string FindCanonicalProviderKey(string providerKey)
        {
            if (string.IsNullOrWhiteSpace(providerKey))
            {
                return null;
            }

            var knownProviderKey = DynamicThemeOptionGroups.KnownProviderKeys
                .FirstOrDefault(key => string.Equals(key, providerKey, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(knownProviderKey))
            {
                return knownProviderKey;
            }

            return EnumerateKnownProviderKeys()
                .FirstOrDefault(key => string.Equals(key, providerKey, StringComparison.OrdinalIgnoreCase));
        }

        private static bool KeysEqual(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
        }

        private ICommand CreateDynamicCommand(
            string commandName,
            IReadOnlyDictionary<string, string> keyMap,
            Func<string> getCurrentKey,
            Action<string> setCurrentKey,
            Action applyBindings,
            string[] propertyNamesToNotify)
        {
            return CreateDynamicCommand(
                commandName,
                (object parameter, out string normalizedKey) => DynamicThemeFilterExpression.TryNormalizeOne(
                    parameter,
                    keyMap,
                    out normalizedKey),
                getCurrentKey,
                setCurrentKey,
                applyBindings,
                propertyNamesToNotify);
        }

        private ICommand CreateDynamicCommand(
            string commandName,
            CommandParameterNormalizer normalizer,
            Func<string> getCurrentKey,
            Action<string> setCurrentKey,
            Action applyBindings,
            string[] propertyNamesToNotify)
        {
            return new RelayCommand(parameter => ExecuteDynamicCommand(
                parameter,
                commandName,
                normalizer,
                getCurrentKey,
                setCurrentKey,
                applyBindings,
                propertyNamesToNotify));
        }

        private void ExecuteDynamicCommand(
            object parameter,
            string commandName,
            CommandParameterNormalizer normalizer,
            Func<string> getCurrentKey,
            Action<string> setCurrentKey,
            Action applyBindings,
            string[] propertyNamesToNotify)
        {
            if (normalizer == null || getCurrentKey == null || setCurrentKey == null || applyBindings == null)
            {
                return;
            }

            if (!normalizer(parameter, out var normalizedKey))
            {
                LogInvalidCommandParameter(commandName, parameter);
                return;
            }

            if (string.Equals(getCurrentKey(), normalizedKey, StringComparison.Ordinal))
            {
                return;
            }

            setCurrentKey(normalizedKey);
            applyBindings();
            NotifySettingProperties(propertyNamesToNotify);
        }

        private static ICommand CreateItemCommand(ICommand rootCommand, object owner)
        {
            return rootCommand == null
                ? null
                : new RelayCommand(parameter => ExecuteRootCommand(rootCommand, parameter ?? owner));
        }

        private static void ExecuteRootCommand(ICommand command, object parameter)
        {
            if (command?.CanExecute(parameter) == true)
            {
                command.Execute(parameter);
            }
        }

        private void AttachAchievementCommands(IEnumerable<AchievementDetail> items)
        {
            foreach (var item in items ?? Enumerable.Empty<AchievementDetail>())
            {
                if (item == null)
                {
                    continue;
                }

                item.SetDynamicAchievementsGameCommand = CreateItemCommand(_settings.SetDynamicAchievementsGameCommand, item);
                item.FilterDynamicLibraryAchievementsByProviderCommand = CreateItemCommand(_settings.FilterDynamicLibraryAchievementsByProviderCommand, item);
                item.OpenViewAchievementsWindow = CreateItemCommand(_settings.OpenViewAchievementsWindow, item);
                item.OpenManageAchievementsWindow = CreateItemCommand(_settings.OpenManageAchievementsWindow, item);
            }
        }

        private void AttachGameSummaryCommands(IEnumerable<GameAchievementSummary> items)
        {
            foreach (var item in items ?? Enumerable.Empty<GameAchievementSummary>())
            {
                if (item == null)
                {
                    continue;
                }

                item.SetDynamicAchievementsGameCommand = CreateItemCommand(_settings.SetDynamicAchievementsGameCommand, item);
                item.FilterDynamicGameSummariesByProviderCommand = CreateItemCommand(_settings.FilterDynamicGameSummariesByProviderCommand, item);
            }
        }

        private void AttachFriendSummaryCommands(IEnumerable<FriendSummaryItem> items)
        {
            foreach (var item in items ?? Enumerable.Empty<FriendSummaryItem>())
            {
                if (item == null)
                {
                    continue;
                }

                item.SetDynamicFriendScopeProviderCommand = CreateItemCommand(_settings.SetDynamicFriendScopeProviderCommand, item);
                item.SetDynamicFriendScopeUserCommand = CreateItemCommand(_settings.SetDynamicFriendScopeUserCommand, item);
            }
        }

        private void AttachFriendGameSummaryCommands(IEnumerable<FriendGameAchievementSummary> items)
        {
            foreach (var item in items ?? Enumerable.Empty<FriendGameAchievementSummary>())
            {
                if (item == null)
                {
                    continue;
                }

                item.SetDynamicAchievementsGameCommand = CreateItemCommand(_settings.SetDynamicAchievementsGameCommand, item);
                item.FilterDynamicGameSummariesByProviderCommand = CreateItemCommand(_settings.FilterDynamicGameSummariesByProviderCommand, item);
                item.SetDynamicFriendScopeProviderCommand = CreateItemCommand(_settings.SetDynamicFriendScopeProviderCommand, item);
                item.SetDynamicFriendScopeGameCommand = CreateItemCommand(_settings.SetDynamicFriendScopeGameCommand, item);
            }
        }

        private void AttachFriendAchievementCommands(IEnumerable<FriendAchievementDisplayItem> items)
        {
            foreach (var item in items ?? Enumerable.Empty<FriendAchievementDisplayItem>())
            {
                if (item == null)
                {
                    continue;
                }

                item.SetDynamicAchievementsGameCommand = CreateItemCommand(_settings.SetDynamicAchievementsGameCommand, item);
                item.FilterDynamicLibraryAchievementsByProviderCommand = CreateItemCommand(_settings.FilterDynamicLibraryAchievementsByProviderCommand, item);
                item.OpenViewAchievementsWindow = CreateItemCommand(_settings.OpenViewAchievementsWindow, item);
                item.OpenManageAchievementsWindow = CreateItemCommand(_settings.OpenManageAchievementsWindow, item);
                item.SetDynamicFriendScopeProviderCommand = CreateItemCommand(_settings.SetDynamicFriendScopeProviderCommand, item);
                item.SetDynamicFriendScopeUserCommand = CreateItemCommand(_settings.SetDynamicFriendScopeUserCommand, item);
                item.SetDynamicFriendScopeGameCommand = CreateItemCommand(_settings.SetDynamicFriendScopeGameCommand, item);
            }
        }

        private void ApplyDynamicSelectedGameBindings()
        {
            ApplyDynamicSelectedGameBindings(updateOptions: true);
        }

        private void ApplyDynamicSelectedGameBindings(bool updateOptions)
        {
            var state = _runtimeState.SelectedGame ?? SelectedGameRuntimeState.Empty;
            var viewState = _runtimeState.SelectedGameAchievements;
            var items = BuildDynamicSelectedGameAchievements(state, viewState);
            AttachAchievementCommands(items);

            _settings.ModernTheme.DynamicAchievements = items;
            _settings.ModernTheme.DynamicAchievementsFilterKey = viewState.FilterKey;
            _settings.ModernTheme.DynamicAchievementsFilterLabel = DynamicThemeLabels.GetLabel(viewState.FilterKey, DynamicThemeViewKeys.All);
            _settings.ModernTheme.DynamicAchievementsSortKey = viewState.SortKey;
            _settings.ModernTheme.DynamicAchievementsSortLabel = DynamicThemeLabels.GetLabel(viewState.SortKey, DynamicThemeViewKeys.Default);
            _settings.ModernTheme.DynamicAchievementsSortDirectionKey = viewState.SortDirectionKey;
            _settings.ModernTheme.DynamicAchievementsSortDirectionLabel = DynamicThemeLabels.GetLabel(viewState.SortDirectionKey, DynamicThemeViewKeys.Descending);
            _settings.ModernTheme.DynamicAchievementsDefaultFilterKey = viewState.DefaultFilterKey;
            _settings.ModernTheme.DynamicAchievementsDefaultSortKey = viewState.DefaultSortKey;
            _settings.ModernTheme.DynamicAchievementsDefaultSortDirectionKey = viewState.DefaultSortDirectionKey;
            _settings.ModernTheme.DynamicAchievementsFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementFilterKeys,
                viewState.FilterKey,
                _settings.SetDynamicAchievementsFilterCommand);
            _settings.ModernTheme.DynamicAchievementsSortOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.SelectedGameAchievementSortKeys,
                viewState.SortKey,
                _settings.SortDynamicAchievementsCommand);
            _settings.ModernTheme.DynamicAchievementsSortDirectionOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.SortDirectionKeys,
                viewState.SortDirectionKey,
                _settings.SetDynamicAchievementsSortDirectionCommand);
            if (updateOptions)
            {
                ApplyDynamicOptionBindings();
            }
        }

        private void ApplyDynamicLibraryAchievementBindings()
        {
            ApplyDynamicLibraryAchievementBindings(updateOptions: true);
        }

        private void ApplyDynamicLibraryAchievementBindings(bool updateOptions)
        {
            var state = _runtimeState.Library ?? new LibraryRuntimeState();
            var viewState = _runtimeState.LibraryAchievements;
            var items = BuildDynamicLibraryAchievements(state, viewState);
            AttachAchievementCommands(items);

            _settings.ModernTheme.DynamicLibraryAchievements = items;
            _settings.ModernTheme.DynamicLibraryAchievementsProviderKey = viewState.ProviderKey;
            _settings.ModernTheme.DynamicLibraryAchievementsProviderLabel = DynamicThemeLabels.GetProviderLabel(viewState.ProviderKey);
            _settings.ModernTheme.DynamicLibraryAchievementsGameKey = viewState.GameKey;
            _settings.ModernTheme.DynamicLibraryAchievementsGameLabel = viewState.GameLabel;
            _settings.ModernTheme.DynamicLibraryAchievementsFilterKey = viewState.FilterKey;
            _settings.ModernTheme.DynamicLibraryAchievementsFilterLabel = DynamicThemeLabels.GetLabel(viewState.FilterKey, DynamicThemeViewKeys.All);
            _settings.ModernTheme.DynamicLibraryAchievementsSortKey = viewState.SortKey;
            _settings.ModernTheme.DynamicLibraryAchievementsSortLabel = DynamicThemeLabels.GetLabel(viewState.SortKey, DynamicThemeViewKeys.UnlockTime);
            _settings.ModernTheme.DynamicLibraryAchievementsSortDirectionKey = viewState.SortDirectionKey;
            _settings.ModernTheme.DynamicLibraryAchievementsSortDirectionLabel = DynamicThemeLabels.GetLabel(viewState.SortDirectionKey, DynamicThemeViewKeys.Descending);
            _settings.ModernTheme.DynamicLibraryAchievementsDefaultProviderKey = viewState.DefaultProviderKey;
            _settings.ModernTheme.DynamicLibraryAchievementsDefaultFilterKey = viewState.DefaultFilterKey;
            _settings.ModernTheme.DynamicLibraryAchievementsDefaultSortKey = viewState.DefaultSortKey;
            _settings.ModernTheme.DynamicLibraryAchievementsDefaultSortDirectionKey = viewState.DefaultSortDirectionKey;
            _settings.ModernTheme.DynamicLibraryAchievementsProviderOptions = DynamicThemeOptionFactory.CreateProviderOptions(
                EnumerateLibraryAchievementProviderKeys(state),
                viewState.ProviderKey,
                _settings.FilterDynamicLibraryAchievementsByProviderCommand);
            _settings.ModernTheme.DynamicLibraryAchievementsFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementFilterKeys,
                viewState.FilterKey,
                _settings.SetDynamicLibraryAchievementsFilterCommand);
            _settings.ModernTheme.DynamicLibraryAchievementsSortOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.LibraryAchievementSortKeys,
                viewState.SortKey,
                _settings.SortDynamicLibraryAchievementsCommand);
            _settings.ModernTheme.DynamicLibraryAchievementsSortDirectionOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.SortDirectionKeys,
                viewState.SortDirectionKey,
                _settings.SetDynamicLibraryAchievementsSortDirectionCommand);
            _settings.ModernTheme.DynamicLibraryAchievementStatusFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementStatusFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(viewState.FilterKey, DynamicThemeOptionGroups.AchievementStatusGroup, DynamicThemeOptionGroups.AchievementFilterGroupMap),
                _settings.SetDynamicLibraryAchievementsStatusFilterCommand);
            _settings.ModernTheme.DynamicLibraryAchievementProgressFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementProgressFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(viewState.FilterKey, DynamicThemeOptionGroups.AchievementProgressGroup, DynamicThemeOptionGroups.AchievementFilterGroupMap),
                _settings.SetDynamicLibraryAchievementsProgressFilterCommand);
            _settings.ModernTheme.DynamicLibraryAchievementRarityFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementRarityFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(viewState.FilterKey, DynamicThemeOptionGroups.AchievementRarityGroup, DynamicThemeOptionGroups.AchievementFilterGroupMap),
                _settings.SetDynamicLibraryAchievementsRarityFilterCommand);
            _settings.ModernTheme.DynamicLibraryAchievementTrophyFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementTrophyFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(viewState.FilterKey, DynamicThemeOptionGroups.AchievementTrophyGroup, DynamicThemeOptionGroups.AchievementFilterGroupMap),
                _settings.SetDynamicLibraryAchievementsTrophyFilterCommand);
            _settings.ModernTheme.DynamicLibraryAchievementCustomizationFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementCustomizationFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(viewState.FilterKey, DynamicThemeOptionGroups.AchievementCustomizationGroups, DynamicThemeOptionGroups.AchievementFilterGroupMap),
                _settings.SetDynamicLibraryAchievementsCustomizationFilterCommand);
            if (updateOptions)
            {
                ApplyDynamicOptionBindings();
            }
        }

        private void ApplyDynamicGameSummaryBindings()
        {
            ApplyDynamicGameSummaryBindings(updateOptions: true);
        }

        private void ApplyDynamicGameSummaryBindings(bool updateOptions)
        {
            var state = _runtimeState.Library ?? new LibraryRuntimeState();
            var viewState = _runtimeState.GameSummaries;
            var items = BuildDynamicGameSummaries(state, viewState);
            AttachGameSummaryCommands(items);

            _settings.ModernTheme.DynamicGameSummaries = ProjectGameSummaries(items);
            _settings.ModernTheme.DynamicGameSummariesProviderKey = viewState.ProviderKey;
            _settings.ModernTheme.DynamicGameSummariesProviderLabel = DynamicThemeLabels.GetProviderLabel(viewState.ProviderKey);
            _settings.ModernTheme.DynamicGameSummariesGameKey = viewState.GameKey;
            _settings.ModernTheme.DynamicGameSummariesGameLabel = viewState.GameLabel;
            _settings.ModernTheme.DynamicGameSummariesFilterKey = viewState.FilterKey;
            _settings.ModernTheme.DynamicGameSummariesFilterLabel = DynamicThemeLabels.GetLabel(viewState.FilterKey, DynamicThemeViewKeys.All);
            _settings.ModernTheme.DynamicGameSummariesSortKey = viewState.SortKey;
            _settings.ModernTheme.DynamicGameSummariesSortLabel = DynamicThemeLabels.GetLabel(viewState.SortKey, DynamicThemeViewKeys.LastUnlock);
            _settings.ModernTheme.DynamicGameSummariesSortDirectionKey = viewState.SortDirectionKey;
            _settings.ModernTheme.DynamicGameSummariesSortDirectionLabel = DynamicThemeLabels.GetLabel(viewState.SortDirectionKey, DynamicThemeViewKeys.Descending);
            _settings.ModernTheme.DynamicGameSummariesDefaultProviderKey = viewState.DefaultProviderKey;
            _settings.ModernTheme.DynamicGameSummariesDefaultFilterKey = viewState.DefaultFilterKey;
            _settings.ModernTheme.DynamicGameSummariesDefaultSortKey = viewState.DefaultSortKey;
            _settings.ModernTheme.DynamicGameSummariesDefaultSortDirectionKey = viewState.DefaultSortDirectionKey;
            _settings.ModernTheme.DynamicGameSummariesProviderOptions = DynamicThemeOptionFactory.CreateProviderOptions(
                (state.AllGamesWithAchievements ?? Enumerable.Empty<GameAchievementSummary>()).Select(item => item?.ProviderKey),
                viewState.ProviderKey,
                _settings.FilterDynamicGameSummariesByProviderCommand);
            _settings.ModernTheme.DynamicGameSummariesFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.GameSummaryFilterKeys,
                viewState.FilterKey,
                _settings.SetDynamicGameSummariesFilterCommand);
            _settings.ModernTheme.DynamicGameSummariesSortOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.GameSummarySortKeys,
                viewState.SortKey,
                _settings.SortDynamicGameSummariesCommand);
            _settings.ModernTheme.DynamicGameSummariesSortDirectionOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.SortDirectionKeys,
                viewState.SortDirectionKey,
                _settings.SetDynamicGameSummariesSortDirectionCommand);
            if (updateOptions)
            {
                ApplyDynamicOptionBindings();
            }
        }

        private void ApplyDynamicFriendBindings()
        {
            ApplyDynamicFriendBindings(updateOptions: true);
        }

        private void ApplyDynamicFriendBindings(bool updateOptions)
        {
            var state = _runtimeState.Friends ?? FriendRuntimeState.Empty;
            ValidateDynamicFriendScope(state);

            var scope = _runtimeState.FriendScope;
            var friendItems = BuildDynamicFriendSummaries(state, _runtimeState.FriendSummaries);
            var gameItems = BuildDynamicFriendGameSummaries(state, _runtimeState.FriendGameSummaries);
            var achievementItems = BuildDynamicFriendAchievements(state, _runtimeState.FriendAchievements);
            AttachFriendSummaryCommands(friendItems);
            AttachFriendAchievementCommands(achievementItems);

            _settings.ModernTheme.DynamicFriendSummaries = new ObservableCollection<FriendSummaryItem>(friendItems);
            _settings.ModernTheme.DynamicFriendGameSummaries = ProjectFriendGameSummaries(gameItems);
            _settings.ModernTheme.DynamicFriendAchievements = new ObservableCollection<FriendAchievementDisplayItem>(achievementItems);

            _settings.ModernTheme.DynamicFriendScopeProviderKey = scope.ProviderKey;
            _settings.ModernTheme.DynamicFriendScopeProviderLabel = DynamicThemeLabels.GetProviderLabel(scope.ProviderKey);
            _settings.ModernTheme.DynamicFriendScopeUserKey = scope.UserKey;
            _settings.ModernTheme.DynamicFriendScopeUserLabel = GetFriendScopeUserLabel(state, scope.UserKey);
            _settings.ModernTheme.DynamicFriendScopeGameKey = scope.GameKey;
            _settings.ModernTheme.DynamicFriendScopeGameLabel = GetFriendScopeGameLabel(state, scope.GameKey);

            ApplyFriendListStateBindings(
                _runtimeState.FriendSummaries,
                DynamicThemeViewKeys.LastUnlock,
                value => _settings.ModernTheme.DynamicFriendSummariesFilterKey = value,
                value => _settings.ModernTheme.DynamicFriendSummariesFilterLabel = value,
                value => _settings.ModernTheme.DynamicFriendSummariesSortKey = value,
                value => _settings.ModernTheme.DynamicFriendSummariesSortLabel = value,
                value => _settings.ModernTheme.DynamicFriendSummariesSortDirectionKey = value,
                value => _settings.ModernTheme.DynamicFriendSummariesSortDirectionLabel = value);

            ApplyFriendListStateBindings(
                _runtimeState.FriendGameSummaries,
                DynamicThemeViewKeys.LastUnlock,
                value => _settings.ModernTheme.DynamicFriendGameSummariesFilterKey = value,
                value => _settings.ModernTheme.DynamicFriendGameSummariesFilterLabel = value,
                value => _settings.ModernTheme.DynamicFriendGameSummariesSortKey = value,
                value => _settings.ModernTheme.DynamicFriendGameSummariesSortLabel = value,
                value => _settings.ModernTheme.DynamicFriendGameSummariesSortDirectionKey = value,
                value => _settings.ModernTheme.DynamicFriendGameSummariesSortDirectionLabel = value);

            ApplyFriendListStateBindings(
                _runtimeState.FriendAchievements,
                DynamicThemeViewKeys.UnlockTime,
                value => _settings.ModernTheme.DynamicFriendAchievementsFilterKey = value,
                value => _settings.ModernTheme.DynamicFriendAchievementsFilterLabel = value,
                value => _settings.ModernTheme.DynamicFriendAchievementsSortKey = value,
                value => _settings.ModernTheme.DynamicFriendAchievementsSortLabel = value,
                value => _settings.ModernTheme.DynamicFriendAchievementsSortDirectionKey = value,
                value => _settings.ModernTheme.DynamicFriendAchievementsSortDirectionLabel = value);

            _settings.ModernTheme.DynamicFriendScopeProviderOptions = CreateFriendProviderOptions(
                state,
                scope.ProviderKey,
                _settings.SetDynamicFriendScopeProviderCommand);
            _settings.ModernTheme.DynamicFriendScopeUserOptions = CreateFriendUserOptions(
                state,
                scope.ProviderKey,
                scope.UserKey,
                _settings.SetDynamicFriendScopeUserCommand);
            _settings.ModernTheme.DynamicFriendScopeGameOptions = CreateFriendGameOptions(
                state,
                scope.ProviderKey,
                scope.UserKey,
                scope.GameKey,
                _settings.SetDynamicFriendScopeGameCommand);
            _settings.ModernTheme.DynamicFriendSummariesFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.FriendSummaryFilterKeys,
                _runtimeState.FriendSummaries.FilterKey,
                _settings.SetDynamicFriendSummariesFilterCommand);
            _settings.ModernTheme.DynamicFriendSummariesSortOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.FriendSummarySortKeys,
                _runtimeState.FriendSummaries.SortKey,
                _settings.SortDynamicFriendSummariesCommand);
            _settings.ModernTheme.DynamicFriendSummariesSortDirectionOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.SortDirectionKeys,
                _runtimeState.FriendSummaries.SortDirectionKey,
                _settings.SetDynamicFriendSummariesSortDirectionCommand);
            _settings.ModernTheme.DynamicFriendSummaryLastUnlockFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.FriendSummaryFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(_runtimeState.FriendSummaries.FilterKey, DynamicThemeOptionGroups.FriendLastUnlockGroup, DynamicThemeOptionGroups.FriendSummaryFilterGroupMap),
                _settings.SetDynamicFriendSummariesLastUnlockFilterCommand);
            _settings.ModernTheme.DynamicFriendGameSummariesFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.GameSummaryFilterKeys,
                _runtimeState.FriendGameSummaries.FilterKey,
                _settings.SetDynamicFriendGameSummariesFilterCommand);
            _settings.ModernTheme.DynamicFriendGameSummariesSortOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.GameSummarySortKeys,
                _runtimeState.FriendGameSummaries.SortKey,
                _settings.SortDynamicFriendGameSummariesCommand);
            _settings.ModernTheme.DynamicFriendGameSummariesSortDirectionOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.SortDirectionKeys,
                _runtimeState.FriendGameSummaries.SortDirectionKey,
                _settings.SetDynamicFriendGameSummariesSortDirectionCommand);
            _settings.ModernTheme.DynamicFriendGameProgressFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.GameProgressFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(_runtimeState.FriendGameSummaries.FilterKey, DynamicThemeOptionGroups.GameProgressGroups, DynamicThemeOptionGroups.GameSummaryFilterGroupMap),
                _settings.SetDynamicFriendGameSummariesProgressFilterCommand);
            _settings.ModernTheme.DynamicFriendGameActivityFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.GameActivityFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(_runtimeState.FriendGameSummaries.FilterKey, DynamicThemeOptionGroups.GameActivityGroups, DynamicThemeOptionGroups.GameSummaryFilterGroupMap),
                _settings.SetDynamicFriendGameSummariesActivityFilterCommand);
            _settings.ModernTheme.DynamicFriendAchievementsFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementFilterKeys,
                _runtimeState.FriendAchievements.FilterKey,
                _settings.SetDynamicFriendAchievementsFilterCommand);
            _settings.ModernTheme.DynamicFriendAchievementsSortOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.LibraryAchievementSortKeys,
                _runtimeState.FriendAchievements.SortKey,
                _settings.SortDynamicFriendAchievementsCommand);
            _settings.ModernTheme.DynamicFriendAchievementsSortDirectionOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.SortDirectionKeys,
                _runtimeState.FriendAchievements.SortDirectionKey,
                _settings.SetDynamicFriendAchievementsSortDirectionCommand);
            _settings.ModernTheme.DynamicFriendAchievementStatusFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementStatusFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(_runtimeState.FriendAchievements.FilterKey, DynamicThemeOptionGroups.AchievementStatusGroup, DynamicThemeOptionGroups.AchievementFilterGroupMap),
                _settings.SetDynamicFriendAchievementsStatusFilterCommand);
            _settings.ModernTheme.DynamicFriendAchievementProgressFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementProgressFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(_runtimeState.FriendAchievements.FilterKey, DynamicThemeOptionGroups.AchievementProgressGroup, DynamicThemeOptionGroups.AchievementFilterGroupMap),
                _settings.SetDynamicFriendAchievementsProgressFilterCommand);
            _settings.ModernTheme.DynamicFriendAchievementRarityFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementRarityFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(_runtimeState.FriendAchievements.FilterKey, DynamicThemeOptionGroups.AchievementRarityGroup, DynamicThemeOptionGroups.AchievementFilterGroupMap),
                _settings.SetDynamicFriendAchievementsRarityFilterCommand);
            _settings.ModernTheme.DynamicFriendAchievementTrophyFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementTrophyFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(_runtimeState.FriendAchievements.FilterKey, DynamicThemeOptionGroups.AchievementTrophyGroup, DynamicThemeOptionGroups.AchievementFilterGroupMap),
                _settings.SetDynamicFriendAchievementsTrophyFilterCommand);
            _settings.ModernTheme.DynamicFriendAchievementCustomizationFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementCustomizationFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(_runtimeState.FriendAchievements.FilterKey, DynamicThemeOptionGroups.AchievementCustomizationGroups, DynamicThemeOptionGroups.AchievementFilterGroupMap),
                _settings.SetDynamicFriendAchievementsCustomizationFilterCommand);

            if (updateOptions)
            {
                ApplyDynamicOptionBindings();
            }
        }

        private static void ApplyFriendListStateBindings(
            DynamicThemeListViewState state,
            string defaultSortKey,
            Action<string> setFilterKey,
            Action<string> setFilterLabel,
            Action<string> setSortKey,
            Action<string> setSortLabel,
            Action<string> setDirectionKey,
            Action<string> setDirectionLabel)
        {
            setFilterKey(state.FilterKey);
            setFilterLabel(DynamicThemeLabels.GetLabel(state.FilterKey, DynamicThemeViewKeys.All));
            setSortKey(state.SortKey);
            setSortLabel(DynamicThemeLabels.GetLabel(state.SortKey, defaultSortKey));
            setDirectionKey(state.SortDirectionKey);
            setDirectionLabel(DynamicThemeLabels.GetLabel(state.SortDirectionKey, DynamicThemeViewKeys.Descending));
        }

        private static ObservableCollection<DynamicThemeOption> CreateFriendProviderOptions(
            FriendRuntimeState state,
            string selectedKey,
            ICommand applyCommand)
        {
            var providerKeys = (state?.Friends ?? Enumerable.Empty<FriendSummaryItem>())
                .SelectMany(GetFriendProviderScopeKeys)
                .Concat((state?.AggregateGames ?? Enumerable.Empty<FriendGameSummaryItem>()).Select(game => game?.ProviderKey));
            return DynamicThemeOptionFactory.CreateProviderOptions(providerKeys, selectedKey, applyCommand);
        }

        private static ObservableCollection<DynamicThemeOption> CreateFriendUserOptions(
            FriendRuntimeState state,
            string providerKey,
            string selectedKey,
            ICommand applyCommand)
        {
            var friends = (state?.Friends ?? Enumerable.Empty<FriendSummaryItem>())
                .Where(friend => FriendMatchesProvider(friend, providerKey))
                .GroupBy(FriendOverviewProjection.GetFriendScopeKey, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var friend = group.First();
                    return new DynamicThemeOption(
                        group.Key,
                        !string.IsNullOrWhiteSpace(friend.DisplayName) ? friend.DisplayName : group.Key,
                        group.Sum(item => Math.Max(0, item?.UnlockedAchievementsCount ?? 0)));
                })
                .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return CreateScopedOptions(friends, selectedKey, applyCommand);
        }

        private static ObservableCollection<DynamicThemeOption> CreateFriendGameOptions(
            FriendRuntimeState state,
            string providerKey,
            string userKey,
            string selectedKey,
            ICommand applyCommand)
        {
            var options = GetFriendGameScopeCandidates(state, providerKey, userKey)
                .GroupBy(FriendOverviewProjection.GetGameScopeKey, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var game = group.First();
                    return new DynamicThemeOption(
                        group.Key,
                        !string.IsNullOrWhiteSpace(game.GameName) ? game.GameName : group.Key,
                        group.Sum(item => Math.Max(0, item?.UnlockedAchievements ?? 0)));
                })
                .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return CreateScopedOptions(options, selectedKey, applyCommand);
        }

        private static IEnumerable<FriendGameSummaryItem> GetFriendGameScopeCandidates(
            FriendRuntimeState state,
            string providerKey,
            string userKey)
        {
            var projection = state?.Projection;
            var selectedFriend = projection?.FindFriend(userKey);
            IEnumerable<FriendGameSummaryItem> games = selectedFriend != null
                ? projection.GetSelectedFriendGames(selectedFriend)
                : state?.AggregateGames ?? Enumerable.Empty<FriendGameSummaryItem>();

            return games.Where(game => game != null && ProviderMatches(game.ProviderKey, providerKey));
        }

        private static bool FriendMatchesProvider(FriendSummaryItem friend, string scopeProviderKey)
        {
            if (friend == null)
            {
                return false;
            }

            return FriendOverviewProjection.IsAllScope(scopeProviderKey) ||
                   GetFriendProviderScopeKeys(friend)
                       .Any(provider => string.Equals(provider, scopeProviderKey, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> GetFriendProviderScopeKeys(FriendSummaryItem friend)
        {
            if (friend == null)
            {
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(friend.ProviderKey) &&
                !string.Equals(friend.ProviderKey, FriendOverviewProjection.MergedProviderKey, StringComparison.OrdinalIgnoreCase))
            {
                yield return friend.ProviderKey;
            }

            foreach (var provider in friend.MemberProviderKeys ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(provider))
                {
                    yield return provider;
                }
            }
        }

        private static ObservableCollection<DynamicThemeOption> CreateScopedOptions(
            IList<DynamicThemeOption> scopedOptions,
            string selectedKey,
            ICommand applyCommand)
        {
            scopedOptions = scopedOptions ?? Array.Empty<DynamicThemeOption>();
            var options = new List<DynamicThemeOption>
            {
                new DynamicThemeOption(
                    DynamicThemeViewKeys.All,
                    DynamicThemeLabels.GetLabel(DynamicThemeViewKeys.All, DynamicThemeViewKeys.All),
                    scopedOptions.Sum(option => option?.Count ?? 0),
                    IsThemeOptionSelected(DynamicThemeViewKeys.All, selectedKey),
                    applyCommand)
            };

            options.AddRange(scopedOptions
                .Where(option => option != null)
                .Select(option => new DynamicThemeOption(
                    option.Key,
                    option.Label,
                    option.Count,
                    IsThemeOptionSelected(option.Key, selectedKey),
                    applyCommand)));

            if (!FriendOverviewProjection.IsAllScope(selectedKey) &&
                options.All(option => !string.Equals(option.Key, selectedKey, StringComparison.OrdinalIgnoreCase)))
            {
                options.Add(new DynamicThemeOption(
                    selectedKey,
                    selectedKey,
                    isSelected: true,
                    applyCommand: applyCommand));
            }

            return new ObservableCollection<DynamicThemeOption>(options);
        }

        private static bool IsThemeOptionSelected(string key, string selectedKey)
        {
            return string.Equals(key ?? string.Empty, selectedKey ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetFriendScopeUserLabel(FriendRuntimeState state, string userKey)
        {
            if (FriendOverviewProjection.IsAllScope(userKey))
            {
                return DynamicThemeLabels.GetLabel(DynamicThemeViewKeys.All, DynamicThemeViewKeys.All);
            }

            var friend = state?.Projection?.FindFriend(userKey);
            return !string.IsNullOrWhiteSpace(friend?.DisplayName)
                ? friend.DisplayName
                : userKey ?? string.Empty;
        }

        private static string GetFriendScopeGameLabel(FriendRuntimeState state, string gameKey)
        {
            if (FriendOverviewProjection.IsAllScope(gameKey))
            {
                return DynamicThemeLabels.GetLabel(DynamicThemeViewKeys.All, DynamicThemeViewKeys.All);
            }

            var game = state?.Projection?.FindGame(gameKey);
            return !string.IsNullOrWhiteSpace(game?.GameName)
                ? game.GameName
                : gameKey ?? string.Empty;
        }

        private void ApplyDynamicOptionBindings()
        {
            var library = _runtimeState.Library ?? new LibraryRuntimeState();
            var selectedGameView = _runtimeState.SelectedGameAchievements;
            var selectedGameKey = selectedGameView.GameKey;
            if (string.IsNullOrWhiteSpace(selectedGameKey) && _settings.ModernTheme.SelectedGameId.HasValue)
            {
                selectedGameKey = _settings.ModernTheme.SelectedGameId.Value.ToString("D");
            }

            var selectedGameLabel = selectedGameView.GameLabel;
            if (string.IsNullOrWhiteSpace(selectedGameLabel) && Guid.TryParse(selectedGameKey, out var selectedGameId))
            {
                selectedGameLabel = ResolveGameLabel(selectedGameId);
            }

            _settings.ModernTheme.DynamicAchievementsGameKey = selectedGameKey ?? string.Empty;
            _settings.ModernTheme.DynamicAchievementsGameLabel = selectedGameLabel ?? string.Empty;
            _settings.ModernTheme.DynamicAchievementGameOptions = DynamicThemeOptionFactory.CreateGameOptions(
                library.AllGamesWithAchievements,
                selectedGameKey,
                _settings.SetDynamicAchievementsGameCommand);

            _settings.ModernTheme.DynamicAchievementStatusFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementStatusFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(selectedGameView.FilterKey, DynamicThemeOptionGroups.AchievementStatusGroup, DynamicThemeOptionGroups.AchievementFilterGroupMap),
                _settings.SetDynamicAchievementsStatusFilterCommand);
            _settings.ModernTheme.DynamicAchievementProgressFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementProgressFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(selectedGameView.FilterKey, DynamicThemeOptionGroups.AchievementProgressGroup, DynamicThemeOptionGroups.AchievementFilterGroupMap),
                _settings.SetDynamicAchievementsProgressFilterCommand);
            _settings.ModernTheme.DynamicAchievementRarityFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementRarityFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(selectedGameView.FilterKey, DynamicThemeOptionGroups.AchievementRarityGroup, DynamicThemeOptionGroups.AchievementFilterGroupMap),
                _settings.SetDynamicAchievementsRarityFilterCommand);
            _settings.ModernTheme.DynamicAchievementTrophyFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementTrophyFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(selectedGameView.FilterKey, DynamicThemeOptionGroups.AchievementTrophyGroup, DynamicThemeOptionGroups.AchievementFilterGroupMap),
                _settings.SetDynamicAchievementsTrophyFilterCommand);
            _settings.ModernTheme.DynamicAchievementCustomizationFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.AchievementCustomizationFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(selectedGameView.FilterKey, DynamicThemeOptionGroups.AchievementCustomizationGroups, DynamicThemeOptionGroups.AchievementFilterGroupMap),
                _settings.SetDynamicAchievementsCustomizationFilterCommand);
            _settings.ModernTheme.DynamicGameProgressFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.GameProgressFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(_runtimeState.GameSummaries.FilterKey, DynamicThemeOptionGroups.GameProgressGroups, DynamicThemeOptionGroups.GameSummaryFilterGroupMap),
                _settings.SetDynamicGameSummariesProgressFilterCommand);
            _settings.ModernTheme.DynamicGameActivityFilterOptions = DynamicThemeOptionFactory.CreateOptions(
                DynamicThemeOptionGroups.GameActivityFilterKeys,
                DynamicThemeOptionGroups.GetGroupSelection(_runtimeState.GameSummaries.FilterKey, DynamicThemeOptionGroups.GameActivityGroups, DynamicThemeOptionGroups.GameSummaryFilterGroupMap),
                _settings.SetDynamicGameSummariesActivityFilterCommand);
        }

        private List<AchievementDetail> BuildDynamicSelectedGameAchievements(
            SelectedGameRuntimeState state,
            SelectedGameAchievementViewState viewState)
        {
            if (state == null || !state.HasAchievements)
            {
                return EmptyAchievementList;
            }

            IEnumerable<AchievementDetail> source = SelectSelectedGameAchievementSource(state, viewState);
            source = DynamicThemeFilterEvaluator.ApplyAchievementFilters(source, viewState.FilterKey);
            return source.ToList();
        }

        private List<AchievementDetail> BuildDynamicLibraryAchievements(
            LibraryRuntimeState state,
            LibraryAchievementViewState viewState)
        {
            if (state == null || !state.HasData)
            {
                return EmptyAchievementList;
            }

            IEnumerable<AchievementDetail> source = SelectLibraryAchievementSource(state, viewState);
            source = ApplyProviderFilter(source, viewState.ProviderKey);
            source = ApplyGameFilter(source, viewState.GameKey, item => item.Game?.Id ?? Guid.Empty);
            source = DynamicThemeFilterEvaluator.ApplyAchievementFilters(source, viewState.FilterKey);
            return source.ToList();
        }

        private List<GameAchievementSummary> BuildDynamicGameSummaries(
            LibraryRuntimeState state,
            GameSummaryViewState viewState)
        {
            if (state == null || !state.HasData)
            {
                return new List<GameAchievementSummary>();
            }

            IEnumerable<GameAchievementSummary> source = state.AllGamesWithAchievements ?? Enumerable.Empty<GameAchievementSummary>();
            source = ApplyProviderFilter(source, viewState.ProviderKey);
            source = ApplyGameFilter(source, viewState.GameKey, item => item.GameId);
            source = DynamicThemeFilterEvaluator.ApplyGameSummaryFilters(source, viewState.FilterKey);
            return SortGameSummaries(source, viewState).ToList();
        }

        private List<FriendSummaryItem> BuildDynamicFriendSummaries(
            FriendRuntimeState state,
            FriendSummaryViewState viewState)
        {
            var projection = state?.Projection;
            if (projection == null || !state.HasData)
            {
                return new List<FriendSummaryItem>();
            }

            var scope = _runtimeState.FriendScope;
            var selectedGame = projection.FindGame(scope.GameKey);
            IEnumerable<FriendSummaryItem> source = state.Friends ?? Enumerable.Empty<FriendSummaryItem>();
            source = source.Where(friend => FriendMatchesProvider(friend, scope.ProviderKey));
            if (selectedGame != null)
            {
                source = source.Where(friend => projection.HasUnlocksForFriendGame(friend, selectedGame));
            }

            source = ApplyFriendSummaryFilter(source, viewState.FilterKey);
            return SortFriendSummaries(source, viewState).ToList();
        }

        private List<FriendGameSummaryItem> BuildDynamicFriendGameSummaries(
            FriendRuntimeState state,
            FriendGameSummaryViewState viewState)
        {
            var projection = state?.Projection;
            if (projection == null || !state.HasData)
            {
                return new List<FriendGameSummaryItem>();
            }

            var scope = _runtimeState.FriendScope;
            var selectedFriend = projection.FindFriend(scope.UserKey);
            var selectedGame = projection.FindGame(scope.GameKey);
            IEnumerable<FriendGameSummaryItem> source = selectedFriend != null
                ? projection.GetSelectedFriendGames(selectedFriend)
                : state.AggregateGames ?? Enumerable.Empty<FriendGameSummaryItem>();

            source = ApplyProviderFilter(source, scope.ProviderKey, item => item.ProviderKey);

            if (selectedGame != null)
            {
                source = source.Where(game => FriendOverviewProjection.IsSameGame(game, selectedGame));
            }

            source = ApplyFriendGameSummaryFilter(source, viewState.FilterKey);
            return SortFriendGameSummaries(source, viewState).ToList();
        }

        private List<FriendAchievementDisplayItem> BuildDynamicFriendAchievements(
            FriendRuntimeState state,
            FriendAchievementViewState viewState)
        {
            var projection = state?.Projection;
            if (projection == null || !state.HasData)
            {
                return new List<FriendAchievementDisplayItem>();
            }

            var scope = _runtimeState.FriendScope;
            var selectedFriend = projection.FindFriend(scope.UserKey);
            var selectedGame = projection.FindGame(scope.GameKey);
            var hasScopedSelection =
                !FriendOverviewProjection.IsAllScope(scope.ProviderKey) ||
                selectedFriend != null ||
                selectedGame != null;

            IEnumerable<FriendAchievementDisplayItem> source = hasScopedSelection
                ? state.AllAchievements ?? Enumerable.Empty<FriendAchievementDisplayItem>()
                : state.RecentUnlocks ?? Enumerable.Empty<FriendAchievementDisplayItem>();

            source = ApplyProviderFilter(source, scope.ProviderKey, item => item.ProviderKey);
            if (selectedFriend != null)
            {
                source = source.Where(achievement => FriendOverviewProjection.IsSameFriend(achievement, selectedFriend));
            }

            if (selectedGame != null)
            {
                source = source.Where(achievement => FriendOverviewProjection.IsSameGame(achievement, selectedGame));
            }

            source = ApplyFriendAchievementFilter(source, viewState.FilterKey);
            return SortFriendAchievements(source, viewState).ToList();
        }

        private static IEnumerable<FriendSummaryItem> ApplyFriendSummaryFilter(
            IEnumerable<FriendSummaryItem> source,
            string filterKey)
        {
            var items = source ?? Enumerable.Empty<FriendSummaryItem>();
            foreach (var key in DynamicThemeFilterExpression.Enumerate(filterKey))
            {
                switch (key)
                {
                    case DynamicThemeViewKeys.HasLastUnlock:
                        items = items.Where(item => item?.LastUnlockUtc.HasValue == true);
                        break;
                    case DynamicThemeViewKeys.NoLastUnlock:
                        items = items.Where(item => item != null && !item.LastUnlockUtc.HasValue);
                        break;
                }
            }

            return items;
        }

        private static IEnumerable<FriendGameSummaryItem> ApplyFriendGameSummaryFilter(
            IEnumerable<FriendGameSummaryItem> source,
            string filterKey)
        {
            var items = source ?? Enumerable.Empty<FriendGameSummaryItem>();
            foreach (var key in DynamicThemeFilterExpression.Enumerate(filterKey))
            {
                switch (key)
                {
                    case DynamicThemeViewKeys.Completed:
                        items = items.Where(item => item?.IsCompleted == true);
                        break;
                    case DynamicThemeViewKeys.Incomplete:
                        items = items.Where(item => item != null && !item.IsCompleted);
                        break;
                    case DynamicThemeViewKeys.Started:
                        items = items.Where(item => item != null && item.UnlockedAchievements > 0);
                        break;
                    case DynamicThemeViewKeys.NotStarted:
                        items = items.Where(item => item != null && item.UnlockedAchievements <= 0);
                        break;
                    case DynamicThemeViewKeys.Played:
                        items = items.Where(item => item?.LastPlayed.HasValue == true || item?.PlaytimeSeconds > 0);
                        break;
                    case DynamicThemeViewKeys.Unplayed:
                        items = items.Where(item => item != null && !item.LastPlayed.HasValue && item.PlaytimeSeconds == 0);
                        break;
                    case DynamicThemeViewKeys.HasLastUnlock:
                        items = items.Where(item => item?.LastUnlockUtc.HasValue == true);
                        break;
                    case DynamicThemeViewKeys.NoLastUnlock:
                        items = items.Where(item => item != null && !item.LastUnlockUtc.HasValue);
                        break;
                }
            }

            return items;
        }

        private static IEnumerable<FriendAchievementDisplayItem> ApplyFriendAchievementFilter(
            IEnumerable<FriendAchievementDisplayItem> source,
            string filterKey)
        {
            var items = source ?? Enumerable.Empty<FriendAchievementDisplayItem>();
            foreach (var key in DynamicThemeFilterExpression.Enumerate(filterKey))
            {
                switch (key)
                {
                    case DynamicThemeViewKeys.Unlocked:
                        items = items.Where(item => item?.Unlocked == true);
                        break;
                    case DynamicThemeViewKeys.Locked:
                        items = items.Where(item => item != null && !item.Unlocked);
                        break;
                    case DynamicThemeViewKeys.InProgress:
                        items = items.Where(item => item != null && !item.Unlocked && item.HasProgress);
                        break;
                    case DynamicThemeViewKeys.NoProgress:
                        items = items.Where(item => item != null && !item.HasProgress);
                        break;
                    case DynamicThemeViewKeys.HasNotes:
                        items = items.Where(item => !string.IsNullOrWhiteSpace(item?.AchievementNote));
                        break;
                    case DynamicThemeViewKeys.NoNotes:
                        items = items.Where(item => string.IsNullOrWhiteSpace(item?.AchievementNote));
                        break;
                    case DynamicThemeViewKeys.Capstone:
                        items = items.Where(item => item?.IsCapstone == true);
                        break;
                    case DynamicThemeViewKeys.Common:
                    case DynamicThemeViewKeys.Uncommon:
                    case DynamicThemeViewKeys.Rare:
                    case DynamicThemeViewKeys.UltraRare:
                        items = items.Where(item => item != null && string.Equals(item.Rarity.ToString(), key, StringComparison.OrdinalIgnoreCase));
                        break;
                    case DynamicThemeViewKeys.Platinum:
                    case DynamicThemeViewKeys.Gold:
                    case DynamicThemeViewKeys.Silver:
                    case DynamicThemeViewKeys.Bronze:
                        items = items.Where(item => string.Equals(item?.TrophyType, key, StringComparison.OrdinalIgnoreCase));
                        break;
                }
            }

            return items;
        }

        private static IEnumerable<FriendSummaryItem> SortFriendSummaries(
            IEnumerable<FriendSummaryItem> source,
            FriendSummaryViewState viewState)
        {
            source ??= Enumerable.Empty<FriendSummaryItem>();
            var descending = !string.Equals(viewState?.SortDirectionKey, DynamicThemeViewKeys.Ascending, StringComparison.Ordinal);
            var comparer = StringComparer.CurrentCultureIgnoreCase;

            switch (viewState?.SortKey)
            {
                case DynamicThemeViewKeys.Name:
                    return descending
                        ? source.OrderByDescending(item => item?.DisplayName ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.DisplayName ?? string.Empty, comparer);
                case DynamicThemeViewKeys.Provider:
                    return descending
                        ? source.OrderByDescending(item => item?.ProviderDisplayName ?? item?.ProviderKey ?? string.Empty, comparer)
                            .ThenBy(item => item?.DisplayName ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.ProviderDisplayName ?? item?.ProviderKey ?? string.Empty, comparer)
                            .ThenBy(item => item?.DisplayName ?? string.Empty, comparer);
                case DynamicThemeViewKeys.UnlockedCount:
                    return descending
                        ? source.OrderByDescending(item => item?.UnlockedAchievementsCount ?? 0)
                            .ThenBy(item => item?.DisplayName ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.UnlockedAchievementsCount ?? 0)
                            .ThenBy(item => item?.DisplayName ?? string.Empty, comparer);
                case DynamicThemeViewKeys.SharedGamesCount:
                case DynamicThemeViewKeys.AchievementCount:
                    return descending
                        ? source.OrderByDescending(item => item?.SharedGamesCount ?? 0)
                            .ThenBy(item => item?.DisplayName ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.SharedGamesCount ?? 0)
                            .ThenBy(item => item?.DisplayName ?? string.Empty, comparer);
                default:
                    return descending
                        ? source.OrderByDescending(item => item?.LastUnlockUtc ?? DateTime.MinValue)
                            .ThenBy(item => item?.DisplayName ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.LastUnlockUtc ?? DateTime.MinValue)
                            .ThenBy(item => item?.DisplayName ?? string.Empty, comparer);
            }
        }

        private static IEnumerable<FriendGameSummaryItem> SortFriendGameSummaries(
            IEnumerable<FriendGameSummaryItem> source,
            FriendGameSummaryViewState viewState)
        {
            source ??= Enumerable.Empty<FriendGameSummaryItem>();
            var descending = !string.Equals(viewState?.SortDirectionKey, DynamicThemeViewKeys.Ascending, StringComparison.Ordinal);
            var comparer = StringComparer.CurrentCultureIgnoreCase;

            switch (viewState?.SortKey)
            {
                case DynamicThemeViewKeys.Name:
                    return descending
                        ? source.OrderByDescending(item => item?.GameName ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.GameName ?? string.Empty, comparer);
                case DynamicThemeViewKeys.Provider:
                    return descending
                        ? source.OrderByDescending(item => item?.Provider ?? item?.ProviderKey ?? string.Empty, comparer)
                            .ThenBy(item => item?.GameName ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.Provider ?? item?.ProviderKey ?? string.Empty, comparer)
                            .ThenBy(item => item?.GameName ?? string.Empty, comparer);
                case DynamicThemeViewKeys.Progress:
                    return descending
                        ? source.OrderByDescending(item => item?.Progression ?? 0)
                            .ThenBy(item => item?.GameName ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.Progression ?? 0)
                            .ThenBy(item => item?.GameName ?? string.Empty, comparer);
                case DynamicThemeViewKeys.LastPlayed:
                    return descending
                        ? source.OrderByDescending(item => item?.LastPlayed ?? DateTime.MinValue)
                            .ThenByDescending(item => item?.LastUnlockUtc ?? DateTime.MinValue)
                        : source.OrderBy(item => item?.LastPlayed ?? DateTime.MinValue)
                            .ThenByDescending(item => item?.LastUnlockUtc ?? DateTime.MinValue);
                case DynamicThemeViewKeys.UnlockedCount:
                    return descending
                        ? source.OrderByDescending(item => item?.UnlockedAchievements ?? 0)
                            .ThenBy(item => item?.GameName ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.UnlockedAchievements ?? 0)
                            .ThenBy(item => item?.GameName ?? string.Empty, comparer);
                case DynamicThemeViewKeys.AchievementCount:
                    return descending
                        ? source.OrderByDescending(item => item?.TotalAchievements ?? 0)
                            .ThenBy(item => item?.GameName ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.TotalAchievements ?? 0)
                            .ThenBy(item => item?.GameName ?? string.Empty, comparer);
                default:
                    return descending
                        ? source.OrderByDescending(item => item?.LastUnlockUtc ?? DateTime.MinValue)
                            .ThenBy(item => item?.GameName ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.LastUnlockUtc ?? DateTime.MinValue)
                            .ThenBy(item => item?.GameName ?? string.Empty, comparer);
            }
        }

        private static IEnumerable<FriendAchievementDisplayItem> SortFriendAchievements(
            IEnumerable<FriendAchievementDisplayItem> source,
            FriendAchievementViewState viewState)
        {
            source ??= Enumerable.Empty<FriendAchievementDisplayItem>();
            var descending = !string.Equals(viewState?.SortDirectionKey, DynamicThemeViewKeys.Ascending, StringComparison.Ordinal);
            var comparer = StringComparer.CurrentCultureIgnoreCase;

            Func<FriendAchievementDisplayItem, object> primary;
            switch (viewState?.SortKey)
            {
                case DynamicThemeViewKeys.Name:
                    primary = item => item?.DisplayName ?? string.Empty;
                    break;
                case DynamicThemeViewKeys.Game:
                    primary = item => item?.GameName ?? string.Empty;
                    break;
                case DynamicThemeViewKeys.Provider:
                    primary = item => item?.ProviderKey ?? string.Empty;
                    break;
                case DynamicThemeViewKeys.Rarity:
                    primary = item => item?.RaritySortValue ?? double.MaxValue;
                    break;
                case DynamicThemeViewKeys.Status:
                    primary = item => item?.Unlocked == true ? 1 : 0;
                    break;
                case DynamicThemeViewKeys.Progress:
                    primary = item => item?.ProgressPercent ?? 0d;
                    break;
                case DynamicThemeViewKeys.Points:
                    primary = item => item?.Points ?? 0;
                    break;
                case DynamicThemeViewKeys.CollectionScore:
                    primary = item => item?.CollectionScore ?? 0;
                    break;
                case DynamicThemeViewKeys.PrestigeScore:
                    primary = item => item?.PrestigeScore ?? 0;
                    break;
                case DynamicThemeViewKeys.TrophyType:
                    primary = item => item?.TrophyType ?? string.Empty;
                    break;
                case DynamicThemeViewKeys.CategoryType:
                    primary = item => item?.CategoryType ?? string.Empty;
                    break;
                case DynamicThemeViewKeys.CategoryLabel:
                    primary = item => item?.CategoryLabel ?? string.Empty;
                    break;
                case DynamicThemeViewKeys.Notes:
                    primary = item => item?.AchievementNote ?? string.Empty;
                    break;
                default:
                    primary = item => item?.UnlockTimeUtc ?? DateTime.MinValue;
                    break;
            }

            return descending
                ? source.OrderByDescending(primary)
                    .ThenBy(item => item?.FriendName ?? string.Empty, comparer)
                    .ThenBy(item => item?.GameName ?? string.Empty, comparer)
                    .ThenBy(item => item?.DisplayName ?? string.Empty, comparer)
                : source.OrderBy(primary)
                    .ThenBy(item => item?.FriendName ?? string.Empty, comparer)
                    .ThenBy(item => item?.GameName ?? string.Empty, comparer)
                    .ThenBy(item => item?.DisplayName ?? string.Empty, comparer);
        }

        private static IEnumerable<AchievementDetail> SelectSelectedGameAchievementSource(
            SelectedGameRuntimeState state,
            SelectedGameAchievementViewState viewState)
        {
            return AchievementSortHelper.ResolveSelectedGameAchievements(
                state,
                viewState?.SortKey,
                viewState?.SortDirectionKey);
        }

        private static IEnumerable<AchievementDetail> SelectLibraryAchievementSource(
            LibraryRuntimeState state,
            LibraryAchievementViewState viewState)
        {
            return AchievementSortHelper.ResolveLibraryAchievements(
                state,
                viewState?.SortKey,
                viewState?.SortDirectionKey);
        }

        private static IEnumerable<TItem> ApplyGameFilter<TItem>(
            IEnumerable<TItem> source,
            string gameKey,
            Func<TItem, Guid> gameIdSelector)
            where TItem : class
        {
            var items = source ?? Enumerable.Empty<TItem>();
            if (IsAllScopeKey(gameKey) || !Guid.TryParse(gameKey, out var gameId) || gameId == Guid.Empty)
            {
                return items;
            }

            return items.Where(item => item != null && gameIdSelector(item) == gameId);
        }

        private static IEnumerable<AchievementDetail> ApplyProviderFilter(
            IEnumerable<AchievementDetail> source,
            string providerKey)
        {
            return ApplyProviderFilter(source, providerKey, item => item.ProviderKey);
        }

        private static IEnumerable<GameAchievementSummary> ApplyProviderFilter(
            IEnumerable<GameAchievementSummary> source,
            string providerKey)
        {
            return ApplyProviderFilter(source, providerKey, item => item.ProviderKey);
        }

        private static IEnumerable<TItem> ApplyProviderFilter<TItem>(
            IEnumerable<TItem> source,
            string providerKey,
            Func<TItem, string> providerKeySelector)
            where TItem : class
        {
            var items = source ?? Enumerable.Empty<TItem>();
            if (string.IsNullOrWhiteSpace(providerKey) ||
                string.Equals(providerKey, DynamicThemeViewKeys.All, StringComparison.OrdinalIgnoreCase))
            {
                return items;
            }

            return items.Where(item =>
                item != null &&
                string.Equals(providerKeySelector(item), providerKey, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsAllScopeKey(string key)
        {
            return string.IsNullOrWhiteSpace(key) ||
                   string.Equals(key, DynamicThemeViewKeys.All, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ProviderScopeMatches(string scopeProviderKey, string providerKey)
        {
            return IsAllScopeKey(scopeProviderKey) ||
                   (!string.IsNullOrWhiteSpace(providerKey) &&
                    string.Equals(scopeProviderKey, providerKey, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveLibraryProviderKeyForGame(LibraryRuntimeState state, Guid gameId)
        {
            if (gameId == Guid.Empty)
            {
                return null;
            }

            var providerKey = (state?.AllAchievements ?? Enumerable.Empty<AchievementDetail>())
                .Where(item => item?.Game?.Id == gameId)
                .Select(item => item?.ProviderKey)
                .FirstOrDefault(key => !string.IsNullOrWhiteSpace(key));
            if (!string.IsNullOrWhiteSpace(providerKey))
            {
                return providerKey;
            }

            return ResolveGameSummaryProviderKeyForGame(state, gameId);
        }

        private static string ResolveGameSummaryProviderKeyForGame(LibraryRuntimeState state, Guid gameId)
        {
            if (gameId == Guid.Empty)
            {
                return null;
            }

            return (state?.AllGamesWithAchievements ?? Enumerable.Empty<GameAchievementSummary>())
                .Where(item => item != null && item.GameId == gameId)
                .Select(item => item?.ProviderKey)
                .FirstOrDefault(key => !string.IsNullOrWhiteSpace(key));
        }

        private FriendGameSummaryItem FindFriendGameScopeCandidate(FriendRuntimeState state, Guid gameId)
        {
            if (gameId == Guid.Empty)
            {
                return null;
            }

            var scope = _runtimeState.FriendScope;
            var candidates = (state?.AggregateGames ?? Enumerable.Empty<FriendGameSummaryItem>())
                .Where(game => game?.PlayniteGameId == gameId)
                .ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            if (!FriendOverviewProjection.IsAllScope(scope.ProviderKey))
            {
                var scopedCandidate = candidates.FirstOrDefault(game =>
                    string.Equals(game.ProviderKey, scope.ProviderKey, StringComparison.OrdinalIgnoreCase));
                if (scopedCandidate != null)
                {
                    return scopedCandidate;
                }
            }

            return candidates
                .OrderByDescending(game => game?.FriendUnlockedAchievementsCount ?? 0)
                .ThenBy(game => game?.GameName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault();
        }

        private static IEnumerable<GameAchievementSummary> SortGameSummaries(
            IEnumerable<GameAchievementSummary> source,
            GameSummaryViewState viewState)
        {
            source ??= Enumerable.Empty<GameAchievementSummary>();
            var descending = !string.Equals(viewState?.SortDirectionKey, DynamicThemeViewKeys.Ascending, StringComparison.Ordinal);
            var comparer = StringComparer.CurrentCultureIgnoreCase;

            switch (viewState?.SortKey)
            {
                case DynamicThemeViewKeys.Name:
                    return descending
                        ? source.OrderByDescending(item => item?.Name ?? string.Empty, comparer)
                            .ThenByDescending(item => item?.LastUnlockDate ?? DateTime.MinValue)
                        : source.OrderBy(item => item?.Name ?? string.Empty, comparer)
                            .ThenByDescending(item => item?.LastUnlockDate ?? DateTime.MinValue);
                case DynamicThemeViewKeys.Provider:
                    return descending
                        ? source.OrderByDescending(item => item?.ProviderName ?? item?.ProviderKey ?? string.Empty, comparer)
                            .ThenBy(item => item?.Name ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.ProviderName ?? item?.ProviderKey ?? string.Empty, comparer)
                            .ThenBy(item => item?.Name ?? string.Empty, comparer);
                case DynamicThemeViewKeys.Progress:
                    return descending
                        ? source.OrderByDescending(item => item?.Progress ?? 0)
                            .ThenByDescending(item => item?.LastUnlockDate ?? DateTime.MinValue)
                            .ThenBy(item => item?.Name ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.Progress ?? 0)
                            .ThenByDescending(item => item?.LastUnlockDate ?? DateTime.MinValue)
                            .ThenBy(item => item?.Name ?? string.Empty, comparer);
                case DynamicThemeViewKeys.LastPlayed:
                    return descending
                        ? source.OrderByDescending(item => item?.LastPlayed ?? DateTime.MinValue)
                            .ThenByDescending(item => item?.LastUnlockDate ?? DateTime.MinValue)
                            .ThenBy(item => item?.Name ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.LastPlayed ?? DateTime.MinValue)
                            .ThenByDescending(item => item?.LastUnlockDate ?? DateTime.MinValue)
                            .ThenBy(item => item?.Name ?? string.Empty, comparer);
                case DynamicThemeViewKeys.UnlockedCount:
                    return descending
                        ? source.OrderByDescending(item => item?.UnlockedCount ?? 0)
                            .ThenByDescending(item => item?.Progress ?? 0)
                            .ThenByDescending(item => item?.LastUnlockDate ?? DateTime.MinValue)
                            .ThenBy(item => item?.Name ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.UnlockedCount ?? 0)
                            .ThenByDescending(item => item?.Progress ?? 0)
                            .ThenByDescending(item => item?.LastUnlockDate ?? DateTime.MinValue)
                            .ThenBy(item => item?.Name ?? string.Empty, comparer);
                case DynamicThemeViewKeys.AchievementCount:
                    return descending
                        ? source.OrderByDescending(item => item?.AchievementCount ?? 0)
                            .ThenByDescending(item => item?.Progress ?? 0)
                            .ThenByDescending(item => item?.LastUnlockDate ?? DateTime.MinValue)
                            .ThenBy(item => item?.Name ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.AchievementCount ?? 0)
                            .ThenByDescending(item => item?.Progress ?? 0)
                            .ThenByDescending(item => item?.LastUnlockDate ?? DateTime.MinValue)
                            .ThenBy(item => item?.Name ?? string.Empty, comparer);
                default:
                    return descending
                        ? source.OrderByDescending(item => item?.LastUnlockDate ?? DateTime.MinValue)
                            .ThenByDescending(item => item?.Progress ?? 0)
                            .ThenBy(item => item?.Name ?? string.Empty, comparer)
                        : source.OrderBy(item => item?.LastUnlockDate ?? DateTime.MinValue)
                            .ThenByDescending(item => item?.Progress ?? 0)
                            .ThenBy(item => item?.Name ?? string.Empty, comparer);
            }
        }

        private static IEnumerable<string> EnumerateLibraryAchievementProviderKeys(LibraryRuntimeState state)
        {
            var achievements = GetAvailableLibraryAchievements(state);
            if (achievements.Count > 0)
            {
                return achievements.Select(item => item?.ProviderKey);
            }

            return (state?.AllGamesWithAchievements ?? Enumerable.Empty<GameAchievementSummary>())
                .Select(item => item?.ProviderKey);
        }

        private static List<AchievementDetail> GetAvailableLibraryAchievements(LibraryRuntimeState state)
        {
            if (state == null)
            {
                return new List<AchievementDetail>();
            }

            return state.AllAchievements ?? new List<AchievementDetail>();
        }

        private bool TryNormalizeProviderKey(object parameter, out string providerKey)
        {
            var raw = ExtractProviderKeyParameter(parameter);
            if (string.IsNullOrWhiteSpace(raw))
            {
                providerKey = null;
                return false;
            }

            if (string.Equals(raw, DynamicThemeViewKeys.All, StringComparison.OrdinalIgnoreCase))
            {
                providerKey = DynamicThemeViewKeys.All;
                return true;
            }

            providerKey = FindCanonicalProviderKey(raw) ?? raw;
            return true;
        }

        private IEnumerable<string> EnumerateKnownProviderKeys()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            IEnumerable<string> YieldKnown(IEnumerable<string> keys)
            {
                foreach (var key in keys ?? Enumerable.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(key) && seen.Add(key))
                    {
                        yield return key;
                    }
                }
            }

            foreach (var key in YieldKnown(
                (_runtimeState?.SelectedGame?.AllAchievements ?? Enumerable.Empty<AchievementDetail>())
                .Select(item => item?.ProviderKey)))
            {
                yield return key;
            }

            foreach (var key in YieldKnown(
                (_runtimeState?.Library?.AllAchievements ?? Enumerable.Empty<AchievementDetail>())
                .Select(item => item?.ProviderKey)))
            {
                yield return key;
            }

            foreach (var key in YieldKnown(
                (_runtimeState?.Library?.AllGamesWithAchievements ?? Enumerable.Empty<GameAchievementSummary>())
                .Select(item => item?.ProviderKey)))
            {
                yield return key;
            }
        }

        private void LogInvalidCommandParameter(string commandName, object parameter)
        {
            _logger?.Debug($"Ignored invalid parameter for {commandName}: '{parameter ?? "null"}'.");
        }

        private void NotifySettingProperties(params string[] propertyNames)
        {
            if (propertyNames == null || propertyNames.Length == 0)
            {
                return;
            }

            for (int i = 0; i < propertyNames.Length; i++)
            {
                var name = propertyNames[i];
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _settings.OnPropertyChanged(name);
                }
            }
        }
    }
}







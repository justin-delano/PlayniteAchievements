using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Friends;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using AsyncCommand = PlayniteAchievements.Common.AsyncCommand;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels
{
    internal sealed class FriendsOverviewViewModel : ObservableObject, IDisposable, IOverviewRefreshHeaderViewModel
    {
        private readonly IFriendCacheManager _friendCache;
        private readonly RefreshEntryPoint _refreshCoordinator;
        private readonly RefreshRuntime _refreshRuntime;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;
        private readonly IPlayniteAPI _playniteApi;
        private readonly Func<Guid?, string, FriendCustomRefreshOptions> _showCustomRefreshDialog;
        private readonly SearchTextIndex<FriendSummaryItem> _friendSearchIndex =
            new SearchTextIndex<FriendSummaryItem>(item => SearchTextBuilder.FromValues(item?.DisplayName, item?.ProviderKey));
        private readonly SearchTextIndex<FriendGameSummaryItem> _gameSearchIndex =
            new SearchTextIndex<FriendGameSummaryItem>(item => SearchTextBuilder.FromValues(item?.GameName, item?.ProviderKey));
        private readonly SearchTextIndex<FriendAchievementDisplayItem> _achievementSearchIndex =
            new SearchTextIndex<FriendAchievementDisplayItem>(item => SearchTextBuilder.FromValues(
                item?.FriendName,
                item?.GameName,
                item?.DisplayName,
                item?.Description,
                item?.CategoryType,
                item?.CategoryLabel,
                item?.ProviderKey));

        private List<FriendSummaryItem> _allFriends = new List<FriendSummaryItem>();
        private List<FriendGameSummaryItem> _allGames = new List<FriendGameSummaryItem>();
        private List<FriendAchievementDisplayItem> _allRecentUnlocks = new List<FriendAchievementDisplayItem>();
        private List<FriendAchievementDisplayItem> _allUnlockedAchievements = new List<FriendAchievementDisplayItem>();
        private FriendOverviewProjection _projection = new FriendOverviewProjection(null);
        private readonly HashSet<string> _selectedTypeFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _selectedCategoryFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private FriendSummaryItem _selectedFriend;
        private FriendGameSummaryItem _selectedGame;
        private bool _isApplyingFilters;
        private bool _isRefreshing;
        private bool _disposed;
        private int _loadVersion;
        private string _statusText;
        private string _friendSearchText;
        private string _gameSearchText;
        private string _achievementSearchText;
        private string _selectedProviderKey;
        private string _selectedRefreshMode = RefreshModeType.FriendsRecent.GetKey();
        private double _progressPercent;
        private string _progressMessage;

        public FriendsOverviewViewModel(
            IFriendCacheManager friendCache,
            RefreshEntryPoint refreshCoordinator,
            RefreshRuntime refreshRuntime,
            PlayniteAchievementsSettings settings,
            ILogger logger,
            IPlayniteAPI playniteApi = null,
            Func<Guid?, string, FriendCustomRefreshOptions> showCustomRefreshDialog = null)
        {
            _friendCache = friendCache;
            _refreshCoordinator = refreshCoordinator;
            _refreshRuntime = refreshRuntime;
            _settings = settings;
            _logger = logger;
            _playniteApi = playniteApi;
            _showCustomRefreshDialog = showCustomRefreshDialog;

            FilteredFriends = new BulkObservableCollection<FriendSummaryItem>();
            FilteredGames = new BulkObservableCollection<FriendGameSummaryItem>();
            DisplayedAchievements = new BulkObservableCollection<FriendAchievementDisplayItem>();
            ProviderFilterOptions = new ObservableCollection<string>();
            TypeFilterOptions = new ObservableCollection<string>();
            CategoryFilterOptions = new ObservableCollection<string>();
            FriendRefreshModes = new ObservableCollection<RefreshMode>(CreateFriendRefreshModes());

            RefreshCommand = new AsyncCommand(async _ => await RefreshSelectedModeAsync().ConfigureAwait(true), _ => CanRefresh());
            RefreshRecentCommand = new AsyncCommand(async _ => await RefreshFriendsAsync(RefreshModeType.FriendsRecent).ConfigureAwait(true), _ => CanRefresh());
            RefreshFullCommand = new AsyncCommand(async _ => await RefreshFriendsAsync(RefreshModeType.FriendsFull).ConfigureAwait(true), _ => CanRefresh());
            RefreshSharedCommand = new AsyncCommand(async _ => await RefreshFriendsAsync(RefreshModeType.FriendsShared).ConfigureAwait(true), _ => CanRefresh());
            RefreshInstalledCommand = new AsyncCommand(async _ => await RefreshFriendsAsync(RefreshModeType.FriendsInstalled).ConfigureAwait(true), _ => CanRefresh());
            RefreshFriendSelectedGameCommand = new AsyncCommand(ExecuteSelectedFriendTargetRefreshAsync, parameter => CanRefreshSelectedFriendTarget(parameter));
            OpenGameInLibraryCommand = new RelayCommand(OpenGameInLibrary);
            RefreshOrCancelCommand = new RelayCommand(ExecuteRefreshOrCancel, _ => CanExecuteRefreshOrCancel());
            ClearSelectionCommand = new RelayCommand(_ => ClearSelection());
            ClearFriendSelectionCommand = new RelayCommand(_ => ClearFriendSelection());
            ClearGameSelectionCommand = new RelayCommand(_ => ClearGameSelection());

            if (_refreshRuntime != null)
            {
                _refreshRuntime.RebuildProgress += OnRebuildProgress;
            }

            if (_settings?.Persisted != null)
            {
                _settings.Persisted.PropertyChanged += OnPersistedSettingsChanged;
            }
        }

        public BulkObservableCollection<FriendSummaryItem> FilteredFriends { get; }
        public BulkObservableCollection<FriendGameSummaryItem> FilteredGames { get; }
        public BulkObservableCollection<FriendAchievementDisplayItem> DisplayedAchievements { get; }

        public BulkObservableCollection<FriendSummaryItem> Friends => FilteredFriends;
        public BulkObservableCollection<FriendGameSummaryItem> Games => FilteredGames;
        public BulkObservableCollection<FriendAchievementDisplayItem> RecentUnlocks => DisplayedAchievements;

        public PlayniteAchievementsSettings Settings => _settings;

        public ObservableCollection<string> ProviderFilterOptions { get; }
        public ObservableCollection<string> TypeFilterOptions { get; }
        public ObservableCollection<string> CategoryFilterOptions { get; }
        public ObservableCollection<RefreshMode> FriendRefreshModes { get; }
        public ObservableCollection<RefreshMode> RefreshModes => FriendRefreshModes;

        public ICommand RefreshCommand { get; }
        public ICommand RefreshRecentCommand { get; }
        public ICommand RefreshFullCommand { get; }
        public ICommand RefreshSharedCommand { get; }
        public ICommand RefreshInstalledCommand { get; }
        public ICommand RefreshFriendSelectedGameCommand { get; }
        public ICommand OpenGameInLibraryCommand { get; }
        public ICommand RefreshOrCancelCommand { get; }
        public ICommand ClearSelectionCommand { get; }
        public ICommand ClearFriendSelectionCommand { get; }
        public ICommand ClearGameSelectionCommand { get; }

        public FriendSummaryItem SelectedFriend
        {
            get => _selectedFriend;
            set
            {
                if (SetValueAndReturn(ref _selectedFriend, value))
                {
                    NotifySelectionStateChanged();
                    if (!_isApplyingFilters)
                    {
                        ApplyFilters();
                    }
                }
            }
        }

        public FriendGameSummaryItem SelectedGame
        {
            get => _selectedGame;
            set
            {
                if (SetValueAndReturn(ref _selectedGame, value))
                {
                    NotifySelectionStateChanged();
                    if (!_isApplyingFilters)
                    {
                        ApplyFilters();
                    }
                }
            }
        }

        public bool HasFriendSelection => SelectedFriend != null;
        public bool HasGameSelection => SelectedGame != null;
        public bool HasAnySelection => SelectedFriend != null || SelectedGame != null;
        public bool HasFriendGameSelection => SelectedFriend != null && SelectedGame != null;

        public string FriendSearchText
        {
            get => _friendSearchText;
            set
            {
                if (SetValueAndReturn(ref _friendSearchText, value))
                {
                    ApplyFilters();
                }
            }
        }

        public string GameSearchText
        {
            get => _gameSearchText;
            set
            {
                if (SetValueAndReturn(ref _gameSearchText, value))
                {
                    ApplyFilters();
                }
            }
        }

        public string AchievementSearchText
        {
            get => _achievementSearchText;
            set
            {
                if (SetValueAndReturn(ref _achievementSearchText, value))
                {
                    ApplyFilters();
                }
            }
        }

        public string SelectedProviderKey
        {
            get => _selectedProviderKey;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                if (SetValueAndReturn(ref _selectedProviderKey, normalized))
                {
                    OnPropertyChanged(nameof(SelectedProviderFilterText));
                    ApplyFilters();
                }
            }
        }

        public string SelectedRefreshMode
        {
            get => _selectedRefreshMode;
            set
            {
                if (SetValueAndReturn(ref _selectedRefreshMode, value))
                {
                    OnPropertyChanged(nameof(RefreshModeSelectionText));
                    OnPropertyChanged(nameof(RefreshActionButtonText));
                    OnPropertyChanged(nameof(RefreshOrCancelButtonText));
                }
            }
        }

        public string RefreshModeSelectionText => FriendRefreshModes?
            .FirstOrDefault(mode => string.Equals(mode?.Key, SelectedRefreshMode, StringComparison.Ordinal))?
            .ShortDisplayName
            ?? ResourceProvider.GetString("LOCPlayAch_Button_Refresh")
            ?? "Refresh";

        public string RefreshActionButtonText => string.Equals(
            SelectedRefreshMode,
            RefreshModeType.FriendsCustom.GetKey(),
            StringComparison.Ordinal)
            ? ResourceProvider.GetString("LOCPlayAch_Button_Configure") ?? "Configure"
            : ResourceProvider.GetString("LOCPlayAch_Button_Refresh") ?? "Refresh";

        public string RefreshOrCancelButtonText => IsRefreshing
            ? ResourceProvider.GetString("LOCPlayAch_Button_Cancel") ?? "Cancel"
            : RefreshActionButtonText;

        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set
            {
                if (SetValueAndReturn(ref _isRefreshing, value))
                {
                    RaiseRefreshCanExecuteChanged();
                    OnPropertyChanged(nameof(RefreshOrCancelButtonText));
                    OnPropertyChanged(nameof(ShowProgress));
                }
            }
        }

        public double ProgressPercent
        {
            get => _progressPercent;
            private set => SetValue(ref _progressPercent, value);
        }

        public string ProgressMessage
        {
            get => _progressMessage;
            private set
            {
                if (SetValueAndReturn(ref _progressMessage, value))
                {
                    OnPropertyChanged(nameof(ShowProgress));
                }
            }
        }

        public bool ShowProgress => IsRefreshing && !string.IsNullOrWhiteSpace(ProgressMessage);

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (SetValueAndReturn(ref _statusText, value))
                {
                    OnPropertyChanged(nameof(HasStatusText));
                }
            }
        }

        public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);
        public bool HasData => _allFriends.Count > 0 || _allGames.Count > 0 || _allRecentUnlocks.Count > 0 || _allUnlockedAchievements.Count > 0;
        public bool IsProviderDisabled => false;

        public string SelectedProviderFilterText => string.IsNullOrWhiteSpace(SelectedProviderKey)
            ? ResourceProvider.GetString("LOCPlayAch_FriendsOverview_AllProviders") ?? "All Providers"
            : SelectedProviderKey;

        public string SelectedTypeFilterText => GetSelectedFilterText(
            _selectedTypeFilters,
            TypeFilterOptions,
            ResourceProvider.GetString("LOCPlayAch_FriendsOverview_TypeFilter") ?? "Type");

        public string SelectedCategoryFilterText => GetSelectedFilterText(
            _selectedCategoryFilters,
            CategoryFilterOptions,
            ResourceProvider.GetString("LOCPlayAch_FriendsOverview_CategoryFilter") ?? "Category");

        public string AchievementSectionTitle
        {
            get
            {
                if (SelectedFriend != null && SelectedGame != null)
                {
                    return string.Format(
                        ResourceProvider.GetString("LOCPlayAch_FriendsOverview_SelectedFriendGameAchievements") ?? "{0} - {1}",
                        SelectedFriend.DisplayName,
                        SelectedGame.GameName);
                }

                if (SelectedFriend != null)
                {
                    return string.Format(
                        ResourceProvider.GetString("LOCPlayAch_FriendsOverview_SelectedFriendAchievements") ?? "{0} Achievements",
                        SelectedFriend.DisplayName);
                }

                if (SelectedGame != null)
                {
                    return string.Format(
                        ResourceProvider.GetString("LOCPlayAch_FriendsOverview_SelectedGameAchievements") ?? "{0} Achievements",
                        SelectedGame.GameName);
                }

                return ResourceProvider.GetString("LOCPlayAch_RecentAchievements") ?? "Recent Achievements";
            }
        }

        public string AchievementCountText => HasFriendGameSelection
            ? string.Format(
                ResourceProvider.GetString("LOCPlayAch_FriendsOverview_SelectedGameAchievementCount") ?? "({0}/{1} {2})",
                DisplayedAchievements.Count,
                Math.Max(0, SelectedGame?.TotalAchievements ?? 0),
                ResourceProvider.GetString("LOCPlayAch_Achievements") ?? "Achievements")
            : string.Empty;

        public Task LoadAsync()
        {
            return LoadFromCacheAsync();
        }

        public void ClearFriendSearch()
        {
            FriendSearchText = string.Empty;
        }

        public void ClearGameSearch()
        {
            GameSearchText = string.Empty;
        }

        public void ClearAchievementSearch()
        {
            AchievementSearchText = string.Empty;
        }

        public bool IsProviderFilterSelected(string providerKey)
        {
            return string.Equals(SelectedProviderKey, providerKey, StringComparison.OrdinalIgnoreCase);
        }

        public void SetProviderFilter(string providerKey)
        {
            SelectedProviderKey = providerKey;
        }

        public bool IsTypeFilterSelected(string value)
        {
            return IsFilterSelected(_selectedTypeFilters, value);
        }

        public void SetTypeFilterSelected(string value, bool isSelected)
        {
            if (!SetFilterSelection(_selectedTypeFilters, value, isSelected))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedTypeFilterText));
            ApplyFilters();
        }

        public bool IsCategoryFilterSelected(string value)
        {
            return IsFilterSelected(_selectedCategoryFilters, value);
        }

        public void SetCategoryFilterSelected(string value, bool isSelected)
        {
            if (!SetFilterSelection(_selectedCategoryFilters, value, isSelected))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedCategoryFilterText));
            ApplyFilters();
        }

        public void ToggleFriendSelection(FriendSummaryItem friend)
        {
            SelectedFriend = ReferenceEquals(SelectedFriend, friend) || IsSameFriend(SelectedFriend, friend)
                ? null
                : friend;
        }

        public void ToggleGameSelection(FriendGameSummaryItem game)
        {
            SelectedGame = ReferenceEquals(SelectedGame, game) || IsSameGame(SelectedGame, game)
                ? null
                : game;
        }

        public void ClearSelection()
        {
            var changed = false;
            if (_selectedFriend != null)
            {
                _selectedFriend = null;
                OnPropertyChanged(nameof(SelectedFriend));
                changed = true;
            }

            if (_selectedGame != null)
            {
                _selectedGame = null;
                OnPropertyChanged(nameof(SelectedGame));
                changed = true;
            }

            if (changed)
            {
                NotifySelectionStateChanged();
                ApplyFilters();
            }
        }

        public void ClearFriendSelection()
        {
            if (_selectedFriend == null)
            {
                return;
            }

            _selectedFriend = null;
            OnPropertyChanged(nameof(SelectedFriend));
            NotifySelectionStateChanged();
            ApplyFilters();
        }

        public void ClearGameSelection()
        {
            if (_selectedGame == null)
            {
                return;
            }

            _selectedGame = null;
            OnPropertyChanged(nameof(SelectedGame));
            NotifySelectionStateChanged();
            ApplyFilters();
        }

        private bool CanRefresh()
        {
            return !IsRefreshing;
        }

        public void CancelRefresh()
        {
            _refreshRuntime?.CancelCurrentRebuild();
        }

        private bool CanExecuteRefreshOrCancel()
        {
            return IsRefreshing || CanRefresh();
        }

        private void ExecuteRefreshOrCancel(object parameter)
        {
            if (IsRefreshing)
            {
                CancelRefresh();
                return;
            }

            if (CanRefresh())
            {
                _ = RefreshSelectedModeAsync();
            }
        }

        private async Task RefreshSelectedModeAsync()
        {
            var selected = FriendRefreshModes.FirstOrDefault(mode =>
                string.Equals(mode?.Key, SelectedRefreshMode, StringComparison.Ordinal));
            if (selected?.Type == RefreshModeType.FriendsSelectedGame)
            {
                await ExecuteSelectedFriendTargetRefreshAsync(null).ConfigureAwait(true);
                return;
            }

            if (selected?.Type == RefreshModeType.FriendsCustom)
            {
                if (_showCustomRefreshDialog == null)
                {
                    StatusText = ResourceProvider.GetString("LOCPlayAch_FriendsOverview_NotAvailable") ??
                                 "Friends Overview is not available.";
                    return;
                }

                var customOptions = _showCustomRefreshDialog(
                    SelectedGame?.PlayniteGameId,
                    SelectedGame?.GameName);
                if (customOptions == null)
                {
                    return;
                }

                await ExecuteFriendRefreshRequestAsync(
                    new RefreshRequest
                    {
                        Mode = RefreshModeType.FriendsCustom,
                        CustomFriendOptions = customOptions
                    },
                    "Custom friends refresh failed.").ConfigureAwait(true);
                return;
            }

            await RefreshFriendsAsync(selected?.Type ?? RefreshModeType.FriendsRecent).ConfigureAwait(true);
        }

        private async Task RefreshFriendsAsync(RefreshModeType mode)
        {
            await ExecuteFriendRefreshRequestAsync(
                new RefreshRequest { Mode = mode },
                "Friend refresh failed.").ConfigureAwait(true);
        }

        private async Task ExecuteFriendRefreshRequestAsync(RefreshRequest request, string errorLogMessage)
        {
            if (_refreshCoordinator == null)
            {
                StatusText = ResourceProvider.GetString("LOCPlayAch_FriendsOverview_NotAvailable") ??
                             "Friends Overview is not available.";
                return;
            }

            try
            {
                IsRefreshing = true;
                StatusText = null;
                ProgressPercent = 0;
                ProgressMessage = ResourceProvider.GetString("LOCPlayAch_FriendsOverview_Refreshing") ??
                                  "Refreshing friends...";
                await _refreshCoordinator.ExecuteAsync(
                    request,
                    new RefreshExecutionPolicy
                    {
                        ValidateAuthentication = true,
                        UseProgressWindow = false,
                        SwallowExceptions = false,
                        ErrorLogMessage = errorLogMessage ?? "Friend refresh failed."
                    }).ConfigureAwait(true);
                await LoadFromCacheAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to refresh friends overview.");
                StatusText = ResourceProvider.GetString("LOCPlayAch_FriendsOverview_RefreshFailed") ??
                             "Friend refresh failed.";
            }
            finally
            {
                IsRefreshing = false;
                ProgressMessage = null;
            }
        }

        private async Task ExecuteSelectedFriendTargetRefreshAsync(object parameter)
        {
            if (!CanRefresh())
            {
                return;
            }

            if (TryBuildSelectedFriendRefreshRequest(parameter, SelectedFriend, SelectedGame, out var request))
            {
                await ExecuteFriendRefreshRequestAsync(
                    request,
                    "Friend selected refresh failed.").ConfigureAwait(true);
                return;
            }

            StatusText = ResourceProvider.GetString("LOCPlayAch_FriendsOverview_SelectTargetForRefresh") ??
                         "Select a friend or game before refreshing selected friend data.";
        }

        private bool CanRefreshSelectedFriendTarget(object parameter)
        {
            return CanRefresh() &&
                   TryBuildSelectedFriendRefreshRequest(parameter, SelectedFriend, SelectedGame, out _);
        }

        internal static bool TryBuildSelectedFriendRefreshRequest(
            object parameter,
            FriendSummaryItem selectedFriend,
            FriendGameSummaryItem selectedGame,
            out RefreshRequest request)
        {
            request = null;

            var parameterIsFriend = TryGetFriendTarget(parameter, out var parameterFriend);
            var parameterIsGame = TryGetGameTarget(parameter, out var parameterGame);

            var friend = parameterIsFriend
                ? parameterFriend
                : selectedFriend;
            var game = parameterIsFriend
                ? null
                : (parameterIsGame ? parameterGame : selectedGame);

            if (friend != null && game == null)
            {
                request = new RefreshRequest
                {
                    Mode = RefreshModeType.FriendsCustom,
                    CustomFriendOptions = new FriendCustomRefreshOptions
                    {
                        ProviderKeys = new[] { friend.ProviderKey },
                        Scope = FriendRefreshScope.Full,
                        LibraryScope = FriendRefreshPolicy.GetDefaultLibraryScope(FriendRefreshScope.Full),
                        FriendExternalUserIds = new[] { friend.ExternalUserId }
                    }
                };
                return true;
            }

            if (friend != null && game != null)
            {
                return TryBuildSelectedFriendGameRefreshRequest(friend, game, out request);
            }

            if (game != null)
            {
                return TryBuildSelectedGameRefreshRequest(game, out request);
            }

            return false;
        }

        private static bool TryBuildSelectedFriendGameRefreshRequest(
            FriendSummaryItem friend,
            object game,
            out RefreshRequest request)
        {
            request = null;
            if (!TryGetFriendTarget(friend, out friend))
            {
                return false;
            }

            if (TryGetPlayniteGameId(game, out var gameId))
            {
                request = new RefreshRequest
                {
                    Mode = RefreshModeType.FriendsCustom,
                    CustomFriendOptions = new FriendCustomRefreshOptions
                    {
                        ProviderKeys = new[] { friend.ProviderKey },
                        Scope = FriendRefreshScope.SelectedGame,
                        LibraryScope = FriendRefreshPolicy.GetDefaultLibraryScope(FriendRefreshScope.SelectedGame),
                        PlayniteGameIds = new[] { gameId },
                        FriendExternalUserIds = new[] { friend.ExternalUserId }
                    }
                };
                return true;
            }

            if (TryGetProviderAppTarget(game, out var providerKey, out var appId))
            {
                request = new RefreshRequest
                {
                    Mode = RefreshModeType.FriendsCustom,
                    CustomFriendOptions = new FriendCustomRefreshOptions
                    {
                        ProviderKeys = new[] { providerKey },
                        Scope = FriendRefreshScope.SelectedGame,
                        LibraryScope = FriendLibraryScope.Full,
                        ProviderAppIds = new[] { appId },
                        FriendExternalUserIds = new[] { friend.ExternalUserId }
                    }
                };
                return true;
            }

            return false;
        }

        private static bool TryBuildSelectedGameRefreshRequest(object game, out RefreshRequest request)
        {
            request = null;
            if (TryGetPlayniteGameId(game, out var gameId))
            {
                request = new RefreshRequest
                {
                    Mode = RefreshModeType.FriendsSelectedGame,
                    SingleGameId = gameId
                };
                return true;
            }

            if (TryGetProviderAppTarget(game, out var providerKey, out var appId))
            {
                request = new RefreshRequest
                {
                    Mode = RefreshModeType.FriendsCustom,
                    CustomFriendOptions = new FriendCustomRefreshOptions
                    {
                        ProviderKeys = new[] { providerKey },
                        Scope = FriendRefreshScope.SelectedGame,
                        LibraryScope = FriendLibraryScope.Full,
                        ProviderAppIds = new[] { appId }
                    }
                };
                return true;
            }

            return false;
        }

        private static bool TryGetFriendTarget(object parameter, out FriendSummaryItem friend)
        {
            friend = parameter as FriendSummaryItem;
            return friend != null &&
                   !string.IsNullOrWhiteSpace(friend.ProviderKey) &&
                   !string.IsNullOrWhiteSpace(friend.ExternalUserId);
        }

        private static bool TryGetGameTarget(object parameter, out object game)
        {
            game = null;
            if (parameter == null)
            {
                return false;
            }

            if (TryGetPlayniteGameId(parameter, out _) ||
                TryGetProviderAppTarget(parameter, out _, out _))
            {
                game = parameter;
                return true;
            }

            return false;
        }

        private void OnRebuildProgress(object sender, ProgressReport report)
        {
            if (report?.Mode?.IsFriendRefreshMode() != true)
            {
                return;
            }

            IsRefreshing = _refreshRuntime?.IsRebuilding == true;
            ProgressPercent = Math.Max(0, Math.Min(100, report.PercentComplete));
            if (!string.IsNullOrWhiteSpace(report.Message))
            {
                ProgressMessage = report.Message;
            }

            if (_refreshRuntime?.IsFinalProgressReport(report) == true)
            {
                _ = LoadFromCacheAsync();
            }
        }

        private async Task LoadFromCacheAsync()
        {
            // Latest-wins guard: a newer load supersedes any in-flight one so its
            // (possibly stale) results are discarded when they arrive on the UI thread.
            var version = Interlocked.Increment(ref _loadVersion);
            var hideSpoilers = _settings?.Persisted?.FriendsOverviewHideSpoilers ?? true;

            FriendsOverviewData data;
            try
            {
                // Heavy work (summary SQL + per-game presentation resolution) runs off
                // the UI thread; the window stays responsive while data loads.
                data = await Task
                    .Run(() =>
                    {
                        using (PerfScope.Start(_logger, "FriendsOverview.LoadCache", thresholdMs: 25))
                        {
                            return _friendCache?.LoadFriendsOverviewData(hideSpoilers, 0);
                        }
                    })
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to load friends overview cache data.");
                data = null;
            }

            void Apply()
            {
                if (_disposed || version != Volatile.Read(ref _loadVersion))
                {
                    return;
                }

                if (data != null)
                {
                    _allFriends = data.Friends ?? new List<FriendSummaryItem>();
                    _allGames = data.Games ?? new List<FriendGameSummaryItem>();
                    _allRecentUnlocks = data.RecentUnlocks ?? new List<FriendAchievementDisplayItem>();
                    _allUnlockedAchievements = data.AllUnlockedAchievements ?? new List<FriendAchievementDisplayItem>();
                    _projection = new FriendOverviewProjection(data);

                    using (PerfScope.Start(_logger, "FriendsOverview.ApplyOnUiThread", thresholdMs: 15))
                    {
                        _friendSearchIndex.Rebuild(_allFriends);
                        _gameSearchIndex.Rebuild(_allGames);
                        _achievementSearchIndex.Rebuild(_allRecentUnlocks.Concat(_allUnlockedAchievements));
                        UpdateFilterOptions();
                        ApplyFilters();
                    }

                    StatusText = HasData
                        ? null
                        : ResourceProvider.GetString("LOCPlayAch_FriendsOverview_NoData") ??
                          "No friend achievement data yet.";
                    OnPropertyChanged(nameof(HasData));
                    OnPropertyChanged(nameof(IsProviderDisabled));
                }
                else
                {
                    _allFriends = new List<FriendSummaryItem>();
                    _allGames = new List<FriendGameSummaryItem>();
                    _allRecentUnlocks = new List<FriendAchievementDisplayItem>();
                    _allUnlockedAchievements = new List<FriendAchievementDisplayItem>();
                    _projection = new FriendOverviewProjection(null);
                    FilteredFriends.ReplaceAll(Array.Empty<FriendSummaryItem>());
                    FilteredGames.ReplaceAll(Array.Empty<FriendGameSummaryItem>());
                    DisplayedAchievements.ReplaceAll(Array.Empty<FriendAchievementDisplayItem>());
                    StatusText = ResourceProvider.GetString("LOCPlayAch_FriendsOverview_LoadFailed") ??
                                 "Failed to load friend achievement data.";
                    OnPropertyChanged(nameof(HasData));
                }
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.InvokeIfNeeded(Apply);
            }
            else
            {
                Apply();
            }
        }

        private void ApplyFilters()
        {
            if (_isApplyingFilters)
            {
                return;
            }

            try
            {
                _isApplyingFilters = true;

                if (SelectedFriend != null && SelectedGame != null && !HasUnlocksForFriendGame(SelectedFriend, SelectedGame))
                {
                    _selectedGame = null;
                    OnPropertyChanged(nameof(SelectedGame));
                    NotifySelectionStateChanged();
                }

                var friendQuery = SearchQuery.From(FriendSearchText);
                var gameQuery = SearchQuery.From(GameSearchText);
                var achievementQuery = SearchQuery.From(AchievementSearchText);

                var friends = _allFriends
                    .Where(friend => MatchesProvider(friend?.ProviderKey))
                    .Where(friend => _friendSearchIndex.Matches(friend, friendQuery));

                if (SelectedGame != null)
                {
                    friends = friends.Where(friend => HasUnlocksForFriendGame(friend, SelectedGame));
                }

                var friendList = friends
                    .OrderByDescending(friend => friend?.LastUnlockUtc ?? DateTime.MinValue)
                    .ThenBy(friend => friend?.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                var gameSource = SelectedFriend != null
                    ? GetSelectedFriendGames(SelectedFriend)
                    : _allGames;

                var games = gameSource
                    .Where(game => MatchesProvider(game?.ProviderKey))
                    .Where(game => _gameSearchIndex.Matches(game, gameQuery));

                if (SelectedFriend == null)
                {
                    games = games.Where(HasAnyFriendUnlocks);
                }

                var gameList = games
                    .OrderByDescending(game => game?.LastUnlockUtc ?? DateTime.MinValue)
                    .ThenBy(game => game?.GameName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                var selectionChanged = false;
                if (SelectedFriend != null && !friendList.Any(friend => IsSameFriend(friend, SelectedFriend)))
                {
                    _selectedFriend = null;
                    OnPropertyChanged(nameof(SelectedFriend));
                    NotifySelectionStateChanged();
                    selectionChanged = true;
                }

                if (SelectedGame != null)
                {
                    var matchingGame = gameList.FirstOrDefault(game => IsSameGame(game, SelectedGame));
                    if (matchingGame == null)
                    {
                        _selectedGame = null;
                        OnPropertyChanged(nameof(SelectedGame));
                        NotifySelectionStateChanged();
                        selectionChanged = true;
                    }
                    else if (!ReferenceEquals(_selectedGame, matchingGame))
                    {
                        _selectedGame = matchingGame;
                        OnPropertyChanged(nameof(SelectedGame));
                        NotifySelectionStateChanged();
                    }
                }

                if (selectionChanged)
                {
                    _isApplyingFilters = false;
                    ApplyFilters();
                    return;
                }

                var achievementSource = HasAnySelection
                    ? _allUnlockedAchievements
                    : _allRecentUnlocks;

                var achievements = achievementSource
                    .Where(achievement => MatchesProvider(achievement?.ProviderKey))
                    .Where(achievement => _achievementSearchIndex.Matches(achievement, achievementQuery))
                    .Where(MatchesAchievementFilters);

                if (SelectedFriend != null)
                {
                    achievements = achievements.Where(achievement => IsSameFriend(achievement, SelectedFriend));
                }

                if (SelectedGame != null)
                {
                    achievements = achievements.Where(achievement => IsSameGame(achievement, SelectedGame));
                }

                var achievementList = achievements
                    .OrderByDescending(achievement => achievement?.UnlockTimeUtc ?? DateTime.MinValue)
                    .ThenBy(achievement => achievement?.FriendName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(achievement => achievement?.GameName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(achievement => achievement?.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                var persisted = _settings?.Persisted;
                FilteredFriends.ReplaceAll(DisplayGridRowLimitHelper.Limit(
                    friendList,
                    persisted?.FriendsOverviewFriendSummariesGridMaxRows));
                FilteredGames.ReplaceAll(DisplayGridRowLimitHelper.Limit(
                    gameList,
                    persisted?.FriendsOverviewGameSummariesGridMaxRows));
                DisplayedAchievements.ReplaceAll(DisplayGridRowLimitHelper.Limit(
                    achievementList,
                    persisted?.FriendsOverviewAchievementsGridMaxRows));
                OnPropertyChanged(nameof(AchievementCountText));
                OnPropertyChanged(nameof(HasData));
            }
            finally
            {
                _isApplyingFilters = false;
            }
        }

        private bool MatchesProvider(string providerKey)
        {
            return string.IsNullOrWhiteSpace(SelectedProviderKey) ||
                   string.Equals(providerKey, SelectedProviderKey, StringComparison.OrdinalIgnoreCase);
        }

        private bool MatchesAchievementFilters(FriendAchievementDisplayItem item)
        {
            if (item == null)
            {
                return false;
            }

            if (_selectedTypeFilters.Count > 0 &&
                !_selectedTypeFilters.Contains(item.CategoryType ?? string.Empty))
            {
                return false;
            }

            if (_selectedCategoryFilters.Count > 0 &&
                !_selectedCategoryFilters.Contains(item.CategoryLabel ?? string.Empty))
            {
                return false;
            }

            return true;
        }

        private IReadOnlyList<FriendGameSummaryItem> GetSelectedFriendGames(FriendSummaryItem friend)
        {
            return _projection?.GetSelectedFriendGames(friend) ?? Array.Empty<FriendGameSummaryItem>();
        }

        private bool HasAnyFriendUnlocks(FriendGameSummaryItem game)
        {
            return _projection?.HasAnyFriendUnlocks(game) == true;
        }

        private bool HasUnlocksForFriendGame(FriendSummaryItem friend, FriendGameSummaryItem game)
        {
            return _projection?.HasUnlocksForFriendGame(friend, game) == true;
        }

        private static bool IsSameFriend(FriendSummaryItem left, FriendSummaryItem right)
        {
            return FriendOverviewProjection.IsSameFriend(left, right);
        }

        private static bool IsSameFriend(FriendAchievementDisplayItem achievement, FriendSummaryItem friend)
        {
            return FriendOverviewProjection.IsSameFriend(achievement, friend);
        }

        private static bool IsSameGame(FriendGameSummaryItem left, FriendGameSummaryItem right)
        {
            return FriendOverviewProjection.IsSameGame(left, right);
        }

        private void OpenGameInLibrary(object parameter)
        {
            if (!TryGetPlayniteGameId(parameter, out var gameId))
            {
                return;
            }

            try
            {
                _playniteApi?.MainView?.SelectGame(gameId);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to open game in Playnite library: {gameId}");
            }
        }

        private static bool TryGetPlayniteGameId(object parameter, out Guid gameId)
        {
            switch (parameter)
            {
                case GameSummaryItem game when game.PlayniteGameId.HasValue && game.PlayniteGameId.Value != Guid.Empty:
                    gameId = game.PlayniteGameId.Value;
                    return true;
                case AchievementDisplayItem achievement when achievement.PlayniteGameId.HasValue && achievement.PlayniteGameId.Value != Guid.Empty:
                    gameId = achievement.PlayniteGameId.Value;
                    return true;
                case Guid id when id != Guid.Empty:
                    gameId = id;
                    return true;
                default:
                    gameId = Guid.Empty;
                    return false;
            }
        }

        private static bool TryGetProviderAppTarget(object parameter, out string providerKey, out int appId)
        {
            switch (parameter)
            {
                case FriendGameSummaryItem game
                    when !game.PlayniteGameId.HasValue &&
                         !string.IsNullOrWhiteSpace(game.ProviderKey) &&
                         game.AppId > 0:
                    providerKey = game.ProviderKey.Trim();
                    appId = game.AppId;
                    return true;
                case FriendAchievementDisplayItem achievement
                    when !achievement.PlayniteGameId.HasValue &&
                         !string.IsNullOrWhiteSpace(achievement.ProviderKey) &&
                         achievement.AppId > 0:
                    providerKey = achievement.ProviderKey.Trim();
                    appId = achievement.AppId;
                    return true;
                default:
                    providerKey = null;
                    appId = 0;
                    return false;
            }
        }

        private static bool IsSameGame(FriendAchievementDisplayItem achievement, FriendGameSummaryItem game)
        {
            return FriendOverviewProjection.IsSameGame(achievement, game);
        }

        private void UpdateFilterOptions()
        {
            ReplaceOptions(
                ProviderFilterOptions,
                _allFriends.Select(friend => friend?.ProviderKey)
                    .Concat(_allGames.Select(game => game?.ProviderKey))
                    .Concat(_allUnlockedAchievements.Select(achievement => achievement?.ProviderKey)));

            ReplaceOptions(
                TypeFilterOptions,
                _allUnlockedAchievements.Select(achievement => achievement?.CategoryType)
                    .Concat(_allRecentUnlocks.Select(achievement => achievement?.CategoryType)));

            ReplaceOptions(
                CategoryFilterOptions,
                _allUnlockedAchievements.Select(achievement => achievement?.CategoryLabel)
                    .Concat(_allRecentUnlocks.Select(achievement => achievement?.CategoryLabel)));

            PruneFilterSelections(_selectedTypeFilters, TypeFilterOptions);
            PruneFilterSelections(_selectedCategoryFilters, CategoryFilterOptions);
            if (!string.IsNullOrWhiteSpace(SelectedProviderKey) &&
                !ProviderFilterOptions.Any(provider => string.Equals(provider, SelectedProviderKey, StringComparison.OrdinalIgnoreCase)))
            {
                _selectedProviderKey = null;
                OnPropertyChanged(nameof(SelectedProviderKey));
                OnPropertyChanged(nameof(SelectedProviderFilterText));
            }

            OnPropertyChanged(nameof(SelectedTypeFilterText));
            OnPropertyChanged(nameof(SelectedCategoryFilterText));
        }

        private static void ReplaceOptions(ObservableCollection<string> target, IEnumerable<string> values)
        {
            var next = (values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            target.Clear();
            foreach (var value in next)
            {
                target.Add(value);
            }
        }

        private static bool IsFilterSelected(HashSet<string> selectedValues, string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   selectedValues != null &&
                   selectedValues.Contains(value);
        }

        private static bool SetFilterSelection(HashSet<string> selectedValues, string value, bool isSelected)
        {
            if (selectedValues == null || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return isSelected
                ? selectedValues.Add(value)
                : selectedValues.Remove(value);
        }

        private static void PruneFilterSelections(HashSet<string> selectedValues, IEnumerable<string> options)
        {
            if (selectedValues == null)
            {
                return;
            }

            var available = new HashSet<string>(options ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            selectedValues.RemoveWhere(value => !available.Contains(value));
        }

        private static string GetSelectedFilterText(
            HashSet<string> selectedValues,
            ObservableCollection<string> options,
            string placeholder)
        {
            var selectedCount = selectedValues?.Count ?? 0;
            if (selectedCount <= 0)
            {
                return placeholder;
            }

            if (selectedCount == 1)
            {
                return selectedValues.First();
            }

            var format = ResourceProvider.GetString("LOCPlayAch_Filter_SelectedCount") ?? "{0:N0} selected";
            return string.Format(format, selectedCount);
        }

        private void NotifySelectionStateChanged()
        {
            OnPropertyChanged(nameof(HasFriendSelection));
            OnPropertyChanged(nameof(HasGameSelection));
            OnPropertyChanged(nameof(HasAnySelection));
            OnPropertyChanged(nameof(HasFriendGameSelection));
            OnPropertyChanged(nameof(AchievementSectionTitle));
            OnPropertyChanged(nameof(AchievementCountText));
        }

        private void RaiseRefreshCanExecuteChanged()
        {
            (RefreshCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (RefreshRecentCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (RefreshFullCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (RefreshSharedCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (RefreshInstalledCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (RefreshFriendSelectedGameCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (RefreshOrCancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void OnPersistedSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            var propertyName = e?.PropertyName;
            if (string.IsNullOrWhiteSpace(propertyName) ||
                propertyName == nameof(PersistedSettings.FriendsOverviewFriendSummariesGridMaxRows) ||
                propertyName == nameof(PersistedSettings.FriendsOverviewGameSummariesGridMaxRows) ||
                propertyName == nameof(PersistedSettings.FriendsOverviewAchievementsGridMaxRows))
            {
                ApplyFilters();
            }
        }

        private static IEnumerable<RefreshMode> CreateFriendRefreshModes()
        {
            yield return CreateRefreshMode(RefreshModeType.FriendsRecent);
            yield return CreateRefreshMode(RefreshModeType.FriendsFull);
            yield return CreateRefreshMode(RefreshModeType.FriendsShared);
            yield return CreateRefreshMode(RefreshModeType.FriendsInstalled);
            yield return CreateRefreshMode(RefreshModeType.FriendsSelectedGame);
            yield return CreateRefreshMode(RefreshModeType.FriendsCustom);
        }

        private static RefreshMode CreateRefreshMode(RefreshModeType type)
        {
            return new RefreshMode(type, type.GetResourceKey(), type.GetShortResourceKey())
            {
                DisplayName = ResourceProvider.GetString(type.GetResourceKey()) ?? type.GetKey(),
                ShortDisplayName = ResourceProvider.GetString(type.GetShortResourceKey()) ?? type.GetKey()
            };
        }

        public void Dispose()
        {
            _disposed = true;

            if (_refreshRuntime != null)
            {
                _refreshRuntime.RebuildProgress -= OnRebuildProgress;
            }

            if (_settings?.Persisted != null)
            {
                _settings.Persisted.PropertyChanged -= OnPersistedSettingsChanged;
            }
        }
    }
}

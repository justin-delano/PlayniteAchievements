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
using System.Threading.Tasks;
using System.Windows.Input;

using AsyncCommand = PlayniteAchievements.Common.AsyncCommand;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels
{
    internal sealed class FriendsOverviewViewModel : ObservableObject, IDisposable
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
        private List<FriendGameLinkItem> _friendGameLinks = new List<FriendGameLinkItem>();
        private HashSet<string> _gameUnlockKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _friendGameUnlockKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _selectedTypeFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _selectedCategoryFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private FriendSummaryItem _selectedFriend;
        private FriendGameSummaryItem _selectedGame;
        private bool _isApplyingFilters;
        private bool _isRefreshing;
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
            RefreshFriendSelectedGameCommand = new AsyncCommand(ExecuteFriendSelectedGameRefreshAsync, parameter => CanRefreshSelectedFriendGame(parameter));
            OpenGameInLibraryCommand = new RelayCommand(OpenGameInLibrary);
            RefreshOrCancelCommand = RefreshCommand;
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
                }
            }
        }

        public string RefreshModeSelectionText => FriendRefreshModes?
            .FirstOrDefault(mode => string.Equals(mode?.Key, SelectedRefreshMode, StringComparison.Ordinal))?
            .ShortDisplayName
            ?? ResourceProvider.GetString("LOCPlayAch_Button_Refresh")
            ?? "Refresh";

        public string RefreshActionButtonText => ResourceProvider.GetString("LOCPlayAch_Button_Refresh") ?? "Refresh";

        public string RefreshOrCancelButtonText => IsRefreshing
            ? ResourceProvider.GetString("LOCPlayAch_FriendsOverview_RefreshingShort") ?? "Refreshing..."
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
                ResourceProvider.GetString("LOCPlayAch_FriendsOverview_SelectedGameAchievementCount") ?? "({0:N0} / {1:N0})",
                DisplayedAchievements.Count,
                Math.Max(0, SelectedGame?.TotalAchievements ?? 0))
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

        private async Task RefreshSelectedModeAsync()
        {
            var selected = FriendRefreshModes.FirstOrDefault(mode =>
                string.Equals(mode?.Key, SelectedRefreshMode, StringComparison.Ordinal));
            if (selected?.Type == RefreshModeType.FriendsSelectedGame)
            {
                await ExecuteFriendSelectedGameRefreshAsync(SelectedGame).ConfigureAwait(true);
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

        private async Task ExecuteFriendSelectedGameRefreshAsync(object parameter)
        {
            if (!TryGetPlayniteGameId(parameter, out var gameId) || !CanRefresh())
            {
                StatusText = ResourceProvider.GetString("LOCPlayAch_FriendsOverview_SelectGameForRefresh") ??
                             "Select a game before refreshing this friend game.";
                return;
            }

            await ExecuteFriendRefreshRequestAsync(
                new RefreshRequest
                {
                    Mode = RefreshModeType.FriendsSelectedGame,
                    SingleGameId = gameId
                },
                "Friend selected-game refresh failed.").ConfigureAwait(true);
        }

        private bool CanRefreshSelectedFriendGame(object parameter)
        {
            return CanRefresh() && TryGetPlayniteGameId(parameter, out _);
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

        private Task LoadFromCacheAsync()
        {
            try
            {
                var persisted = _settings?.Persisted;
                var data = _friendCache?.LoadFriendsOverviewData(
                    persisted?.FriendsOverviewHideSpoilers ?? true,
                    0);

                _allFriends = data?.Friends ?? new List<FriendSummaryItem>();
                _allGames = data?.Games ?? new List<FriendGameSummaryItem>();
                _allRecentUnlocks = data?.RecentUnlocks ?? new List<FriendAchievementDisplayItem>();
                _allUnlockedAchievements = data?.AllUnlockedAchievements ?? new List<FriendAchievementDisplayItem>();
                _friendGameLinks = data?.FriendGameLinks ?? new List<FriendGameLinkItem>();
                RebuildUnlockRelationshipIndexes();

                _friendSearchIndex.Rebuild(_allFriends);
                _gameSearchIndex.Rebuild(_allGames);
                _achievementSearchIndex.Rebuild(_allRecentUnlocks.Concat(_allUnlockedAchievements));
                UpdateFilterOptions();
                ApplyFilters();

                StatusText = HasData
                    ? null
                    : ResourceProvider.GetString("LOCPlayAch_FriendsOverview_NoData") ??
                      "No friend achievement data yet.";
                OnPropertyChanged(nameof(HasData));
                OnPropertyChanged(nameof(IsProviderDisabled));
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to load friends overview cache data.");
                _allFriends = new List<FriendSummaryItem>();
                _allGames = new List<FriendGameSummaryItem>();
                _allRecentUnlocks = new List<FriendAchievementDisplayItem>();
                _allUnlockedAchievements = new List<FriendAchievementDisplayItem>();
                _friendGameLinks = new List<FriendGameLinkItem>();
                RebuildUnlockRelationshipIndexes();
                FilteredFriends.ReplaceAll(Array.Empty<FriendSummaryItem>());
                FilteredGames.ReplaceAll(Array.Empty<FriendGameSummaryItem>());
                DisplayedAchievements.ReplaceAll(Array.Empty<FriendAchievementDisplayItem>());
                StatusText = ResourceProvider.GetString("LOCPlayAch_FriendsOverview_LoadFailed") ??
                             "Failed to load friend achievement data.";
                OnPropertyChanged(nameof(HasData));
            }

            return Task.CompletedTask;
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

                var games = _allGames
                    .Where(game => MatchesProvider(game?.ProviderKey))
                    .Where(HasAnyFriendUnlocks)
                    .Where(game => _gameSearchIndex.Matches(game, gameQuery));

                if (SelectedFriend != null)
                {
                    games = games.Where(game => HasUnlocksForFriendGame(SelectedFriend, game));
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

                if (SelectedGame != null && !gameList.Any(game => IsSameGame(game, SelectedGame)))
                {
                    _selectedGame = null;
                    OnPropertyChanged(nameof(SelectedGame));
                    NotifySelectionStateChanged();
                    selectionChanged = true;
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

        private void RebuildUnlockRelationshipIndexes()
        {
            _gameUnlockKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _friendGameUnlockKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var achievement in _allUnlockedAchievements ?? Enumerable.Empty<FriendAchievementDisplayItem>())
            {
                var gameKey = BuildGameUnlockKey(achievement?.ProviderKey, achievement?.AppId ?? 0, achievement?.PlayniteGameId);
                if (!string.IsNullOrWhiteSpace(gameKey))
                {
                    _gameUnlockKeys.Add(gameKey);
                }

                var friendGameKey = BuildFriendGameUnlockKey(
                    achievement?.ProviderKey,
                    achievement?.FriendExternalUserId,
                    achievement?.AppId ?? 0,
                    achievement?.PlayniteGameId);
                if (!string.IsNullOrWhiteSpace(friendGameKey))
                {
                    _friendGameUnlockKeys.Add(friendGameKey);
                }
            }
        }

        private bool HasAnyFriendUnlocks(FriendGameSummaryItem game)
        {
            if (game == null)
            {
                return false;
            }

            var gameKey = BuildGameUnlockKey(game.ProviderKey, game.AppId, game.PlayniteGameId);
            if (!string.IsNullOrWhiteSpace(gameKey) && _gameUnlockKeys.Contains(gameKey))
            {
                return true;
            }

            return _allUnlockedAchievements.Count == 0 &&
                   (game.FriendsWithUnlocksCount > 0 ||
                    game.FriendUnlockedAchievementsCount > 0 ||
                    game.LastFriendUnlockUtc.HasValue);
        }

        private bool HasUnlocksForFriendGame(FriendSummaryItem friend, FriendGameSummaryItem game)
        {
            if (friend == null || game == null)
            {
                return false;
            }

            var key = BuildFriendGameUnlockKey(friend.ProviderKey, friend.ExternalUserId, game.AppId, game.PlayniteGameId);
            if (!string.IsNullOrWhiteSpace(key) && _friendGameUnlockKeys.Contains(key))
            {
                return true;
            }

            return _allUnlockedAchievements.Count == 0 &&
                   game.FriendsWithUnlocksCount > 0 &&
                   _friendGameLinks.Any(link => IsSameFriend(link, friend) && IsSameGame(link, game));
        }

        private static string BuildFriendGameUnlockKey(
            string providerKey,
            string externalUserId,
            int appId,
            Guid? playniteGameId)
        {
            if (string.IsNullOrWhiteSpace(externalUserId))
            {
                return null;
            }

            var gameKey = BuildGameUnlockKey(providerKey, appId, playniteGameId);
            return string.IsNullOrWhiteSpace(gameKey)
                ? null
                : externalUserId.Trim().ToLowerInvariant() + "|" + gameKey;
        }

        private static string BuildGameUnlockKey(string providerKey, int appId, Guid? playniteGameId)
        {
            var provider = string.IsNullOrWhiteSpace(providerKey)
                ? string.Empty
                : providerKey.Trim().ToLowerInvariant();
            if (appId > 0)
            {
                return provider + "|app:" + appId.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
            }

            return playniteGameId.HasValue && playniteGameId.Value != Guid.Empty
                ? provider + "|playnite:" + playniteGameId.Value.ToString("D")
                : null;
        }

        private static bool IsSameFriend(FriendSummaryItem left, FriendSummaryItem right)
        {
            return left != null &&
                   right != null &&
                   string.Equals(left.ProviderKey, right.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(left.ExternalUserId, right.ExternalUserId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameFriend(FriendAchievementDisplayItem achievement, FriendSummaryItem friend)
        {
            return achievement != null &&
                   friend != null &&
                   string.Equals(achievement.ProviderKey, friend.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(achievement.FriendExternalUserId, friend.ExternalUserId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameFriend(FriendGameLinkItem link, FriendSummaryItem friend)
        {
            return link != null &&
                   friend != null &&
                   string.Equals(link.ProviderKey, friend.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(link.ExternalUserId, friend.ExternalUserId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameGame(FriendGameSummaryItem left, FriendGameSummaryItem right)
        {
            return left != null &&
                   right != null &&
                   string.Equals(left.ProviderKey, right.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
                   (left.AppId > 0 && left.AppId == right.AppId ||
                    left.PlayniteGameId.HasValue && right.PlayniteGameId.HasValue && left.PlayniteGameId.Value == right.PlayniteGameId.Value);
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

        private static bool IsSameGame(FriendAchievementDisplayItem achievement, FriendGameSummaryItem game)
        {
            return achievement != null &&
                   game != null &&
                   string.Equals(achievement.ProviderKey, game.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
                   (achievement.AppId > 0 && achievement.AppId == game.AppId ||
                    achievement.PlayniteGameId.HasValue && game.PlayniteGameId.HasValue && achievement.PlayniteGameId.Value == game.PlayniteGameId.Value);
        }

        private static bool IsSameGame(FriendGameLinkItem link, FriendGameSummaryItem game)
        {
            return link != null &&
                   game != null &&
                   string.Equals(link.ProviderKey, game.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
                   (link.AppId > 0 && link.AppId == game.AppId ||
                    link.PlayniteGameId.HasValue && game.PlayniteGameId.HasValue && link.PlayniteGameId.Value == game.PlayniteGameId.Value);
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

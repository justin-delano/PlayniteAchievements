using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Summaries;
using PlayniteAchievements.Views.Helpers;
using Playnite.SDK;

using ObservableObject = PlayniteAchievements.Common.ObservableObject;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels
{
    public class ViewAchievementsViewModel : ObservableObject, IDisposable
    {
        private readonly RefreshRuntime _refreshService;
        private readonly AchievementDataService _achievementDataService;
        private readonly IPlayniteAPI _playniteApi;
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly GameSummaryItemBuilder _summaryBuilder;
        private readonly Guid _gameId;
        private Guid? _activeRefreshOperationId;
        private bool _isApplyingTimelineState;

        // Sort state tracking for quick reverse
        private string _currentSortPath;
        private ListSortDirection _currentSortDirection;

        // Search and filter state
        private List<AchievementDisplayItem> _allAchievements = new List<AchievementDisplayItem>();
        private List<AchievementDisplayItem> _filteredAchievements = new List<AchievementDisplayItem>();
        private string _searchText = string.Empty;
        private bool _showUnlocked = true;
        private bool _showLocked = true;
        private bool _showHidden = true;
        private bool _hasCustomAchievementOrder;
        private readonly HashSet<string> _selectedCategoryTypeFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _selectedCategoryLabelFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // In-memory sort/filter state for the most recently viewed game. Restored when the
        // window reopens for the same game, overwritten on every close, and reset when a
        // different game is opened. Process-lifetime only; clears on Playnite restart.
        private sealed class GridStateSnapshot
        {
            public Guid GameId;
            public string SortPath;
            public ListSortDirection SortDirection;
            public string SearchText;
            public bool ShowUnlocked;
            public bool ShowLocked;
            public bool ShowHidden;
            public List<string> CategoryTypeFilters;
            public List<string> CategoryLabelFilters;
        }

        private static GridStateSnapshot _lastGridState;

        public ViewAchievementsViewModel(
            Guid gameId,
            RefreshRuntime refreshRuntime,
            AchievementDataService achievementDataService,
            IPlayniteAPI playniteApi,
            ILogger logger,
            PlayniteAchievementsSettings settings)
        {
            _gameId = gameId;
            _refreshService = refreshRuntime ?? throw new ArgumentNullException(nameof(refreshRuntime));
            _achievementDataService = achievementDataService ?? throw new ArgumentNullException(nameof(achievementDataService));
            _playniteApi = playniteApi;
            _logger = logger;
            _settings = settings;
            _summaryBuilder = new GameSummaryItemBuilder(_refreshService.Providers, _playniteApi, _logger);

            Timeline = new TimelineViewModel();
            ApplySavedTimelineState();
            Timeline.PropertyChanged += Timeline_PropertyChanged;
            OnPropertyChanged(nameof(Timeline));

            CategoryTypeFilterOptions = new ObservableCollection<string>();
            CategoryLabelFilterOptions = new ObservableCollection<string>();

            // Initialize commands
            RevealAchievementCommand = new RelayCommand(param => RevealAchievement(param as AchievementDisplayItem));
            DismissStatusCommand = new RelayCommand(_ => DismissStatus(), _ => CanDismissStatus);
            OpenGameInLibraryCommand = new RelayCommand(_ => OpenGameInLibrary());

            RefreshGameCommand = new RelayCommand(
                async (param) =>
                {
                    if (IsRefreshing) return;

                    IsRefreshing = true;
                    _activeRefreshOperationId = null;
                    RefreshStatusMessage = ResourceProvider.GetString("LOCPlayAch_Status_Refreshing");
                    IsStatusMessageVisible = true;

                    try
                    {
                        await ExecuteSingleGameRefreshAsync();

                        // Load updated data
                        LoadGameData();

                        // Simple success message
                        RefreshStatusMessage = ResourceProvider.GetString("LOCPlayAch_Status_RefreshComplete");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Failed to refresh game {_gameId}.");
                        RefreshStatusMessage = string.Format(
                            ResourceProvider.GetString("LOCPlayAch_Error_RefreshFailed"),
                            ex.Message);
                    }
                    finally
                    {
                        IsRefreshing = false;
                        _activeRefreshOperationId = null;
                        await Task.Delay(3000);
                        IsStatusMessageVisible = false;
                    }
                },
                _ => !_refreshService.IsRebuilding);

            // Subscribe to settings changes
            if (_settings != null)
            {
                _settings.PropertyChanged += OnSettingsChanged;
                if (_settings.Persisted != null)
                {
                    _settings.Persisted.PropertyChanged += OnPersistedSettingsChanged;
                }
            }
            _refreshService.GameCacheUpdated += OnGameCacheUpdated;
            _refreshService.CacheDeltaUpdated += OnCacheDeltaUpdated;
            _refreshService.RebuildProgress += OnRebuildProgress;

            // Restore the previous session's sort/filter state for this game (if any) before
            // the initial load so the first display reflects it.
            RestoreGridStateIfMatching();

            // Load data
            LoadGameData();
        }

        private void RestoreGridStateIfMatching()
        {
            var snapshot = _lastGridState;
            if (snapshot == null || snapshot.GameId != _gameId)
            {
                return;
            }

            // Assign the backing fields directly to avoid triggering ApplySearchFilter
            // repeatedly; LoadGameData applies the combined state once.
            _currentSortPath = snapshot.SortPath;
            _currentSortDirection = snapshot.SortDirection;
            _searchText = snapshot.SearchText ?? string.Empty;
            _showUnlocked = snapshot.ShowUnlocked;
            _showLocked = snapshot.ShowLocked;
            _showHidden = snapshot.ShowHidden;

            _selectedCategoryTypeFilters.Clear();
            if (snapshot.CategoryTypeFilters != null)
            {
                foreach (var value in snapshot.CategoryTypeFilters)
                {
                    _selectedCategoryTypeFilters.Add(value);
                }
            }

            _selectedCategoryLabelFilters.Clear();
            if (snapshot.CategoryLabelFilters != null)
            {
                foreach (var value in snapshot.CategoryLabelFilters)
                {
                    _selectedCategoryLabelFilters.Add(value);
                }
            }
        }

        private void SaveGridState()
        {
            _lastGridState = new GridStateSnapshot
            {
                GameId = _gameId,
                SortPath = _currentSortPath,
                SortDirection = _currentSortDirection,
                SearchText = _searchText,
                ShowUnlocked = _showUnlocked,
                ShowLocked = _showLocked,
                ShowHidden = _showHidden,
                CategoryTypeFilters = _selectedCategoryTypeFilters.ToList(),
                CategoryLabelFilters = _selectedCategoryLabelFilters.ToList(),
            };
        }

        private async Task ExecuteSingleGameRefreshAsync()
        {
            var coordinator = PlayniteAchievementsPlugin.Instance?.RefreshEntryPoint;
            if (coordinator == null)
            {
                throw new InvalidOperationException("RefreshEntryPoint is not available.");
            }

            await coordinator.ExecuteAsync(
                new RefreshRequest
                {
                    Mode = RefreshModeType.Single,
                    SingleGameId = _gameId
                },
                new RefreshExecutionPolicy
                {
                    ValidateAuthentication = true,
                    UseProgressWindow = false,
                    SwallowExceptions = false
                });
        }

        #region Properties

        private string _gameName;
        public string GameName
        {
            get => _gameName;
            private set => SetValue(ref _gameName, value);
        }

        private int _totalAchievements;
        public int TotalAchievements
        {
            get => _totalAchievements;
            private set
            {
                if (SetValueAndReturn(ref _totalAchievements, value))
                {
                    OnPropertyChanged(nameof(HasAchievements));
                }
            }
        }

        public TimelineViewModel Timeline { get; private set; }

        // Achievement list
        public ObservableCollection<AchievementDisplayItem> Achievements { get; } = new ObservableCollection<AchievementDisplayItem>();

        // Single-row game summary grid (standardized header surface).
        public ObservableCollection<GameSummaryItem> SummaryItems { get; } = new ObservableCollection<GameSummaryItem>();

        public bool SummaryUseCoverImages => _settings?.Persisted?.ViewAchievementsGameSummariesUseCoverImages ?? false;

        public bool SummaryShowGameMetadata => _settings?.Persisted?.ViewAchievementsGameSummariesShowGameMetadata ?? true;

        public bool SummaryShowCompletionBorder => _settings?.Persisted?.ViewAchievementsGameSummariesShowCompletionBorder ?? true;

        public bool SummaryShowColumnHeaders => _settings?.Persisted?.ShowViewAchievementsGameSummariesGridColumnHeaders ?? true;

        public double? SummaryGridRowHeight => _settings?.Persisted?.ViewAchievementsGameSummariesGridRowHeight;

        private bool _IsRefreshing;
        public bool IsRefreshing
        {
            get => _IsRefreshing;
            private set => SetValue(ref _IsRefreshing, value);
        }

        private string _RefreshStatusMessage;
        public string RefreshStatusMessage
        {
            get => _RefreshStatusMessage;
            private set => SetValue(ref _RefreshStatusMessage, value);
        }

        private bool _isStatusMessageVisible;
        public bool IsStatusMessageVisible
        {
            get => _isStatusMessageVisible;
            private set
            {
                if (SetValueAndReturn(ref _isStatusMessageVisible, value))
                {
                    OnPropertyChanged(nameof(CanDismissStatus));
                    (DismissStatusCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanDismissStatus => IsStatusMessageVisible;

        private bool _isTimelineVisible = false;
        public bool IsTimelineVisible
        {
            get => _isTimelineVisible;
            set
            {
                if (SetValueAndReturn(ref _isTimelineVisible, value))
                {
                    PersistTimelineVisibility(value);
                }
            }
        }

        public bool HasAchievements => TotalAchievements > 0;

        public bool HasCustomAchievementOrder
        {
            get => _hasCustomAchievementOrder;
            private set => SetValue(ref _hasCustomAchievementOrder, value);
        }

        public string CurrentSortPath => _currentSortPath;

        public ListSortDirection? CurrentSortDirection =>
            string.IsNullOrWhiteSpace(_currentSortPath)
                ? (ListSortDirection?)null
                : _currentSortDirection;

        public double? SingleGameGridRowHeight => _settings?.Persisted?.SingleGameGridRowHeight;

        // The Manage Achievements window follows the Overview "Selected Game Achievements" glow setting.
        public bool ShowRarityGlow => _settings?.Persisted?.OverviewSelectedGameShowRarityGlow ?? true;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetValueAndReturn(ref _searchText, value ?? string.Empty))
                {
                    ApplySearchFilter();
                }
            }
        }

        public bool ShowUnlocked
        {
            get => _showUnlocked;
            set
            {
                if (SetValueAndReturn(ref _showUnlocked, value))
                {
                    ApplySearchFilter();
                }
            }
        }

        public bool ShowLocked
        {
            get => _showLocked;
            set
            {
                if (SetValueAndReturn(ref _showLocked, value))
                {
                    ApplySearchFilter();
                }
            }
        }

        public bool ShowHidden
        {
            get => _showHidden;
            set
            {
                if (SetValueAndReturn(ref _showHidden, value))
                {
                    ApplySearchFilter();
                }
            }
        }

        public ObservableCollection<string> CategoryTypeFilterOptions { get; }

        public string SelectedCategoryTypeFilterText => GetSelectedFilterText(
            _selectedCategoryTypeFilters,
            CategoryTypeFilterOptions,
            L("LOCPlayAch_Common_Label_Type", "Type"),
            AchievementCategoryTypeHelper.ToCategoryTypeDisplayText);

        public bool IsCategoryTypeFilterSelected(string value)
        {
            return IsFilterSelected(_selectedCategoryTypeFilters, value);
        }

        public void SetCategoryTypeFilterSelected(string value, bool isSelected)
        {
            if (!SetFilterSelection(_selectedCategoryTypeFilters, value, isSelected))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedCategoryTypeFilterText));
            ApplySearchFilter();
        }

        public ObservableCollection<string> CategoryLabelFilterOptions { get; }

        public string SelectedCategoryLabelFilterText => GetSelectedFilterText(
            _selectedCategoryLabelFilters,
            CategoryLabelFilterOptions,
            L("LOCPlayAch_Common_Label_Category", "Category"),
            AchievementCategoryTypeHelper.ToCategoryLabelDisplayText);

        public bool IsCategoryLabelFilterSelected(string value)
        {
            return IsFilterSelected(_selectedCategoryLabelFilters, value);
        }

        public void SetCategoryLabelFilterSelected(string value, bool isSelected)
        {
            if (!SetFilterSelection(_selectedCategoryLabelFilters, value, isSelected))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedCategoryLabelFilterText));
            ApplySearchFilter();
        }

        #endregion

        #region Commands

        public ICommand RevealAchievementCommand { get; }
        public ICommand RefreshGameCommand { get; }
        public ICommand DismissStatusCommand { get; }
        public ICommand OpenGameInLibraryCommand { get; }

        #endregion

        #region Private Methods

        private static bool IsFilterSelected(HashSet<string> selectedValues, string value)
        {
            if (selectedValues == null || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return selectedValues.Contains(value.Trim());
        }

        private static bool SetFilterSelection(HashSet<string> selectedValues, string value, bool isSelected)
        {
            if (selectedValues == null || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim();
            return isSelected
                ? selectedValues.Add(normalized)
                : selectedValues.Remove(normalized);
        }

        private static bool PruneFilterSelections(HashSet<string> selectedValues, IEnumerable<string> options)
        {
            if (selectedValues == null)
            {
                return false;
            }

            var optionSet = new HashSet<string>(
                (options ?? Enumerable.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.OrdinalIgnoreCase);
            return selectedValues.RemoveWhere(value => !optionSet.Contains(value)) > 0;
        }

        private static string GetSelectedFilterText(
            HashSet<string> selectedValues,
            IEnumerable<string> options,
            string placeholder,
            Func<string, string> displayText = null)
        {
            if (selectedValues == null || selectedValues.Count == 0)
            {
                return placeholder;
            }

            var ordered = new List<string>();
            foreach (var option in options ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(option) && selectedValues.Contains(option))
                {
                    ordered.Add(option);
                }
            }

            if (ordered.Count == 0)
            {
                ordered.AddRange(selectedValues.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            }

            return string.Join(", ", ordered.Select(value => displayText?.Invoke(value) ?? value));
        }

        private static string L(string key, string fallback)
        {
            var value = ResourceProvider.GetString(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private void Timeline_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isApplyingTimelineState ||
                e?.PropertyName != nameof(TimelineViewModel.TimelineRange))
            {
                return;
            }

            PersistTimelineRange();
        }

        private void ApplySavedTimelineState()
        {
            var persisted = _settings?.Persisted;
            var range = persisted?.ViewAchievementsTimelineRange ?? TimelineRange.OneYear;
            var isVisible = persisted?.ViewAchievementsTimelineVisible ?? false;

            try
            {
                _isApplyingTimelineState = true;

                if (_isTimelineVisible != isVisible)
                {
                    _isTimelineVisible = isVisible;
                    OnPropertyChanged(nameof(IsTimelineVisible));
                }

                if (Timeline != null && Timeline.TimelineRange != range)
                {
                    Timeline.TimelineRange = range;
                }
            }
            finally
            {
                _isApplyingTimelineState = false;
            }
        }

        private void PersistTimelineRange()
        {
            if (_isApplyingTimelineState || _settings?.Persisted == null || Timeline == null)
            {
                return;
            }

            if (_settings.Persisted.ViewAchievementsTimelineRange == Timeline.TimelineRange)
            {
                return;
            }

            _settings.Persisted.ViewAchievementsTimelineRange = Timeline.TimelineRange;
            PersistSettingsForUi();
        }

        private void PersistTimelineVisibility(bool isVisible)
        {
            if (_isApplyingTimelineState || _settings?.Persisted == null)
            {
                return;
            }

            if (_settings.Persisted.ViewAchievementsTimelineVisible == isVisible)
            {
                return;
            }

            _settings.Persisted.ViewAchievementsTimelineVisible = isVisible;
            PersistSettingsForUi();
        }

        private void PersistSettingsForUi()
        {
            try
            {
                PlayniteAchievementsPlugin.Instance?.PersistSettingsForUi();
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to persist view achievements timeline settings.");
            }
        }

        private void UpdateAchievementFilterOptions(IEnumerable<AchievementDisplayItem> source)
        {
            var typeValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (source != null)
            {
                foreach (var item in source)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var parsedTypes = AchievementCategoryTypeHelper.ParseValues(
                        AchievementCategoryTypeHelper.NormalizeOrDefault(item.CategoryType));
                    foreach (var parsedType in parsedTypes)
                    {
                        if (!string.IsNullOrWhiteSpace(parsedType))
                        {
                            typeValues.Add(parsedType);
                        }
                    }
                }
            }

            var typeOptions = AchievementCategoryTypeHelper.AllowedCategoryTypes
                .Where(typeValues.Contains)
                .ToList();

            var categoryOptions = AchievementCategoryFilterOrderHelper.BuildOrderedCategoryLabels(
                source,
                item => item?.CategoryLabel);

            CollectionHelper.SynchronizeCollection(CategoryTypeFilterOptions, typeOptions);
            CollectionHelper.SynchronizeCollection(CategoryLabelFilterOptions, categoryOptions);

            if (PruneFilterSelections(_selectedCategoryTypeFilters, CategoryTypeFilterOptions))
            {
                OnPropertyChanged(nameof(SelectedCategoryTypeFilterText));
            }

            if (PruneFilterSelections(_selectedCategoryLabelFilters, CategoryLabelFilterOptions))
            {
                OnPropertyChanged(nameof(SelectedCategoryLabelFilterText));
            }
        }

        private void LoadGameData()
        {
            try
            {
                var game = _playniteApi?.Database?.Games?.Get(_gameId);
                if (game == null)
                {
                    _logger?.Warn($"Game not found: {_gameId}");
                    UpdateSummaryItem(null, null);
                    return;
                }

                GameName = game.Name;

                var gameData = _achievementDataService.GetVisibleGameAchievementData(_gameId);
                UpdateSummaryItem(game, gameData);
                if (gameData == null || !gameData.HasAchievements || gameData.Achievements == null)
                {
                    _logger?.Info($"No achievement data for game: {game.Name}");

                    TotalAchievements = 0;
                    _allAchievements = new List<AchievementDisplayItem>();
                    UpdateAchievementFilterOptions(null);
                    HasCustomAchievementOrder = false;

                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        Achievements.Clear();
                    });

                    Timeline.SetCounts(null);
                    return;
                }

                var achievements = gameData.Achievements;
                var hasCustomOrder = gameData.AchievementOrder != null && gameData.AchievementOrder.Count > 0;
                HasCustomAchievementOrder = hasCustomOrder;
                TotalAchievements = achievements.Count;

                var displayItems = new List<AchievementDisplayItem>();
                var unlockCounts = new Dictionary<DateTime, int>();

                IEnumerable<AchievementDetail> projectionOrder = achievements;
                if (hasCustomOrder)
                {
                    projectionOrder = AchievementOrderHelper.ApplyOrder(
                        achievements,
                        a => a.ApiName,
                        gameData.AchievementOrder);
                }

                foreach (var ach in projectionOrder)
                {
                    if (ach.Unlocked && ach.UnlockTimeUtc.HasValue)
                    {
                        var date = DateTimeUtilities.AsUtcKind(ach.UnlockTimeUtc.Value).Date;
                        if (unlockCounts.TryGetValue(date, out var existing))
                        {
                            unlockCounts[date] = existing + 1;
                        }
                        else
                        {
                            unlockCounts[date] = 1;
                        }
                    }

                    var item = AchievementDisplayItem.Create(gameData, ach, _settings, playniteGameIdOverride: _gameId);
                    if (item != null)
                    {
                        displayItems.Add(item);
                    }
                }

                _allAchievements = displayItems;

                UpdateAchievementFilterOptions(_allAchievements);
                ApplySearchFilter();

                Timeline.SetCounts(unlockCounts);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to load game data for {_gameId}");
                HasCustomAchievementOrder = false;
            }
        }

        private void RevealAchievement(AchievementDisplayItem item)
        {
            item?.ToggleReveal();
        }

        private void OpenGameInLibrary()
        {
            try
            {
                PlayniteUiProvider.RestoreMainView();
                _playniteApi?.MainView?.SelectGame(_gameId);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to open game in Playnite library: {_gameId}");
            }
        }

        // Builds the single-row game summary that replaces the legacy header/stats cards.
        private void UpdateSummaryItem(Playnite.SDK.Models.Game game, GameAchievementData gameData)
        {
            GameSummaryItem item = null;

            if (gameData != null)
            {
                if (gameData.Game == null)
                {
                    gameData.Game = game;
                }

                item = _summaryBuilder.Build(gameData, _settings, allowEmpty: true);
            }
            else if (game != null)
            {
                var stub = new GameAchievementData
                {
                    GameName = game.Name,
                    PlayniteGameId = _gameId,
                    Game = game,
                    HasAchievements = false,
                    Achievements = new List<AchievementDetail>()
                };
                item = _summaryBuilder.Build(stub, _settings, allowEmpty: true);
            }

            var items = item != null
                ? new List<GameSummaryItem> { item }
                : new List<GameSummaryItem>();

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                CollectionHelper.SynchronizeCollection(SummaryItems, items));
        }

        private void RaiseSummaryAppearanceProperties()
        {
            OnPropertyChanged(nameof(SummaryUseCoverImages));
            OnPropertyChanged(nameof(SummaryShowGameMetadata));
            OnPropertyChanged(nameof(SummaryShowCompletionBorder));
            OnPropertyChanged(nameof(SummaryShowColumnHeaders));
            OnPropertyChanged(nameof(SummaryGridRowHeight));
        }

        private void DismissStatus()
        {
            IsStatusMessageVisible = false;
        }

        private void OnGameCacheUpdated(object sender, GameCacheUpdatedEventArgs e)
        {
            if (e.GameId == _gameId.ToString())
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(LoadGameData);
            }
        }

        private void OnCacheDeltaUpdated(object sender, CacheDeltaEventArgs e)
        {
            if (e?.IsFullReset != true)
            {
                return;
            }

            System.Windows.Application.Current?.Dispatcher?.Invoke(LoadGameData);
        }

        private void OnRebuildProgress(object sender, ProgressReport report)
        {
            if (report == null) return;
            var refreshStatus = _refreshService.GetRefreshStatusSnapshot(report);
            var statusMessage = refreshStatus.Message ?? ResourceProvider.GetString("LOCPlayAch_Status_Refreshing");

            var isForOurGame = report.CurrentGameId.HasValue && report.CurrentGameId.Value == _gameId;
            if (isForOurGame && report.OperationId.HasValue)
            {
                _activeRefreshOperationId = report.OperationId;
            }

            var isTrackedOperation = _activeRefreshOperationId.HasValue &&
                                     report.OperationId.HasValue &&
                                     _activeRefreshOperationId.Value == report.OperationId.Value;

            if (isForOurGame || isTrackedOperation)
            {
                RefreshStatusMessage = statusMessage;
                OnPropertyChanged(nameof(RefreshStatusMessage));
            }
            else
            {
                return;
            }

            // Handle completion
            if (refreshStatus.IsCanceled)
            {
                IsRefreshing = false;
                _activeRefreshOperationId = null;
                RefreshStatusMessage = statusMessage;
                OnPropertyChanged(nameof(IsRefreshing));
                OnPropertyChanged(nameof(RefreshStatusMessage));
            }
            else if (refreshStatus.IsFinal)
            {
                IsRefreshing = false;
                _activeRefreshOperationId = null;
                RefreshStatusMessage = statusMessage;
                OnPropertyChanged(nameof(IsRefreshing));
                OnPropertyChanged(nameof(RefreshStatusMessage));
            }

            (RefreshGameCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void OnSettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlayniteAchievementsSettings.Persisted))
            {
                if (_settings?.Persisted != null)
                {
                    _settings.Persisted.PropertyChanged -= OnPersistedSettingsChanged;
                    _settings.Persisted.PropertyChanged += OnPersistedSettingsChanged;
                }

                ApplyAppearanceSettingsToAchievements();
                OnPropertyChanged(nameof(SingleGameGridRowHeight));
                RaiseSummaryAppearanceProperties();
                ApplySavedTimelineState();
                ApplySearchFilter(skipDefaultSort: CurrentSortDirection.HasValue);
            }
        }

        private void OnPersistedSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (AchievementDisplayItem.IsAppearanceSettingPropertyName(e?.PropertyName))
            {
                ApplyAppearanceSettingsToAchievements();
                return;
            }

            if (e?.PropertyName == nameof(PersistedSettings.SingleGameGridRowHeight))
            {
                OnPropertyChanged(nameof(SingleGameGridRowHeight));
                return;
            }

            if (e?.PropertyName == nameof(PersistedSettings.OverviewSelectedGameShowRarityGlow))
            {
                OnPropertyChanged(nameof(ShowRarityGlow));
                return;
            }

            if (e?.PropertyName == nameof(PersistedSettings.SingleGameGridMaxRows))
            {
                SyncAchievementsDisplay();
                return;
            }

            if (e?.PropertyName == nameof(PersistedSettings.ViewAchievementsGameSummariesUseCoverImages) ||
                e?.PropertyName == nameof(PersistedSettings.ViewAchievementsGameSummariesShowGameMetadata) ||
                e?.PropertyName == nameof(PersistedSettings.ViewAchievementsGameSummariesShowCompletionBorder) ||
                e?.PropertyName == nameof(PersistedSettings.ShowViewAchievementsGameSummariesGridColumnHeaders) ||
                e?.PropertyName == nameof(PersistedSettings.ViewAchievementsGameSummariesGridRowHeight))
            {
                RaiseSummaryAppearanceProperties();
                return;
            }

            if (e?.PropertyName == nameof(PersistedSettings.ViewAchievementsTimelineRange) ||
                e?.PropertyName == nameof(PersistedSettings.ViewAchievementsTimelineVisible))
            {
                ApplySavedTimelineState();
                return;
            }

            if (!CurrentSortDirection.HasValue &&
                AchievementSortHelper.IsConfiguredDefaultSortPropertyName(
                    e?.PropertyName,
                    AchievementSortSurface.SingleGame))
            {
                ApplySearchFilter();
            }
        }

        private void ApplyAppearanceSettingsToAchievements()
        {
            if (_settings?.Persisted == null)
            {
                return;
            }

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                var items = new HashSet<AchievementDisplayItem>();
                foreach (var item in _allAchievements)
                {
                    if (item != null)
                    {
                        items.Add(item);
                    }
                }

                foreach (var item in Achievements)
                {
                    if (item != null)
                    {
                        items.Add(item);
                    }
                }

                foreach (var item in items)
                {
                    item.ApplyAppearanceSettings(_settings);
                }
            });
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Refreshes the game data display. Called when settings are saved or when cache is updated.
        /// </summary>
        public void RefreshView()
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(LoadGameData);
        }

        #endregion

        public void SortDataGrid(string sortMemberPath, ListSortDirection direction)
        {
            var items = _filteredAchievements.ToList();
            var currentSortDirection = (ListSortDirection?)_currentSortDirection;
            if (!AchievementSortHelper.TrySortItems(
                    items,
                    sortMemberPath,
                    direction,
                    AchievementSortScope.GameAchievements,
                    ref _currentSortPath,
                    ref currentSortDirection))
            {
                return;
            }

            if (currentSortDirection.HasValue)
            {
                _currentSortDirection = currentSortDirection.Value;
            }

            _filteredAchievements = items;
            SyncAchievementsDisplay();
        }

        public void ResetSortToDefault()
        {
            _currentSortPath = null;
            _currentSortDirection = AchievementSortHelper.GetConfiguredDefaultSort(
                _settings?.Persisted,
                AchievementSortSurface.SingleGame).Direction;
            ApplySearchFilter(skipDefaultSort: true);
        }

        private void ApplySearchFilter(bool skipDefaultSort = false)
        {
            IEnumerable<AchievementDisplayItem> filtered = _allAchievements;

            if (!ShowHidden)
            {
                filtered = filtered.Where(a => !(a.Hidden && !a.Unlocked));
            }

            filtered = filtered.Where(a => a.Unlocked ? ShowUnlocked : ShowLocked);

            if (_selectedCategoryTypeFilters.Count > 0)
            {
                var selectedTypeSet = new HashSet<string>(
                    _selectedCategoryTypeFilters
                        .Select(AchievementCategoryTypeHelper.NormalizeOrDefault)
                        .Where(value => !string.IsNullOrWhiteSpace(value)),
                    StringComparer.OrdinalIgnoreCase);
                filtered = filtered.Where(a =>
                    AchievementCategoryTypeHelper.ParseValues(
                            AchievementCategoryTypeHelper.NormalizeOrDefault(a.CategoryType))
                        .Any(selectedTypeSet.Contains));
            }

            if (_selectedCategoryLabelFilters.Count > 0)
            {
                var selectedCategorySet = new HashSet<string>(
                    _selectedCategoryLabelFilters
                        .Select(AchievementCategoryTypeHelper.NormalizeCategoryOrDefault)
                        .Where(value => !string.IsNullOrWhiteSpace(value)),
                    StringComparer.OrdinalIgnoreCase);
                filtered = filtered.Where(a =>
                    selectedCategorySet.Contains(
                        AchievementCategoryTypeHelper.NormalizeCategoryOrDefault(a.CategoryLabel)));
            }

            if (!string.IsNullOrEmpty(_searchText))
            {
                filtered = filtered.Where(a =>
                    (a.DisplayName?.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (a.Description?.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                var filteredItems = filtered.ToList();
                if (CurrentSortDirection.HasValue)
                {
                    var currentSortDirection = CurrentSortDirection;
                    AchievementSortHelper.TrySortItems(
                        filteredItems,
                        _currentSortPath,
                        currentSortDirection.Value,
                        AchievementSortScope.GameAchievements,
                        ref _currentSortPath,
                        ref currentSortDirection);
                }
                else if (!skipDefaultSort)
                {
                    AchievementSortHelper.ApplyConfiguredDefaultSort(
                        filteredItems,
                        _settings?.Persisted,
                        AchievementSortSurface.SingleGame,
                        AchievementSortScope.GameAchievements,
                        stableOrder: AchievementSortHelper.CreateStableOrderMap(filteredItems));
                }

                _filteredAchievements = filteredItems;
                SyncAchievementsDisplay();
            });
        }

        private void SyncAchievementsDisplay()
        {
            var displayItems = DisplayGridRowLimitHelper.Limit(
                _filteredAchievements,
                _settings?.Persisted?.SingleGameGridMaxRows);
            CollectionHelper.SynchronizeCollection(Achievements, displayItems);
        }

        public void ClearSearch()
        {
            SearchText = string.Empty;
        }

        #region IDisposable

        public void Dispose()
        {
            SaveGridState();

            if (_settings != null)
            {
                _settings.PropertyChanged -= OnSettingsChanged;
                if (_settings.Persisted != null)
                {
                    _settings.Persisted.PropertyChanged -= OnPersistedSettingsChanged;
                }
            }
            _refreshService.GameCacheUpdated -= OnGameCacheUpdated;
            _refreshService.CacheDeltaUpdated -= OnCacheDeltaUpdated;
            _refreshService.RebuildProgress -= OnRebuildProgress;
            if (Timeline != null)
            {
                Timeline.PropertyChanged -= Timeline_PropertyChanged;
            }
        }

        #endregion
    }
}






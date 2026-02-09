using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services.Sidebar;
using PlayniteAchievements.Services;
using PlayniteAchievements.Views.Helpers;
using Playnite.SDK;
using Playnite.SDK.Models;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels
{
    public class SidebarViewModel : ObservableObject, IDisposable
    {
        /// <summary>
        /// Returns true if IgnoreUnplayedGames is enabled in settings.
        /// </summary>
        public bool IgnoreUnplayedGames => _settings?.Persisted?.IgnoreUnplayedGames ?? false;

        private readonly AchievementManager _achievementManager;
        private readonly IPlayniteAPI _playniteApi;
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;

        private readonly SidebarDataBuilder _dataBuilder;

        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _refreshCts;
        private volatile bool _isActive;
        private int _refreshVersion;
        private bool _disposed;

        private readonly HashSet<string> _revealedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private SidebarDataSnapshot _latestSnapshot;

        private readonly object _progressLock = new object();
        private DateTime _lastProgressUpdate = DateTime.MinValue;
        private static readonly TimeSpan ProgressMinInterval = TimeSpan.FromMilliseconds(50);
        private System.Windows.Threading.DispatcherTimer _refreshDebounceTimer;

        private readonly PaginationManager<AchievementDisplayItem> _achievementsPager;
        private readonly PaginationManager<GameOverviewItem> _overviewPager;
        private readonly PaginationManager<RecentAchievementItem> _recentPager;
        private readonly PaginationManager<AchievementDisplayItem> _selectedGameAchievementsPager;

        private List<RecentAchievementItem> _filteredRecentAchievements = new List<RecentAchievementItem>();
        private List<AchievementDisplayItem> _filteredSelectedGameAchievements = new List<AchievementDisplayItem>();
        private List<string> _availableProviders = new List<string>();

        // Sort state tracking for quick reverse
        private string _overviewSortPath;
        private ListSortDirection _overviewSortDirection;
        private string _recentSortPath;
        private ListSortDirection _recentSortDirection;
        private string _selectedGameSortPath;
        private ListSortDirection _selectedGameSortDirection;


        public SidebarViewModel(AchievementManager achievementManager, IPlayniteAPI playniteApi, ILogger logger, PlayniteAchievementsSettings settings)
        {
            _achievementManager = achievementManager ?? throw new ArgumentNullException(nameof(achievementManager));
            _playniteApi = playniteApi;
            _logger = logger;
            _settings = settings;
            _dataBuilder = new SidebarDataBuilder(_achievementManager, _playniteApi, _logger);

            // Initialize debounce timer
            _refreshDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _refreshDebounceTimer.Tick += OnRefreshDebounceTimerTick;

            // Initialize collections
            AllAchievements = new BulkObservableCollection<AchievementDisplayItem>();
            PagedAchievements = new BulkObservableCollection<AchievementDisplayItem>();
            GamesOverview = new BulkObservableCollection<GameOverviewItem>();
            RecentAchievements = new BulkObservableCollection<RecentAchievementItem>();
            SelectedGameAchievements = new BulkObservableCollection<AchievementDisplayItem>();

            // Initialize scan mode options from service (exclude LibrarySelected - context menu only)
            var scanModes = _achievementManager.GetScanModes();
            ScanModes = new ObservableCollection<ScanMode>(scanModes.Where(m => m.Type != ScanModeType.LibrarySelected));

            _achievementsPager = new PaginationManager<AchievementDisplayItem>(
                DefaultPageSize,
                PagedAchievements,
                OnPropertyChanged,
                RaisePaginationChanged,
                useDispatcherInvoke: false,
                propertyNames: new PaginationPropertyNames
                {
                    CurrentPage = nameof(CurrentPage),
                    TotalPages = nameof(TotalPages),
                    CanGoNext = nameof(CanGoNext),
                    CanGoPrevious = nameof(CanGoPrevious),
                    HasMultiplePages = nameof(HasMultiplePages),
                    TotalItems = nameof(FilteredCount)
                });

            _overviewPager = new PaginationManager<GameOverviewItem>(
                OverviewPageSize,
                GamesOverview,
                OnPropertyChanged,
                RaiseOverviewPaginationChanged,
                useDispatcherInvoke: false,
                propertyNames: new PaginationPropertyNames
                {
                    CurrentPage = nameof(OverviewCurrentPage),
                    TotalPages = nameof(OverviewTotalPages),
                    CanGoNext = nameof(CanGoOverviewNext),
                    CanGoPrevious = nameof(CanGoOverviewPrevious),
                    HasMultiplePages = nameof(OverviewHasMultiplePages),
                    TotalItems = nameof(TotalGamesOverview)
                });

            _recentPager = new PaginationManager<RecentAchievementItem>(
                RecentPageSize,
                RecentAchievements,
                OnPropertyChanged,
                RaiseRecentPaginationChanged,
                useDispatcherInvoke: false,
                propertyNames: new PaginationPropertyNames
                {
                    CurrentPage = nameof(RecentCurrentPage),
                    TotalPages = nameof(RecentTotalPages),
                    CanGoNext = nameof(CanGoRecentNext),
                    CanGoPrevious = nameof(CanGoRecentPrevious),
                    HasMultiplePages = nameof(RecentHasMultiplePages),
                    TotalItems = nameof(RecentTotalItems)
                });

            _selectedGameAchievementsPager = new PaginationManager<AchievementDisplayItem>(
                SelectedGameAchievementsPageSize,
                SelectedGameAchievements,
                OnPropertyChanged,
                RaiseSelectedGameAchievementsPaginationChanged,
                useDispatcherInvoke: false,
                propertyNames: new PaginationPropertyNames
                {
                    CurrentPage = nameof(SelectedGameAchievementsCurrentPage),
                    TotalPages = nameof(SelectedGameAchievementsTotalPages),
                    CanGoNext = nameof(CanGoSelectedGameAchievementsNext),
                    CanGoPrevious = nameof(CanGoSelectedGameAchievementsPrevious),
                    HasMultiplePages = nameof(SelectedGameAchievementsHasMultiplePages),
                    TotalItems = nameof(SelectedGameAchievementsTotalItems)
                });

            GlobalTimeline = new TimelineViewModel { EnableDiagnostics = _settings?.Persisted?.EnableDiagnostics == true };
            SelectedGameTimeline = new TimelineViewModel { EnableDiagnostics = _settings?.Persisted?.EnableDiagnostics == true };

            GamesPieChart = new PieChartViewModel();
            RarityPieChart = new PieChartViewModel();
            ProviderPieChart = new PieChartViewModel();

            // Set defaults: Unlocked Only, sorted by Unlock Date
            _showUnlockedOnly = true;
            _sortIndex = 2; // Unlock Date

            // Initialize commands
            RefreshViewCommand = new AsyncCommand(_ => RefreshViewAsync());
            ScanAllCommand = new AsyncCommand(_ => ScanAllAsync(), _ => !IsScanning);
            QuickScanCommand = new AsyncCommand(_ => QuickScanAsync(), _ => !IsScanning);
            CancelScanCommand = new RelayCommand(_ => CancelScan(), _ => IsScanning);
            NextPageCommand = new RelayCommand(_ => GoToNextPage(), _ => CanGoNext);
            PreviousPageCommand = new RelayCommand(_ => GoToPreviousPage(), _ => CanGoPrevious);
            FirstPageCommand = new RelayCommand(_ => GoToFirstPage(), _ => CanGoPrevious);
            LastPageCommand = new RelayCommand(_ => GoToLastPage(), _ => CanGoNext);
            OverviewNextPageCommand = new RelayCommand(_ => GoToOverviewNextPage(), _ => CanGoOverviewNext);
            OverviewPreviousPageCommand = new RelayCommand(_ => GoToOverviewPreviousPage(), _ => CanGoOverviewPrevious);
            OverviewFirstPageCommand = new RelayCommand(_ => GoToOverviewFirstPage(), _ => CanGoOverviewPrevious);
            OverviewLastPageCommand = new RelayCommand(_ => GoToOverviewLastPage(), _ => CanGoOverviewNext);
            RecentNextPageCommand = new RelayCommand(_ => GoToRecentNextPage(), _ => CanGoRecentNext);
            RecentPreviousPageCommand = new RelayCommand(_ => GoToRecentPreviousPage(), _ => CanGoRecentPrevious);
            RecentFirstPageCommand = new RelayCommand(_ => GoToRecentFirstPage(), _ => CanGoRecentPrevious);
            RecentLastPageCommand = new RelayCommand(_ => GoToRecentLastPage(), _ => CanGoRecentNext);
            SelectedGameAchievementsNextPageCommand = new RelayCommand(_ => GoToSelectedGameAchievementsNextPage(), _ => CanGoSelectedGameAchievementsNext);
            SelectedGameAchievementsPreviousPageCommand = new RelayCommand(_ => GoToSelectedGameAchievementsPreviousPage(), _ => CanGoSelectedGameAchievementsPrevious);
            SelectedGameAchievementsFirstPageCommand = new RelayCommand(_ => GoToSelectedGameAchievementsFirstPage(), _ => CanGoSelectedGameAchievementsPrevious);
            SelectedGameAchievementsLastPageCommand = new RelayCommand(_ => GoToSelectedGameAchievementsLastPage(), _ => CanGoSelectedGameAchievementsNext);
            RevealAchievementCommand = new RelayCommand(param => RevealAchievement(param as AchievementDisplayItem));
            CloseViewCommand = new RelayCommand(_ => PlayniteUiProvider.RestoreMainView());
            ClearGameSelectionCommand = new RelayCommand(_ => ClearGameSelection());
            NavigateToGameCommand = new RelayCommand(param => NavigateToGame(param as GameOverviewItem));
            ScanCommand = new AsyncCommand(_ => ExecuteScanAsync(), _ => !IsScanning);

            // Subscribe to progress events
            _achievementManager.RebuildProgress += OnRebuildProgress;
            _achievementManager.CacheInvalidated += OnCacheInvalidated;
            if (_settings != null)
            {
                _settings.PropertyChanged += OnSettingsChanged;
            }

            if (_settings?.Persisted?.IgnoreUnplayedGames == true)
            {
                _hideUnplayedGames = true;
            }
        }

        #region Collections

        public ObservableCollection<AchievementDisplayItem> AllAchievements { get; }
        public ObservableCollection<AchievementDisplayItem> PagedAchievements { get; }

        // Overview tab collections
        public ObservableCollection<GameOverviewItem> GamesOverview { get; }
        public ObservableCollection<RecentAchievementItem> RecentAchievements { get; }

        private List<GameOverviewItem> _allGamesOverview = new List<GameOverviewItem>();
        private List<GameOverviewItem> _filteredGamesOverview = new List<GameOverviewItem>();

        private List<RecentAchievementItem> _allRecentAchievements = new List<RecentAchievementItem>();
        private List<AchievementDisplayItem> _allSelectedGameAchievements = new List<AchievementDisplayItem>();

        #endregion

        #region Overview Tab Properties

        private string _leftSearchText = string.Empty;
        public string LeftSearchText
        {
            get => _leftSearchText;
            set
            {
                if (SetValueAndReturn(ref _leftSearchText, value ?? string.Empty))
                {
                    OverviewCurrentPage = 1;
                    ApplyLeftFilters();
                }
            }
        }

        private string _rightSearchText = string.Empty;
        public string RightSearchText
        {
            get => _rightSearchText;
            set
            {
                if (SetValueAndReturn(ref _rightSearchText, value ?? string.Empty))
                {
                    ApplyRightFilters();
                }
            }
        }

        private ObservableCollection<string> _providerFilterOptions;
        public ObservableCollection<string> ProviderFilterOptions
        {
            get => _providerFilterOptions;
            private set => SetValue(ref _providerFilterOptions, value);
        }

        private string _selectedProviderFilter = "All";
        public string SelectedProviderFilter
        {
            get => _selectedProviderFilter;
            set
            {
                if (SetValueAndReturn(ref _selectedProviderFilter, value))
                {
                    OverviewCurrentPage = 1;
                    ApplyLeftFilters();
                }
            }
        }

        public ObservableCollection<ScanMode> ScanModes { get; }

        private string _selectedScanMode = ScanModeType.Installed.GetKey();
        public string SelectedScanMode
        {
            get => _selectedScanMode;
            set
            {
                if (SetValueAndReturn(ref _selectedScanMode, value))
                {
                    (ScanCommand as AsyncCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _hideGamesWithNoUnlocked = true;
        public bool HideGamesWithNoUnlocked
        {
            get => _hideGamesWithNoUnlocked;
            set
            {
                if (SetValueAndReturn(ref _hideGamesWithNoUnlocked, value))
                {
                    OverviewCurrentPage = 1;
                    ApplyLeftFilters();
                }
            }
        }

        private bool _hideUnplayedGames = true;
        public bool HideUnplayedGames
        {
            get => _hideUnplayedGames;
            set
            {
                if (SetValueAndReturn(ref _hideUnplayedGames, value))
                {
                    OverviewCurrentPage = 1;
                    ApplyLeftFilters();
                }
            }
        }

        public bool UseCoverImages => _settings?.Persisted?.UseCoverImages ?? false;

        private int _totalGamesOverview;
        public int TotalGamesOverview
        {
            get => _totalGamesOverview;
            private set => SetValue(ref _totalGamesOverview, value);
        }

        private int _totalAchievementsOverview;
        public int TotalAchievementsOverview
        {
            get => _totalAchievementsOverview;
            private set => SetValue(ref _totalAchievementsOverview, value);
        }

        private int _totalUnlockedOverview;
        public int TotalUnlockedOverview
        {
            get => _totalUnlockedOverview;
            private set => SetValue(ref _totalUnlockedOverview, value);
        }

        private int _perfectGames;
        public int PerfectGames
        {
            get => _perfectGames;
            private set => SetValue(ref _perfectGames, value);
        }

        private int _totalCommon;
        public int TotalCommon
        {
            get => _totalCommon;
            private set => SetValue(ref _totalCommon, value);
        }

        private int _totalUncommon;
        public int TotalUncommon
        {
            get => _totalUncommon;
            private set => SetValue(ref _totalUncommon, value);
        }

        private int _totalRare;
        public int TotalRare
        {
            get => _totalRare;
            private set => SetValue(ref _totalRare, value);
        }

        private int _totalUltraRare;
        public int TotalUltraRare
        {
            get => _totalUltraRare;
            private set => SetValue(ref _totalUltraRare, value);
        }

        private double _globalProgression;
        public double GlobalProgression
        {
            get => _globalProgression;
            private set => SetValue(ref _globalProgression, value);
        }

        private GameOverviewItem _selectedGame;
        public GameOverviewItem SelectedGame
        {
            get => _selectedGame;
            set
            {
                if (SetValueAndReturn(ref _selectedGame, value))
                {
                    OnPropertyChanged(nameof(IsGameSelected));
                    LoadSelectedGameAchievements();
                }
            }
        }

        public bool IsGameSelected => SelectedGame != null;

        public ObservableCollection<AchievementDisplayItem> SelectedGameAchievements { get; }

        public ObservableCollection<ChartDataPoint> SelectedGameDailyUnlocks { get; } = new ObservableCollection<ChartDataPoint>();

        #endregion

        #region Timeline Properties

        public TimelineViewModel GlobalTimeline { get; private set; }
        public TimelineViewModel SelectedGameTimeline { get; private set; }

        public PieChartViewModel GamesPieChart { get; private set; }
        public PieChartViewModel RarityPieChart { get; private set; }
        public PieChartViewModel ProviderPieChart { get; private set; }

        // Rarity percentage properties for distribution bars
        public double CommonPercentage => TotalUnlockedOverview > 0
            ? (double)TotalCommon / TotalUnlockedOverview * 100 : 0;

        public double UncommonPercentage => TotalUnlockedOverview > 0
            ? (double)TotalUncommon / TotalUnlockedOverview * 100 : 0;

        public double RarePercentage => TotalUnlockedOverview > 0
            ? (double)TotalRare / TotalUnlockedOverview * 100 : 0;

        public double UltraRarePercentage => TotalUnlockedOverview > 0
            ? (double)TotalUltraRare / TotalUnlockedOverview * 100 : 0;

        #endregion

        #region Pagination Properties

        private const int DefaultPageSize = 100;

        public int CurrentPage
        {
            get => _achievementsPager.CurrentPage;
            set => _achievementsPager.CurrentPage = value;
        }

        public int TotalPages => _achievementsPager.TotalPages;

        public int FilteredCount => _achievementsPager.TotalItems;

        public bool CanGoNext => _achievementsPager.CanGoNext;
        public bool CanGoPrevious => _achievementsPager.CanGoPrevious;
        public bool HasMultiplePages => _achievementsPager.HasMultiplePages;

        public string PageInfo => string.Format(
            Playnite.SDK.ResourceProvider.GetString("LOCPlayAch_Achievements_PageInfo"),
            CurrentPage,
            TotalPages);

        #endregion

        #region Overview Pagination Properties

        private const int OverviewPageSize = 100;

        public int OverviewCurrentPage
        {
            get => _overviewPager.CurrentPage;
            set => _overviewPager.CurrentPage = value;
        }

        public int OverviewTotalPages => _overviewPager.TotalPages;

        public bool CanGoOverviewNext => _overviewPager.CanGoNext;
        public bool CanGoOverviewPrevious => _overviewPager.CanGoPrevious;

        public bool OverviewHasMultiplePages => _overviewPager.HasMultiplePages;

        public string OverviewPageInfo => string.Format(
            Playnite.SDK.ResourceProvider.GetString("LOCPlayAch_Achievements_PageInfo"),
            OverviewCurrentPage,
            OverviewTotalPages);

        #endregion

        #region Recent Achievements Pagination Properties

        private const int RecentPageSize = 100;

        public int RecentCurrentPage
        {
            get => _recentPager.CurrentPage;
            set => _recentPager.CurrentPage = value;
        }

        public int RecentTotalPages => _recentPager.TotalPages;

        public int RecentTotalItems => _recentPager.TotalItems;

        public bool CanGoRecentNext => _recentPager.CanGoNext;
        public bool CanGoRecentPrevious => _recentPager.CanGoPrevious;

        public bool RecentHasMultiplePages => _recentPager.HasMultiplePages;

        public string RecentPageInfo => string.Format(
            Playnite.SDK.ResourceProvider.GetString("LOCPlayAch_Achievements_PageInfo"),
            RecentCurrentPage,
            RecentTotalPages);

        #endregion

        #region Selected Game Achievements Pagination Properties

        private const int SelectedGameAchievementsPageSize = 100;

        public int SelectedGameAchievementsCurrentPage
        {
            get => _selectedGameAchievementsPager.CurrentPage;
            set => _selectedGameAchievementsPager.CurrentPage = value;
        }

        public int SelectedGameAchievementsTotalPages => _selectedGameAchievementsPager.TotalPages;

        public int SelectedGameAchievementsTotalItems => _selectedGameAchievementsPager.TotalItems;

        public bool CanGoSelectedGameAchievementsNext => _selectedGameAchievementsPager.CanGoNext;
        public bool CanGoSelectedGameAchievementsPrevious => _selectedGameAchievementsPager.CanGoPrevious;

        public bool SelectedGameAchievementsHasMultiplePages => _selectedGameAchievementsPager.HasMultiplePages;

        public string SelectedGameAchievementsPageInfo => string.Format(
            ResourceProvider.GetString("LOCPlayAch_Achievements_PageInfo"),
            SelectedGameAchievementsCurrentPage,
            SelectedGameAchievementsTotalPages);

        #endregion

        #region Progress Properties

        public bool IsScanning => _achievementManager.IsRebuilding;

        private double _progressPercent;
        public double ProgressPercent
        {
            get => _progressPercent;
            set => SetValue(ref _progressPercent, value);
        }

        private string _progressMessage;
        public string ProgressMessage
        {
            get => _progressMessage;
            set => SetValue(ref _progressMessage, value);
        }

        public bool ShowProgress => IsScanning;

        #endregion

        #region Filter Properties

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetValueAndReturn(ref _searchText, value ?? string.Empty))
                {
                    CurrentPage = 1;
                    RefreshFilter();
                }
            }
        }

        private bool _showUnlockedOnly;
        public bool ShowUnlockedOnly
        {
            get => _showUnlockedOnly;
            set
            {
                if (SetValueAndReturn(ref _showUnlockedOnly, value))
                {
                    if (value) _showLockedOnly = false;
                    OnPropertyChanged(nameof(ShowLockedOnly));
                    CurrentPage = 1;
                    RefreshFilter();
                }
            }
        }

        private bool _showLockedOnly;
        public bool ShowLockedOnly
        {
            get => _showLockedOnly;
            set
            {
                if (SetValueAndReturn(ref _showLockedOnly, value))
                {
                    if (value) _showUnlockedOnly = false;
                    OnPropertyChanged(nameof(ShowUnlockedOnly));
                    CurrentPage = 1;
                    RefreshFilter();
                }
            }
        }

        private int _sortIndex = 2; // Default to Unlock Date
        public int SortIndex
        {
            get => _sortIndex;
            set
            {
                if (SetValueAndReturn(ref _sortIndex, value))
                {
                    CurrentPage = 1;
                    RefreshFilter();
                }
            }
        }

        #endregion

        #region Status Properties

        private string _statusText;
        public string StatusText
        {
            get => _statusText;
            set => SetValue(ref _statusText, value);
        }

        private int _totalCount;
        private int _unlockedCount;
        private int _gamesCount;

        #endregion

        #region Commands

        public ICommand RefreshViewCommand { get; }
        public ICommand ScanAllCommand { get; }
        public ICommand QuickScanCommand { get; }
        public ICommand CancelScanCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand LastPageCommand { get; }
        public ICommand OverviewNextPageCommand { get; }
        public ICommand OverviewPreviousPageCommand { get; }
        public ICommand OverviewFirstPageCommand { get; }
        public ICommand OverviewLastPageCommand { get; }
        public ICommand RecentNextPageCommand { get; }
        public ICommand RecentPreviousPageCommand { get; }
        public ICommand RecentFirstPageCommand { get; }
        public ICommand RecentLastPageCommand { get; }
        public ICommand SelectedGameAchievementsNextPageCommand { get; }
        public ICommand SelectedGameAchievementsPreviousPageCommand { get; }
        public ICommand SelectedGameAchievementsFirstPageCommand { get; }
        public ICommand SelectedGameAchievementsLastPageCommand { get; }
        public ICommand RevealAchievementCommand { get; }
        public ICommand CloseViewCommand { get; }
        public ICommand ClearGameSelectionCommand { get; }
        public ICommand NavigateToGameCommand { get; }
        public ICommand ScanCommand { get; }

        #endregion

        #region Public Methods

        public void SetActive(bool isActive)
        {
            _isActive = isActive;
            if (!isActive)
            {
                CancelPendingRefresh();
            }
            else
            {
                // Restore progress UI state from AchievementManager
                if (IsScanning)
                {
                    var lastReport = _achievementManager.GetLastRebuildProgress();
                    if (lastReport != null)
                    {
                        ProgressPercent = CalculatePercent(lastReport);
                        ProgressMessage = lastReport.Message ?? string.Empty;
                    }
                }

                // Ensure progress UI shows correct state
                OnPropertyChanged(nameof(IsScanning));
                OnPropertyChanged(nameof(ShowProgress));
                RaiseCommandsChanged();
            }
        }

        public async System.Threading.Tasks.Task RefreshViewAsync()
        {
            if (!_isActive)
            {
                return;
            }

            // Ensure the UI gets a chance to paint before we begin heavy work.
            await Task.Yield();

            var version = Interlocked.Increment(ref _refreshVersion);

            var newCts = new CancellationTokenSource();
            var oldCts = Interlocked.Exchange(ref _refreshCts, newCts);
            try { oldCts?.Cancel(); } catch { }
            try { oldCts?.Dispose(); } catch { }

            var cancel = newCts.Token;

            try
            {
                StatusText = ResourceProvider.GetString("LOCPlayAch_Status_LoadingAchievements");

                await _refreshLock.WaitAsync(cancel).ConfigureAwait(false);
                try
                {
                    HashSet<string> revealedCopy;
                    lock (_revealedKeys)
                    {
                        revealedCopy = new HashSet<string>(_revealedKeys, StringComparer.OrdinalIgnoreCase);
                    }
                    var diagnostics = _settings?.Persisted?.EnableDiagnostics == true;

                    SidebarDataSnapshot snapshot;
                    using (PerfTrace.Measure("SidebarViewModel.BuildSnapshot", _logger, diagnostics))
                    {
                        snapshot = await Task.Run(
                            () => _dataBuilder.Build(_settings, revealedCopy, cancel),
                            cancel).ConfigureAwait(false);
                    }

                    System.Windows.Application.Current?.Dispatcher?.InvokeIfNeeded(() =>
                    {
                        if (_disposed || !_isActive)
                        {
                            return;
                        }

                        if (version != _refreshVersion)
                        {
                            return;
                        }

                        using (PerfTrace.Measure("SidebarViewModel.ApplySnapshot", _logger, diagnostics))
                        {
                            ApplySnapshot(snapshot);
                        }
                    });
                }
                finally
                {
                    _refreshLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when new refresh starts.
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to refresh sidebar achievements");
                StatusText = string.Format(ResourceProvider.GetString("LOCPlayAch_Error_ScanFailed"), ex.Message);
            }
        }

        public async System.Threading.Tasks.Task ScanAllAsync()
        {
            if (IsScanning) return;

            try
            {
                ProgressPercent = 0;
                ProgressMessage = ResourceProvider.GetString("LOCPlayAch_Status_Starting");

                await _achievementManager.StartManagedRebuildAsync();
                await RefreshViewAsync();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Scan all failed");
                StatusText = string.Format(ResourceProvider.GetString("LOCPlayAch_Error_ScanFailed"), ex.Message);
            }
            finally
            {
                ProgressPercent = 0;
            }
        }

        public async System.Threading.Tasks.Task QuickScanAsync()
        {
            if (IsScanning) return;

            try
            {
                ProgressPercent = 0;
                ProgressMessage = ResourceProvider.GetString("LOCPlayAch_Status_Starting");

                await _achievementManager.StartManagedQuickRefreshAsync();
                await RefreshViewAsync();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Quick scan failed");
                StatusText = string.Format(ResourceProvider.GetString("LOCPlayAch_Error_QuickScanFailed"), ex.Message);
            }
            finally
            {
                ProgressPercent = 0;
            }
        }

        public void CancelScan()
        {
            _achievementManager.CancelCurrentRebuild();
        }

        public void ClearSearch()
        {
            SearchText = string.Empty;
        }

        public void ClearLeftSearch()
        {
            LeftSearchText = string.Empty;
        }

        public void ClearRightSearch()
        {
            RightSearchText = string.Empty;
        }

        public async System.Threading.Tasks.Task ExecuteScanAsync()
        {
            if (IsScanning) return;

            try
            {
                ProgressPercent = 0;
                ProgressMessage = ResourceProvider.GetString("LOCPlayAch_Status_Starting");

                Guid? singleGameId = null;
                if (SelectedScanMode == ScanModeType.Single.GetKey())
                {
                    if (SelectedGame?.PlayniteGameId.HasValue == true)
                    {
                        singleGameId = SelectedGame.PlayniteGameId.Value;
                    }
                    else
                    {
                        StatusText = "No game selected in the overview.";
                        return;
                    }
                }

                await _achievementManager.ExecuteScanAsync(SelectedScanMode, singleGameId);
                await RefreshViewAsync();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"{SelectedScanMode} scan failed");
                StatusText = string.Format(ResourceProvider.GetString("LOCPlayAch_Error_ScanFailed"), ex.Message);
            }
            finally
            {
                ProgressPercent = 0;
            }
        }

        private void NavigateToGame(GameOverviewItem game)
        {
            if (game == null || !game.PlayniteGameId.HasValue) return;

            try
            {
                PlayniteUiProvider.RestoreMainView();
                _playniteApi.MainView.SelectGame(game.PlayniteGameId.Value);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to navigate to game {game.GameName}");
            }
        }

        #endregion

        #region Private Methods

        private void CancelPendingRefresh()
        {
            var cts = Interlocked.Exchange(ref _refreshCts, null);
            try { cts?.Cancel(); } catch { }
            try { cts?.Dispose(); } catch { }
        }

        private void ApplySnapshot(SidebarDataSnapshot snapshot)
        {
            if (_disposed)
            {
                return;
            }

            _latestSnapshot = snapshot;

            if (AllAchievements is BulkObservableCollection<AchievementDisplayItem> bulkAll)
            {
                bulkAll.ReplaceAll(snapshot.Achievements);
            }
            else
            {
                CollectionHelper.SynchronizeCollection(AllAchievements, snapshot.Achievements);
            }

            _allGamesOverview = snapshot.GamesOverview ?? new List<GameOverviewItem>();
            _allRecentAchievements = snapshot.RecentAchievements ?? new List<RecentAchievementItem>();

            // Build provider filter options from unique providers
            var providers = _allGamesOverview
                .Select(g => g.Provider)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();
            var providerOptions = new List<string> { "All" };
            providerOptions.AddRange(providers);
            ProviderFilterOptions = new ObservableCollection<string>(providerOptions);

            // Initialize filtered lists
            _filteredRecentAchievements = new List<RecentAchievementItem>(_allRecentAchievements);
            _filteredSelectedGameAchievements = new List<AchievementDisplayItem>();

            _totalCount = snapshot.TotalAchievements;
            _unlockedCount = snapshot.TotalUnlocked;
            _gamesCount = snapshot.TotalGames;

            TotalGamesOverview = snapshot.TotalGames;
            TotalAchievementsOverview = snapshot.TotalAchievements;
            TotalUnlockedOverview = snapshot.TotalUnlocked;
            TotalCommon = snapshot.TotalCommon;
            TotalUncommon = snapshot.TotalUncommon;
            TotalRare = snapshot.TotalRare;
            TotalUltraRare = snapshot.TotalUltraRare;
            PerfectGames = snapshot.PerfectGames;
            GlobalProgression = snapshot.GlobalProgressionPercent;

            // Build provider lookup dictionary from AchievementManager
            var providerLookup = new Dictionary<string, (string iconKey, string colorHex)>(StringComparer.OrdinalIgnoreCase);
            foreach (var provider in _achievementManager.GetProviders())
            {
                providerLookup[provider.ProviderName] = (provider.ProviderIconKey, provider.ProviderColorHex);
            }

            // Get localized strings
            var perfectLabel = ResourceProvider.GetString("LOCPlayAch_Perfect");
            var incompleteLabel = ResourceProvider.GetString("LOCPlayAch_Sidebar_Incomplete");
            var commonLabel = ResourceProvider.GetString("LOCPlayAch_Rarity_Common");
            var uncommonLabel = ResourceProvider.GetString("LOCPlayAch_Rarity_Uncommon");
            var rareLabel = ResourceProvider.GetString("LOCPlayAch_Rarity_Rare");
            var ultraRareLabel = ResourceProvider.GetString("LOCPlayAch_Rarity_UltraRare");
            var lockedLabel = ResourceProvider.GetString("LOCPlayAch_Sidebar_Locked");

            // Update charts
            ProviderPieChart.SetProviderData(snapshot.UnlockedByProvider, snapshot.TotalLocked, lockedLabel, providerLookup);

            GamesPieChart.SetGameData(snapshot.TotalGames, snapshot.PerfectGames, perfectLabel, incompleteLabel);
            RarityPieChart.SetRarityData(
                snapshot.TotalCommon,
                snapshot.TotalUncommon,
                snapshot.TotalRare,
                snapshot.TotalUltraRare,
                snapshot.TotalLocked,
                commonLabel,
                uncommonLabel,
                rareLabel,
                ultraRareLabel,
                lockedLabel
            );

            OnPropertyChanged(nameof(CommonPercentage));
            OnPropertyChanged(nameof(UncommonPercentage));
            OnPropertyChanged(nameof(RarePercentage));
            OnPropertyChanged(nameof(UltraRarePercentage));

            GlobalTimeline.SetCounts(snapshot.GlobalUnlockCountsByDate);

            if (SelectedGame?.PlayniteGameId.HasValue == true &&
                snapshot.UnlockCountsByDateByGame != null &&
                snapshot.UnlockCountsByDateByGame.TryGetValue(SelectedGame.PlayniteGameId.Value, out var selectedCounts))
            {
                SelectedGameTimeline.SetCounts(selectedCounts);
            }
            else
            {
                SelectedGameTimeline.SetCounts(null);
            }

            RefreshFilter();
            ApplyLeftFilters();

            _recentPager.SetSourceItems(_filteredRecentAchievements);
            _recentPager.GoToFirstPage();
            UpdateFilteredStatus();
        }

        private static string MakeRevealKey(Guid? playniteGameId, string apiName, string gameName)
        {
            var gamePart = playniteGameId?.ToString() ?? (gameName ?? string.Empty);
            return $"{gamePart}\u001f{apiName ?? string.Empty}";
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Persisted.UseCoverImages")
            {
                OnPropertyChanged(nameof(UseCoverImages));
            }
            else if (e.PropertyName == "Persisted.IgnoreUnplayedGames")
            {
                OnPropertyChanged(nameof(IgnoreUnplayedGames));
                if (IgnoreUnplayedGames)
                {
                    HideUnplayedGames = true;
                }
            }
        }

        private void RevealAchievement(AchievementDisplayItem item)
        {
            if (item == null)
            {
                return;
            }

            var key = MakeRevealKey(item.PlayniteGameId, item.ApiName, item.GameName);

            item.ToggleReveal();

            lock (_revealedKeys)
            {
                if (item.IsRevealed)
                {
                    _revealedKeys.Add(key);
                }
                else
                {
                    _revealedKeys.Remove(key);
                }
            }
        }

        private void OnRebuildProgress(object sender, ProgressReport report)
        {
            if (report == null) return;

            var now = DateTime.UtcNow;

            lock (_progressLock)
            {
                var pct = CalculatePercent(report);
                var isFinal = pct >= 100 || report.IsCanceled || (report.TotalSteps > 0 && report.CurrentStep >= report.TotalSteps);

                if (!isFinal && (now - _lastProgressUpdate) < ProgressMinInterval)
                {
                    return;
                }

                _lastProgressUpdate = now;
            }

            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                try
                {
                    OnPropertyChanged(nameof(IsScanning));
                    OnPropertyChanged(nameof(ShowProgress));
                    RaiseCommandsChanged();
                    ProgressPercent = CalculatePercent(report);
                    ProgressMessage = report.Message ?? string.Empty;
                }
                catch (Exception ex)
                {
                    _logger?.Debug($"Progress UI update error: {ex.Message}");
                }
            }));
        }

        private void OnCacheInvalidated(object sender, EventArgs e)
        {
            if (!_isActive)
            {
                return;
            }

            System.Windows.Application.Current?.Dispatcher?.InvokeIfNeeded(() =>
            {
                _refreshDebounceTimer.Stop();
                _refreshDebounceTimer.Start();
            });
        }

        private async void OnRefreshDebounceTimerTick(object sender, EventArgs e)
        {
            _refreshDebounceTimer.Stop();
            try
            {
                await RefreshViewAsync();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to auto-refresh sidebar on cache change");
            }
        }

        private static double CalculatePercent(ProgressReport report)
        {
            if (report == null) return 0;

            var pct = report.PercentComplete;
            if ((pct <= 0 || double.IsNaN(pct)) && report.TotalSteps > 0)
            {
                pct = Math.Max(0, Math.Min(100, (report.CurrentStep * 100.0) / report.TotalSteps));
            }
            return pct;
        }

        private bool FilterAchievement(AchievementDisplayItem item)
        {
            if (item == null) return false;

            // Search filter
            if (!string.IsNullOrEmpty(SearchText))
            {
                var matchesSearch =
                    (item.GameName?.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (item.DisplayName?.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (item.Description?.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);

                if (!matchesSearch) return false;
            }

            // Unlocked/Locked filters
            if (ShowUnlockedOnly && !item.Unlocked) return false;
            if (ShowLockedOnly && item.Unlocked) return false;

            return true;
        }

        public void RefreshFilter()
        {
            var diagnostics = _settings?.Persisted?.EnableDiagnostics == true;
            using (PerfTrace.Measure("SidebarViewModel.RefreshFilter", _logger, diagnostics))
            {
                var filtered = ApplySort(AllAchievements.Where(FilterAchievement)).ToList();
                _achievementsPager.SetSourceItems(filtered);

                OnPropertyChanged(nameof(PageInfo));
                UpdateFilteredStatus();
            }
        }

        private IEnumerable<AchievementDisplayItem> ApplySort(IEnumerable<AchievementDisplayItem> items)
        {
            switch (SortIndex)
            {
                case 0: // Game Name
                    return items.OrderBy(a => a.GameName).ThenBy(a => a.DisplayName);
                case 1: // Achievement Name
                    return items.OrderBy(a => a.DisplayName);
                case 2: // Unlock Date (most recent first)
                    return items.OrderByDescending(a => a.UnlockTimeUtc ?? DateTime.MinValue);
                case 3: // Rarity (rarest first)
                    return items.OrderBy(a => a.GlobalPercentUnlocked ?? 100);
                default:
                    return items;
            }
        }



        private void GoToNextPage()
        {
            if (CanGoNext) CurrentPage++;
        }

        private void GoToPreviousPage()
        {
            if (CanGoPrevious) CurrentPage--;
        }

        private void GoToFirstPage()
        {
            CurrentPage = 1;
        }

        private void GoToLastPage()
        {
            CurrentPage = TotalPages;
        }

        private void GoToOverviewNextPage()
        {
            if (CanGoOverviewNext) OverviewCurrentPage++;
        }

        private void GoToOverviewPreviousPage()
        {
            if (CanGoOverviewPrevious) OverviewCurrentPage--;
        }

        private void GoToOverviewFirstPage()
        {
            OverviewCurrentPage = 1;
        }

        private void GoToOverviewLastPage()
        {
            OverviewCurrentPage = OverviewTotalPages;
        }

        private void GoToRecentNextPage()
        {
            if (CanGoRecentNext) RecentCurrentPage++;
        }

        private void GoToRecentPreviousPage()
        {
            if (CanGoRecentPrevious) RecentCurrentPage--;
        }

        private void GoToRecentFirstPage()
        {
            RecentCurrentPage = 1;
        }

        private void GoToRecentLastPage()
        {
            RecentCurrentPage = RecentTotalPages;
        }

        private void GoToSelectedGameAchievementsNextPage()
        {
            if (CanGoSelectedGameAchievementsNext) SelectedGameAchievementsCurrentPage++;
        }

        private void GoToSelectedGameAchievementsPreviousPage()
        {
            if (CanGoSelectedGameAchievementsPrevious) SelectedGameAchievementsCurrentPage--;
        }

        private void GoToSelectedGameAchievementsFirstPage()
        {
            SelectedGameAchievementsCurrentPage = 1;
        }

        private void GoToSelectedGameAchievementsLastPage()
        {
            SelectedGameAchievementsCurrentPage = SelectedGameAchievementsTotalPages;
        }

        private void RaisePaginationChanged()
        {
            OnPropertyChanged(nameof(PageInfo));
            OnPropertyChanged(nameof(HasMultiplePages));
            (NextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FirstPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (LastPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void RaiseOverviewPaginationChanged()
        {
            OnPropertyChanged(nameof(OverviewPageInfo));
            OnPropertyChanged(nameof(OverviewHasMultiplePages));
            (OverviewNextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OverviewPreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OverviewFirstPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OverviewLastPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void RaiseRecentPaginationChanged()
        {
            OnPropertyChanged(nameof(RecentPageInfo));
            OnPropertyChanged(nameof(RecentHasMultiplePages));
            (RecentNextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RecentPreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RecentFirstPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RecentLastPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void RaiseSelectedGameAchievementsPaginationChanged()
        {
            OnPropertyChanged(nameof(SelectedGameAchievementsPageInfo));
            OnPropertyChanged(nameof(SelectedGameAchievementsHasMultiplePages));
            (SelectedGameAchievementsNextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SelectedGameAchievementsPreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SelectedGameAchievementsFirstPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SelectedGameAchievementsLastPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void UpdateStats()
        {
            _totalCount = AllAchievements.Count;
            _unlockedCount = AllAchievements.Count(a => a.Unlocked);
            _gamesCount = AllAchievements.Select(a => a.GameName).Distinct().Count();

            UpdateFilteredStatus();
        }

        private void UpdateFilteredStatus()
        {
            if (_totalCount == 0)
            {
                StatusText = ResourceProvider.GetString("LOCPlayAch_Status_NoAchievementsCached");
            }
            else if (FilteredCount < _totalCount)
            {
                StatusText = string.Format(ResourceProvider.GetString("LOCPlayAch_Status_FilteredCounts"), FilteredCount, _totalCount, _unlockedCount, _gamesCount);
            }
            else
            {
                StatusText = string.Format(ResourceProvider.GetString("LOCPlayAch_Status_TotalCounts"), _totalCount, _unlockedCount, _gamesCount);
            }
        }

        private void UpdateGameInView(string gameId)
        {
            // This method is intentionally left empty.
            // The debounced OnCacheInvalidated handler now manages all UI refreshes
            // to prevent flickering from partial or redundant updates.
        }

        private void RecalculateOverviewStats()
        {
            // This now calculates from the filtered view
            var sourceList = _filteredGamesOverview;

            TotalGamesOverview = sourceList.Count;
            TotalAchievementsOverview = sourceList.Sum(g => g.TotalAchievements);
            TotalUnlockedOverview = sourceList.Sum(g => g.UnlockedAchievements);
            TotalCommon = sourceList.Sum(g => g.CommonCount);
            TotalUncommon = sourceList.Sum(g => g.UncommonCount);
            TotalRare = sourceList.Sum(g => g.RareCount);
            TotalUltraRare = sourceList.Sum(g => g.UltraRareCount);
            PerfectGames = sourceList.Count(g => g.IsPerfect);

            GlobalProgression = TotalAchievementsOverview > 0 ? (double)TotalUnlockedOverview / TotalAchievementsOverview * 100 : 0;
        }

        private void RaiseCommandsChanged()
        {
            (ScanAllCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (QuickScanCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (CancelScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (NextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FirstPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (LastPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        #endregion

        #region Overview Methods

        // LoadOverviewData removed: overview and recent lists are built via SidebarDataBuilder snapshots.

        private void ApplyLeftFilters()
        {
            // Preserve selection across filter updates
            Guid? selectedGameId = SelectedGame?.PlayniteGameId;

            var filtered = _allGamesOverview.AsEnumerable();

            // Search filter
            if (!string.IsNullOrEmpty(LeftSearchText))
            {
                filtered = filtered.Where(g =>
                    g.GameName?.IndexOf(LeftSearchText, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            // Provider filter
            if (SelectedProviderFilter != "All")
            {
                filtered = filtered.Where(g => g.Provider == SelectedProviderFilter);
            }

            // Hide games with no unlocked
            if (HideGamesWithNoUnlocked)
            {
                filtered = filtered.Where(g => g.UnlockedAchievements > 0);
            }

            // Hide unplayed games, but show them if they have achievements
            if (HideUnplayedGames)
            {
                filtered = filtered.Where(g => g.LastPlayed.HasValue || g.UnlockedAchievements > 0);
            }

            _filteredGamesOverview = filtered.ToList();
            _overviewPager.SetSourceItems(_filteredGamesOverview);
            RecalculateOverviewStats();

            // Restore selection by finding the game with matching PlayniteGameId
            if (selectedGameId.HasValue)
            {
                var restored = GamesOverview.FirstOrDefault(g => g.PlayniteGameId == selectedGameId.Value);
                if (restored != null)
                {
                    SelectedGame = restored;
                }
            }
        }

        private void ApplyRightFilters()
        {
            // Contextually filter based on IsGameSelected
            if (IsGameSelected)
            {
                // Filter SelectedGameAchievements
                var filtered = _allSelectedGameAchievements.AsEnumerable();
                if (!string.IsNullOrEmpty(RightSearchText))
                {
                    filtered = filtered.Where(a =>
                        (a.DisplayName?.IndexOf(RightSearchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (a.Description?.IndexOf(RightSearchText, StringComparison.OrdinalIgnoreCase) >= 0));
                }
                _filteredSelectedGameAchievements = filtered.ToList();
                _selectedGameAchievementsPager.SetSourceItems(_filteredSelectedGameAchievements);
            }
            else
            {
                // Filter RecentAchievements
                var filtered = _allRecentAchievements.AsEnumerable();
                if (!string.IsNullOrEmpty(RightSearchText))
                {
                    filtered = filtered.Where(r =>
                        (r.GameName?.IndexOf(RightSearchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (r.Name?.IndexOf(RightSearchText, StringComparison.OrdinalIgnoreCase) >= 0));
                }
                _filteredRecentAchievements = filtered.ToList();
                _recentPager.SetSourceItems(_filteredRecentAchievements);
            }
        }

        private async void LoadSelectedGameAchievements()
        {
            // Reset right search when selecting a game
            RightSearchText = string.Empty;

            if (SelectedGame == null)
            {
                _allSelectedGameAchievements = new List<AchievementDisplayItem>();
                _filteredSelectedGameAchievements = new List<AchievementDisplayItem>();
                _selectedGameAchievementsPager.SetSourceItems(_allSelectedGameAchievements);
                _selectedGameAchievementsPager.GoToFirstPage();
                // Restore global timeline to show all games
                GlobalTimeline.SetCounts(_latestSnapshot?.GlobalUnlockCountsByDate);
                return;
            }

            try
            {
                if (!SelectedGame.PlayniteGameId.HasValue)
                    return;

                var gameId = SelectedGame.PlayniteGameId.Value;
                var hideLocked = _settings?.Persisted?.HideAchievementsLockedForSelf ?? false;
                HashSet<string> revealedCopy;
                lock (_revealedKeys)
                {
                    revealedCopy = new HashSet<string>(_revealedKeys, StringComparer.OrdinalIgnoreCase);
                }

                (List<AchievementDisplayItem> items, Dictionary<DateTime, int> counts) result = await Task.Run(() =>
                {
                    var counts = new Dictionary<DateTime, int>();

                    var gameData = _achievementManager.GetGameAchievementData(gameId);
                    if (gameData == null || gameData.Achievements == null)
                    {
                        return (new List<AchievementDisplayItem>(), counts);
                    }

                    var achievements = new List<AchievementDisplayItem>(gameData.Achievements.Count);
                    foreach (var ach in gameData.Achievements)
                    {
                        var item = new AchievementDisplayItem
                        {
                            GameName = gameData.GameName ?? "Unknown",
                            PlayniteGameId = gameId,
                            DisplayName = ach.DisplayName ?? ach.ApiName ?? "Unknown",
                            Description = ach.Description ?? string.Empty,
                            IconPath = ach.IconPath,
                            UnlockTimeUtc = ach.UnlockTimeUtc,
                            GlobalPercentUnlocked = ach.GlobalPercentUnlocked,
                            Unlocked = ach.Unlocked,
                            Hidden = ach.Hidden,
                            ApiName = ach.ApiName,
                            HideAchievementsLockedForSelf = hideLocked,
                            ProgressNum = ach.ProgressNum,
                            ProgressDenom = ach.ProgressDenom
                        };

                        item.IsRevealed = revealedCopy.Contains(MakeRevealKey(gameId, ach.ApiName, gameData.GameName));
                        achievements.Add(item);

                        if (ach.Unlocked && ach.UnlockTimeUtc.HasValue)
                        {
                            var date = DateTimeUtilities.AsUtcKind(ach.UnlockTimeUtc.Value).Date;
                            if (counts.TryGetValue(date, out var existing))
                            {
                                counts[date] = existing + 1;
                            }
                            else
                            {
                                counts[date] = 1;
                            }
                        }
                    }

                    var sorted = achievements
                        .OrderByDescending(a => a.Unlocked)
                        .ThenByDescending(a => a.UnlockTimeUtc ?? DateTime.MinValue)
                        .ThenBy(a => a.GlobalPercentUnlocked ?? 100)
                        .ToList();

                    return (sorted, counts);
                }).ConfigureAwait(true);

                _allSelectedGameAchievements = result.items;
                _filteredSelectedGameAchievements = new List<AchievementDisplayItem>(_allSelectedGameAchievements);
                _selectedGameAchievementsPager.SetSourceItems(_filteredSelectedGameAchievements);
                _selectedGameAchievementsPager.GoToFirstPage();

                // Filter global timeline to show only this game's achievements
                if (_latestSnapshot?.UnlockCountsByDateByGame != null &&
                    _latestSnapshot.UnlockCountsByDateByGame.TryGetValue(gameId, out var snapshotCounts))
                {
                    GlobalTimeline.SetCounts(snapshotCounts);
                }
                else
                {
                    GlobalTimeline.SetCounts(result.counts);
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to load achievements for game {SelectedGame?.AppId}");
            }
        }

        public void ClearGameSelection()
        {
            // Reset right search when clearing selection
            RightSearchText = string.Empty;
            SelectedGame = null;
        }

        #endregion

        public void SortDataGrid(DataGrid dataGrid, string sortMemberPath, ListSortDirection direction)
        {
            if (dataGrid == null || string.IsNullOrEmpty(sortMemberPath)) return;

            // Identify which DataGrid is being sorted by checking ItemsSource
            var itemsSource = dataGrid.ItemsSource;

            if (itemsSource == GamesOverview)
            {
                SortGamesOverview(sortMemberPath, direction);
            }
            else if (itemsSource == RecentAchievements)
            {
                SortRecentAchievements(sortMemberPath, direction);
            }
            else if (itemsSource == SelectedGameAchievements)
            {
                SortSelectedGameAchievements(sortMemberPath, direction);
            }
        }

        private void SortGamesOverview(string sortMemberPath, ListSortDirection direction)
        {
            // Quick reverse if same column
            if (_overviewSortPath == sortMemberPath && _overviewSortDirection == ListSortDirection.Ascending &&
                direction == ListSortDirection.Descending)
            {
                _filteredGamesOverview.Reverse();
                _overviewSortDirection = direction;
                _overviewPager.SetSourceItems(_filteredGamesOverview);
                return;
            }

            _overviewSortPath = sortMemberPath;
            _overviewSortDirection = direction;

            Comparison<GameOverviewItem> comparison = sortMemberPath switch
            {
                nameof(GameOverviewItem.GameName) => (a, b) => string.Compare(a.GameName, b.GameName, StringComparison.OrdinalIgnoreCase),
                nameof(GameOverviewItem.LastPlayed) => (a, b) => (a.LastPlayed ?? DateTime.MinValue).CompareTo(b.LastPlayed ?? DateTime.MinValue),
                nameof(GameOverviewItem.Progression) => (a, b) => a.Progression.CompareTo(b.Progression),
                nameof(GameOverviewItem.TotalAchievements) => (a, b) => a.TotalAchievements.CompareTo(b.TotalAchievements),
                nameof(GameOverviewItem.UnlockedAchievements) => (a, b) => a.UnlockedAchievements.CompareTo(b.UnlockedAchievements),
                _ => null
            };

            if (comparison != null)
            {
                if (direction == ListSortDirection.Descending)
                {
                    _filteredGamesOverview.Sort((a, b) => comparison(b, a));
                }
                else
                {
                    _filteredGamesOverview.Sort(comparison);
                }
            }

            _overviewPager.SetSourceItems(_filteredGamesOverview);
        }

        private void SortRecentAchievements(string sortMemberPath, ListSortDirection direction)
        {
            // Quick reverse if same column
            if (_recentSortPath == sortMemberPath && _recentSortDirection == ListSortDirection.Ascending &&
                direction == ListSortDirection.Descending)
            {
                _filteredRecentAchievements.Reverse();
                _recentSortDirection = direction;
                _recentPager.SetSourceItems(_filteredRecentAchievements);
                return;
            }

            _recentSortPath = sortMemberPath;
            _recentSortDirection = direction;

            Comparison<RecentAchievementItem> comparison = sortMemberPath switch
            {
                "Name" => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                "GameName" => (a, b) => string.Compare(a.GameName, b.GameName, StringComparison.OrdinalIgnoreCase),
                "UnlockTime" => (a, b) => a.UnlockTime.CompareTo(b.UnlockTime),
                "GlobalPercent" => (a, b) => a.GlobalPercent.CompareTo(b.GlobalPercent),
                _ => null
            };

            if (comparison != null)
            {
                if (direction == ListSortDirection.Descending)
                {
                    _filteredRecentAchievements.Sort((a, b) => comparison(b, a));
                }
                else
                {
                    _filteredRecentAchievements.Sort(comparison);
                }
            }

            _recentPager.SetSourceItems(_filteredRecentAchievements);
        }

        private void SortSelectedGameAchievements(string sortMemberPath, ListSortDirection direction)
        {
            // Quick reverse if same column
            if (_selectedGameSortPath == sortMemberPath && _selectedGameSortDirection == ListSortDirection.Ascending &&
                direction == ListSortDirection.Descending)
            {
                _filteredSelectedGameAchievements.Reverse();
                _selectedGameSortDirection = direction;
                _selectedGameAchievementsPager.SetSourceItems(_filteredSelectedGameAchievements);
                return;
            }

            _selectedGameSortPath = sortMemberPath;
            _selectedGameSortDirection = direction;

            Comparison<AchievementDisplayItem> comparison = sortMemberPath switch
            {
                "DisplayName" => (a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase),
                "UnlockTime" => (a, b) => (a.UnlockTimeUtc ?? DateTime.MinValue).CompareTo(b.UnlockTimeUtc ?? DateTime.MinValue),
                "GlobalPercent" => (a, b) => (a.GlobalPercentUnlocked ?? 100).CompareTo(b.GlobalPercentUnlocked ?? 100),
                _ => null
            };

            if (comparison != null)
            {
                if (direction == ListSortDirection.Descending)
                {
                    _filteredSelectedGameAchievements.Sort((a, b) => comparison(b, a));
                }
                else
                {
                    _filteredSelectedGameAchievements.Sort(comparison);
                }
            }

            _selectedGameAchievementsPager.SetSourceItems(_filteredSelectedGameAchievements);
        }

        public void Dispose()
        {
            _disposed = true;
            SetActive(false);
            _refreshDebounceTimer?.Stop();
            CancelPendingRefresh();
            if (_achievementManager != null)
            {
                _achievementManager.RebuildProgress -= OnRebuildProgress;
                _achievementManager.CacheInvalidated -= OnCacheInvalidated;
            }
            if (_settings != null)
            {
                _settings.PropertyChanged -= OnSettingsChanged;
            }
        }
    }
}

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
using PlayniteAchievements.Services;
using Playnite.SDK;

using ObservableObject = PlayniteAchievements.Common.ObservableObject;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;
using ScanModeKeys = PlayniteAchievements.Models.ScanModeKeys;

namespace PlayniteAchievements.ViewModels
{
    public class SingleGameControlModel : ObservableObject, IDisposable
    {
        private readonly AchievementManager _achievementManager;
        private readonly IPlayniteAPI _playniteApi;
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly Guid _gameId;

        private const int AchievementsPageSize = 100;
        private int _currentPage = 1;
        private List<AchievementDisplayItem> _allAchievements = new List<AchievementDisplayItem>();

        // Sort state tracking for quick reverse
        private string _currentSortPath;
        private ListSortDirection _currentSortDirection;

        public SingleGameControlModel(
            Guid gameId,
            AchievementManager achievementManager,
            IPlayniteAPI playniteApi,
            ILogger logger,
            PlayniteAchievementsSettings settings)
        {
            _gameId = gameId;
            _achievementManager = achievementManager ?? throw new ArgumentNullException(nameof(achievementManager));
            _playniteApi = playniteApi;
            _logger = logger;
            _settings = settings;

            Timeline = new TimelineViewModel { EnableDiagnostics = _settings?.Persisted?.EnableDiagnostics == true };
            OnPropertyChanged(nameof(Timeline));

            // Initialize commands
            RevealAchievementCommand = new RelayCommand(param => RevealAchievement(param as AchievementDisplayItem));
            DismissStatusCommand = new RelayCommand(_ => DismissStatus(), _ => CanDismissStatus);
            NextPageCommand = new RelayCommand(_ => GoToNextPage(), _ => CanGoNext);
            PreviousPageCommand = new RelayCommand(_ => GoToPreviousPage(), _ => CanGoPrevious);
            FirstPageCommand = new RelayCommand(_ => GoToFirstPage(), _ => CanGoPrevious);
            LastPageCommand = new RelayCommand(_ => GoToLastPage(), _ => CanGoNext);

            ScanGameCommand = new RelayCommand(
                async (param) =>
                {
                    if (IsScanning) return;

                    IsScanning = true;
                    ScanStatusMessage = ResourceProvider.GetString("LOCPlayAch_Status_Scanning");
                    IsStatusMessageVisible = true;

                    try
                    {
                        await _achievementManager.ExecuteScanAsync(ScanModeType.Single.GetKey(), _gameId);

                        // Load updated data
                        LoadGameData();

                        // Simple success message
                        ScanStatusMessage = ResourceProvider.GetString("LOCPlayAch_Status_ScanComplete");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Failed to scan game {_gameId}.");
                        ScanStatusMessage = string.Format(
                            ResourceProvider.GetString("LOCPlayAch_Error_ScanFailed"),
                            ex.Message);
                    }
                    finally
                    {
                        IsScanning = false;
                        await Task.Delay(3000);
                        IsStatusMessageVisible = false;
                    }
                },
                _ => !_achievementManager.IsRebuilding);

            // Subscribe to settings changes
            if (_settings != null)
            {
                _settings.PropertyChanged += OnSettingsChanged;
            }
            _achievementManager.GameCacheUpdated += OnGameCacheUpdated;
            _achievementManager.CacheInvalidated += OnCacheInvalidated;
            _achievementManager.RebuildProgress += OnRebuildProgress;

            // Load data
            LoadGameData();
        }

        #region Properties

        private string _gameName;
        public string GameName
        {
            get => _gameName;
            private set => SetValue(ref _gameName, value);
        }

        private string _gameIconPath;
        public string GameIconPath
        {
            get => _gameIconPath;
            private set => SetValue(ref _gameIconPath, value);
        }

        private int _totalAchievements;
        public int TotalAchievements
        {
            get => _totalAchievements;
            private set => SetValue(ref _totalAchievements, value);
        }

        private int _unlockedAchievements;
        public int UnlockedAchievements
        {
            get => _unlockedAchievements;
            private set => SetValue(ref _unlockedAchievements, value);
        }

        private bool _isPerfect;
        public bool IsPerfect
        {
            get => _isPerfect;
            private set => SetValue(ref _isPerfect, value);
        }

        private int _commonCount;
        public int CommonCount
        {
            get => _commonCount;
            private set => SetValue(ref _commonCount, value);
        }

        private int _uncommonCount;
        public int UncommonCount
        {
            get => _uncommonCount;
            private set => SetValue(ref _uncommonCount, value);
        }

        private int _rareCount;
        public int RareCount
        {
            get => _rareCount;
            private set => SetValue(ref _rareCount, value);
        }

        private int _ultraRareCount;
        public int UltraRareCount
        {
            get => _ultraRareCount;
            private set => SetValue(ref _ultraRareCount, value);
        }

        public double Progression => TotalAchievements > 0
            ? (double)UnlockedAchievements / TotalAchievements * 100
            : 0;

        public string ProgressionText => string.Format(ResourceProvider.GetString("LOCPlayAch_Format_Percentage"), Progression);

        public TimelineViewModel Timeline { get; private set; }

        // Achievement list
        public ObservableCollection<AchievementDisplayItem> Achievements { get; } = new ObservableCollection<AchievementDisplayItem>();

        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                var clamped = Math.Max(1, Math.Min(value, TotalPages));
                if (SetValueAndReturn(ref _currentPage, clamped))
                {
                    UpdatePagedAchievements();
                    RaisePaginationChanged();
                }
            }
        }

        public int TotalPages => Math.Max(1, (int)Math.Ceiling(_allAchievements.Count / (double)AchievementsPageSize));

        public bool CanGoNext => CurrentPage < TotalPages;
        public bool CanGoPrevious => CurrentPage > 1;
        public bool HasMultiplePages => TotalPages > 1;

        public string PageInfo => string.Format(
            ResourceProvider.GetString("LOCPlayAch_Achievements_PageInfo"),
            CurrentPage,
            TotalPages);

        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            private set => SetValue(ref _isScanning, value);
        }

        private string _scanStatusMessage;
        public string ScanStatusMessage
        {
            get => _scanStatusMessage;
            private set => SetValue(ref _scanStatusMessage, value);
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
            set => SetValue(ref _isTimelineVisible, value);
        }

        #endregion

        #region Commands

        public ICommand RevealAchievementCommand { get; }
        public ICommand ScanGameCommand { get; }
        public ICommand DismissStatusCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand LastPageCommand { get; }

        #endregion

        #region Private Methods

        private void UpdatePagedAchievements()
        {
            var skip = (CurrentPage - 1) * AchievementsPageSize;
            var pageItems = _allAchievements.Skip(skip).Take(AchievementsPageSize);
            CollectionHelper.SynchronizeCollection(Achievements, pageItems);
            OnPropertyChanged(nameof(PageInfo));
        }

        private void RaisePaginationChanged()
        {
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(HasMultiplePages));
            OnPropertyChanged(nameof(PageInfo));
            (NextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FirstPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (LastPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
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

        private void LoadGameData()
        {
            try
            {
                var game = _playniteApi?.Database?.Games?.Get(_gameId);
                if (game == null)
                {
                    _logger?.Warn($"Game not found: {_gameId}");
                    return;
                }

                GameName = game.Name;
                GameIconPath = (!string.IsNullOrEmpty(game.Icon)) ? _playniteApi.Database.GetFullFilePath(game.Icon) : null;

                var gameData = _achievementManager.GetGameAchievementData(_gameId);
                if (gameData == null || gameData.NoAchievements || gameData.Achievements == null)
                {
                    _logger?.Info($"No achievement data for game: {game.Name}");

                    TotalAchievements = 0;
                    UnlockedAchievements = 0;
                    IsPerfect = false;
                    CommonCount = 0;
                    UncommonCount = 0;
                    RareCount = 0;
                    UltraRareCount = 0;

                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        _allAchievements = new List<AchievementDisplayItem>();
                        _currentPage = 1;
                        OnPropertyChanged(nameof(CurrentPage));
                        Achievements.Clear();
                    });

                    OnPropertyChanged(nameof(Progression));
                    OnPropertyChanged(nameof(ProgressionText));
                    RaisePaginationChanged();

                    Timeline.SetCounts(null);
                    return;
                }

                var achievements = gameData.Achievements;
                TotalAchievements = achievements.Count;
                UnlockedAchievements = achievements.Count(a => a.Unlocked);
                IsPerfect = UnlockedAchievements == TotalAchievements && TotalAchievements > 0;

                // Calculate rarity counts
                int common = 0, uncommon = 0, rare = 0, ultraRare = 0;
                var hideLocked = _settings?.Persisted.HideAchievementsLockedForSelf ?? false;
                var displayItems = new List<AchievementDisplayItem>();
                var unlockCounts = new Dictionary<DateTime, int>();

                foreach (var ach in achievements)
                {
                    if (ach.Unlocked)
                    {
                        if (ach.UnlockTimeUtc.HasValue)
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

                        var pct = ach.GlobalPercentUnlocked ?? 100;
                        var tier = RarityHelper.GetRarityTier(pct);
                        switch (tier)
                        {
                            case RarityTier.UltraRare: ultraRare++; break;
                            case RarityTier.Rare: rare++; break;
                            case RarityTier.Uncommon: uncommon++; break;
                            default: common++; break;
                        }
                    }

                    displayItems.Add(new AchievementDisplayItem
                    {
                        GameName = gameData.GameName ?? "Unknown",
                        PlayniteGameId = _gameId,
                        DisplayName = ach.DisplayName ?? ach.ApiName ?? "Unknown",
                        Description = ach.Description ?? "",
                        IconPath = ach.IconPath,
                        UnlockTimeUtc = ach.UnlockTimeUtc,
                        GlobalPercentUnlocked = ach.GlobalPercentUnlocked,
                        Unlocked = ach.Unlocked,
                        Hidden = ach.Hidden,
                        ApiName = ach.ApiName,
                        HideAchievementsLockedForSelf = hideLocked,
                        ProgressNum = ach.ProgressNum,
                        ProgressDenom = ach.ProgressDenom
                    });
                }

                CommonCount = common;
                UncommonCount = uncommon;
                RareCount = rare;
                UltraRareCount = ultraRare;

                // Sort: unlocked first by date desc, then locked by rarity
                var sorted = displayItems
                    .OrderByDescending(a => a.Unlocked)
                    .ThenByDescending(a => a.UnlockTimeUtc ?? DateTime.MinValue)
                    .ThenBy(a => a.GlobalPercentUnlocked ?? 100)
                    .ToList();

                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    _allAchievements = sorted;
                    _currentPage = 1;
                    OnPropertyChanged(nameof(CurrentPage));
                    UpdatePagedAchievements();
                    RaisePaginationChanged();
                });

                OnPropertyChanged(nameof(Progression));
                OnPropertyChanged(nameof(ProgressionText));

                Timeline.SetCounts(unlockCounts);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to load game data for {_gameId}");
            }
        }

        private void RevealAchievement(AchievementDisplayItem item)
        {
            item?.ToggleReveal();
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

        private void OnCacheInvalidated(object sender, EventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(LoadGameData);
        }

        private void OnRebuildProgress(object sender, ProgressReport report)
        {
            if (report == null) return;

            // Check if this progress is for our game
            var isForOurGame = report.Message?.Contains(_gameId.ToString()) == true ||
                               (GameName != null && report.Message?.Contains(GameName) == true);

            if (!isForOurGame && IsScanning)
            {
                // If we're scanning and get progress not for our game, update generic status
                ScanStatusMessage = report.Message ?? ResourceProvider.GetString("LOCPlayAch_Status_Scanning");
                OnPropertyChanged(nameof(ScanStatusMessage));
            }
            else if (isForOurGame)
            {
                // Update with specific progress for our game
                ScanStatusMessage = report.Message ?? ResourceProvider.GetString("LOCPlayAch_Status_Scanning");
                OnPropertyChanged(nameof(ScanStatusMessage));
            }

            // Handle completion
            if (report.TotalSteps > 0 && report.CurrentStep >= report.TotalSteps)
            {
                IsScanning = false;
                OnPropertyChanged(nameof(IsScanning));
            }
            else if (report.IsCanceled)
            {
                IsScanning = false;
                ScanStatusMessage = ResourceProvider.GetString("LOCPlayAch_Status_Canceled");
                OnPropertyChanged(nameof(IsScanning));
                OnPropertyChanged(nameof(ScanStatusMessage));
            }

            (ScanGameCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void OnSettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Persisted.HideAchievementsLockedForSelf"
                || e.PropertyName == "HideAchievementsLockedForSelf"
                || e.PropertyName == "Persisted")
            {
                var hide = _settings.Persisted.HideAchievementsLockedForSelf;

                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    foreach (var item in _allAchievements)
                    {
                        item.HideAchievementsLockedForSelf = hide;
                    }
                });
            }
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
            // Quick reverse if same column
            if (_currentSortPath == sortMemberPath && _currentSortDirection == ListSortDirection.Ascending &&
                direction == ListSortDirection.Descending)
            {
                _allAchievements.Reverse();
                _currentSortDirection = direction;
                _currentPage = 1;
                OnPropertyChanged(nameof(CurrentPage));
                UpdatePagedAchievements();
                RaisePaginationChanged();
                return;
            }

            _currentSortPath = sortMemberPath;
            _currentSortDirection = direction;

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
                    _allAchievements.Sort((a, b) => comparison(b, a));
                }
                else
                {
                    _allAchievements.Sort(comparison);
                }
            }

            _currentPage = 1;
            OnPropertyChanged(nameof(CurrentPage));
            UpdatePagedAchievements();
            RaisePaginationChanged();
        }

        #region IDisposable

        public void Dispose()
        {
            if (_settings != null)
            {
                _settings.PropertyChanged -= OnSettingsChanged;
            }
            _achievementManager.GameCacheUpdated -= OnGameCacheUpdated;
            _achievementManager.CacheInvalidated -= OnCacheInvalidated;
            _achievementManager.RebuildProgress -= OnRebuildProgress;
        }

        #endregion
    }
}

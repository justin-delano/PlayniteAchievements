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
        private readonly ScanManager _achievementManager;
        private readonly IPlayniteAPI _playniteApi;
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly Guid _gameId;

        // Sort state tracking for quick reverse
        private string _currentSortPath;
        private ListSortDirection _currentSortDirection;

        // Search and filter state
        private List<AchievementDisplayItem> _allAchievements = new List<AchievementDisplayItem>();
        private string _searchText = string.Empty;

        public SingleGameControlModel(
            Guid gameId,
            ScanManager achievementManager,
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
            private set
            {
                if (SetValueAndReturn(ref _totalAchievements, value))
                {
                    OnPropertyChanged(nameof(HasAchievements));
                }
            }
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

        private bool _isStatsVisible = true;
        public bool IsStatsVisible
        {
            get => _isStatsVisible;
            set => SetValue(ref _isStatsVisible, value);
        }

        public bool HasAchievements => TotalAchievements > 0;

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

        #endregion

        #region Commands

        public ICommand RevealAchievementCommand { get; }
        public ICommand ScanGameCommand { get; }
        public ICommand DismissStatusCommand { get; }

        #endregion

        #region Private Methods

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
                    _allAchievements = new List<AchievementDisplayItem>();

                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        Achievements.Clear();
                    });

                    OnPropertyChanged(nameof(Progression));
                    OnPropertyChanged(nameof(ProgressionText));

                    Timeline.SetCounts(null);
                    return;
                }

                var achievements = gameData.Achievements;
                TotalAchievements = achievements.Count;
                UnlockedAchievements = achievements.Count(a => a.Unlocked);
                IsPerfect = UnlockedAchievements == TotalAchievements && TotalAchievements > 0;

                // Calculate rarity counts
                int common = 0, uncommon = 0, rare = 0, ultraRare = 0;
                var hideIcon = _settings?.Persisted.HideHiddenIcon ?? false;
                var hideTitle = _settings?.Persisted.HideHiddenTitle ?? false;
                var hideDescription = _settings?.Persisted.HideHiddenDescription ?? false;
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
                        HideHiddenIcon = hideIcon,
                        HideHiddenTitle = hideTitle,
                        HideHiddenDescription = hideDescription,
                        ProgressNum = ach.ProgressNum,
                        ProgressDenom = ach.ProgressDenom
                    });
                }

                CommonCount = common;
                UncommonCount = uncommon;
                RareCount = rare;
                UltraRareCount = ultraRare;

                // Sort: unlocked first by date desc, then locked by rarity
                _allAchievements = displayItems
                    .OrderByDescending(a => a.Unlocked)
                    .ThenByDescending(a => a.UnlockTimeUtc ?? DateTime.MinValue)
                    .ThenBy(a => a.GlobalPercentUnlocked ?? 100)
                    .ToList();

                ApplySearchFilter();

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
            var scanStatus = _achievementManager.GetScanStatusSnapshot(report);
            var statusMessage = scanStatus.Message ?? ResourceProvider.GetString("LOCPlayAch_Status_Scanning");

            // Check if this progress is for our game
            var isForOurGame = report.Message?.Contains(_gameId.ToString()) == true ||
                               (GameName != null && report.Message?.Contains(GameName) == true);

            if (!isForOurGame && IsScanning)
            {
                // If we're scanning and get progress not for our game, update generic status
                ScanStatusMessage = statusMessage;
                OnPropertyChanged(nameof(ScanStatusMessage));
            }
            else if (isForOurGame)
            {
                // Update with specific progress for our game
                ScanStatusMessage = statusMessage;
                OnPropertyChanged(nameof(ScanStatusMessage));
            }

            // Handle completion
            if (scanStatus.IsCanceled)
            {
                IsScanning = false;
                ScanStatusMessage = statusMessage;
                OnPropertyChanged(nameof(IsScanning));
                OnPropertyChanged(nameof(ScanStatusMessage));
            }
            else if (scanStatus.IsFinal)
            {
                IsScanning = false;
                ScanStatusMessage = statusMessage;
                OnPropertyChanged(nameof(IsScanning));
                OnPropertyChanged(nameof(ScanStatusMessage));
            }

            (ScanGameCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void OnSettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Persisted.HideHiddenIcon"
                || e.PropertyName == "Persisted.HideHiddenTitle"
                || e.PropertyName == "Persisted.HideHiddenDescription"
                || e.PropertyName == "Persisted")
            {
                var hideIcon = _settings.Persisted.HideHiddenIcon;
                var hideTitle = _settings.Persisted.HideHiddenTitle;
                var hideDescription = _settings.Persisted.HideHiddenDescription;

                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    foreach (var item in Achievements)
                    {
                        item.HideHiddenIcon = hideIcon;
                        item.HideHiddenTitle = hideTitle;
                        item.HideHiddenDescription = hideDescription;
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
                var items = Achievements.ToList();
                items.Reverse();
                _currentSortDirection = direction;
                CollectionHelper.SynchronizeCollection(Achievements, items);
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
                var items = Achievements.ToList();
                if (direction == ListSortDirection.Descending)
                {
                    items.Sort((a, b) => comparison(b, a));
                }
                else
                {
                    items.Sort(comparison);
                }
                CollectionHelper.SynchronizeCollection(Achievements, items);
            }
        }

        private void ApplySearchFilter()
        {
            IEnumerable<AchievementDisplayItem> filtered = _allAchievements;

            if (!string.IsNullOrEmpty(_searchText))
            {
                filtered = filtered.Where(a =>
                    (a.DisplayName?.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (a.Description?.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                CollectionHelper.SynchronizeCollection(Achievements, filtered.ToList());
            });
        }

        public void ClearSearch()
        {
            SearchText = string.Empty;
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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Providers.Manual;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels
{
    /// <summary>
    /// Item representing an editable achievement in the edit dialog.
    /// Uses AchievementDetail directly to avoid duplicating definition.
    /// </summary>
    public sealed class ManualAchievementEditItem : INotifyPropertyChanged
    {
        private bool _isUnlocked;
        private DateTime? _unlockTime;

        public AchievementDetail Source { get; }

        // Forward properties from AchievementDetail
        public string ApiName => Source.ApiName;
        public string DisplayName => Source.DisplayName;
        public string Description => Source.Description;
        public string UnlockedIconUrl => Source.UnlockedIconPath;
        public string LockedIconUrl => Source.LockedIconPath;
        public bool Hidden => Source.Hidden;
        public double? GlobalPercentUnlocked => Source.GlobalPercentUnlocked;
        public int? Points => Source.Points;
        public bool HasRarity => GlobalPercentUnlocked.HasValue;

        public bool IsUnlocked
        {
            get => _isUnlocked;
            set
            {
                if (_isUnlocked != value)
                {
                    _isUnlocked = value;
                    OnPropertyChanged(nameof(IsUnlocked));

                    // Auto-set unlock time to now when unlocking without a time
                    if (value && !_unlockTime.HasValue)
                    {
                        UnlockTime = DateTime.UtcNow;
                    }
                    // Clear unlock time when locking
                    else if (!value)
                    {
                        UnlockTime = null;
                    }
                }
            }
        }

        public DateTime? UnlockTime
        {
            get => _unlockTime;
            set
            {
                if (_unlockTime != value)
                {
                    _unlockTime = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UnlockTimeLocal));
                    OnPropertyChanged(nameof(UnlockDate));
                    OnPropertyChanged(nameof(UnlockTimeOfDay));

                    // Ensure unlocked state matches
                    if (value.HasValue && !_isUnlocked)
                    {
                        _isUnlocked = true;
                        OnPropertyChanged(nameof(IsUnlocked));
                    }
                }
            }
        }

        /// <summary>
        /// Local time representation for display.
        /// </summary>
        public DateTime? UnlockTimeLocal
        {
            get => UnlockTime?.ToLocalTime();
            set
            {
                if (value.HasValue)
                {
                    // Convert local to UTC
                    UnlockTime = value.Value.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(value.Value, DateTimeKind.Local).ToUniversalTime()
                        : value.Value.ToUniversalTime();
                }
                else
                {
                    UnlockTime = null;
                }
            }
        }

        /// <summary>
        /// Date component of unlock time (for DatePicker).
        /// </summary>
        public DateTime? UnlockDate
        {
            get => UnlockTimeLocal?.Date;
            set
            {
                if (value.HasValue)
                {
                    // Preserve time of day or default to noon
                    var existingTime = UnlockTimeLocal?.TimeOfDay ?? TimeSpan.FromHours(12);
                    UnlockTimeLocal = value.Value.Date + existingTime;
                }
                else if (!IsUnlocked)
                {
                    UnlockTime = null;
                }
            }
        }

        /// <summary>
        /// Time of day component (for TimePicker).
        /// </summary>
        public TimeSpan? UnlockTimeOfDay
        {
            get => UnlockTimeLocal?.TimeOfDay;
            set
            {
                if (value.HasValue && UnlockDate.HasValue)
                {
                    UnlockTimeLocal = UnlockDate.Value.Date + value.Value;
                }
            }
        }

        public ManualAchievementEditItem(AchievementDetail source, bool isUnlocked, DateTime? unlockTime)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            _isUnlocked = isUnlocked;
            _unlockTime = unlockTime;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// ViewModel for the manual achievements edit dialog.
    /// Allows users to view and edit unlock states for linked achievements.
    /// </summary>
    public sealed class ManualAchievementsEditViewModel : INotifyPropertyChanged
    {
        private readonly IManualSource _source;
        private readonly ILogger _logger;
        private readonly string _language;
        private readonly ManualAchievementLink _existingLink;
        private bool _isLoading;
        private string _statusMessage = string.Empty;
        private string _searchFilter = string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler RequestClose;

        public string WindowTitle =>
            string.Format(ResourceProvider.GetString("LOCPlayAch_ManualAchievements_Edit_Title"), PlayniteGameName);

        public string PlayniteGameName { get; }

        public string SourceGameName { get; }

        public string SourceGameId { get; }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                if (_searchFilter != value)
                {
                    _searchFilter = value ?? string.Empty;
                    OnPropertyChanged();
                    FilterAchievements();
                }
            }
        }

        public ObservableCollection<ManualAchievementEditItem> AllAchievements { get; } =
            new ObservableCollection<ManualAchievementEditItem>();

        public ObservableCollection<ManualAchievementEditItem> FilteredAchievements { get; } =
            new ObservableCollection<ManualAchievementEditItem>();

        public int TotalCount => AllAchievements.Count;

        public int UnlockedCount => AllAchievements.Count(a => a.IsUnlocked);

        public double CompletionPercent =>
            AllAchievements.Count > 0
                ? (double)UnlockedCount / TotalCount * 100.0
                : 0;

        public bool? DialogResult { get; private set; }

        public RelayCommand UnlockAllCommand { get; }
        public RelayCommand LockAllCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }

        public ManualAchievementsEditViewModel(
            IManualSource source,
            List<AchievementDetail> achievements,
            string sourceGameId,
            string sourceGameName,
            ManualAchievementLink existingLink,
            string playniteGameName,
            string language,
            ILogger logger)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _logger = logger;
            _language = language ?? "english";
            _existingLink = existingLink;
            PlayniteGameName = playniteGameName ?? string.Empty;
            SourceGameName = sourceGameName ?? string.Empty;
            SourceGameId = sourceGameId ?? string.Empty;

            UnlockAllCommand = new RelayCommand(_ => SetAllUnlocked(true));
            LockAllCommand = new RelayCommand(_ => SetAllUnlocked(false));
            SaveCommand = new RelayCommand(_ => CloseDialog(true));
            CancelCommand = new RelayCommand(_ => CloseDialog(false));

            // Populate achievements directly from AchievementDetail list
            if (achievements != null)
            {
                foreach (var detail in achievements)
                {
                    if (detail == null || string.IsNullOrWhiteSpace(detail.ApiName))
                    {
                        continue;
                    }

                    // Get existing unlock state
                    var isUnlocked = false;
                    DateTime? unlockTime = null;

                    if (existingLink?.UnlockTimes != null &&
                        existingLink.UnlockTimes.TryGetValue(detail.ApiName, out var existingTime))
                    {
                        unlockTime = existingTime;
                        isUnlocked = existingTime.HasValue;
                    }

                    var item = new ManualAchievementEditItem(detail, isUnlocked, unlockTime);
                    item.PropertyChanged += OnAchievementChanged;
                    AllAchievements.Add(item);
                }
            }

            FilterAchievements();
            UpdateCounts();
        }

        private void OnAchievementChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ManualAchievementEditItem.IsUnlocked))
            {
                UpdateCounts();
            }
        }

        private void SetAllUnlocked(bool unlocked)
        {
            foreach (var item in AllAchievements)
            {
                if (unlocked)
                {
                    // Set to now if not already unlocked
                    if (!item.IsUnlocked)
                    {
                        item.UnlockTime = DateTime.UtcNow;
                    }
                }
                else
                {
                    item.UnlockTime = null;
                    item.IsUnlocked = false;
                }
            }
        }

        private void FilterAchievements()
        {
            FilteredAchievements.Clear();

            var filter = SearchFilter?.Trim().ToLowerInvariant() ?? string.Empty;
            var hasFilter = !string.IsNullOrEmpty(filter);

            foreach (var item in AllAchievements)
            {
                if (!hasFilter ||
                    item.DisplayName?.ToLowerInvariant().Contains(filter) == true ||
                    item.Description?.ToLowerInvariant().Contains(filter) == true ||
                    item.ApiName?.ToLowerInvariant().Contains(filter) == true)
                {
                    FilteredAchievements.Add(item);
                }
            }
        }

        private void UpdateCounts()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(UnlockedCount));
            OnPropertyChanged(nameof(CompletionPercent));
        }

        /// <summary>
        /// Builds a ManualAchievementLink from the current state.
        /// </summary>
        public ManualAchievementLink BuildLink()
        {
            var now = DateTime.UtcNow;

            var link = new ManualAchievementLink
            {
                SourceKey = _source.SourceKey,
                SourceGameId = SourceGameId,
                UnlockTimes = new Dictionary<string, DateTime?>(),
                CreatedUtc = _existingLink?.CreatedUtc ?? now,
                LastModifiedUtc = now
            };

            foreach (var item in AllAchievements)
            {
                link.UnlockTimes[item.ApiName] = item.IsUnlocked ? item.UnlockTime : null;
            }

            return link;
        }

        /// <summary>
        /// Builds a GameAchievementData object suitable for caching.
        /// Combines achievement schema with user-set unlock times.
        /// </summary>
        public GameAchievementData BuildGameAchievementData(Game game, string providerName)
        {
            var achievements = new List<AchievementDetail>();

            foreach (var item in AllAchievements)
            {
                var detail = new AchievementDetail
                {
                    ApiName = item.ApiName,
                    DisplayName = item.DisplayName,
                    Description = item.Description,
                    UnlockedIconPath = item.Source.UnlockedIconPath,
                    LockedIconPath = item.Source.LockedIconPath,
                    Hidden = item.Hidden,
                    GlobalPercentUnlocked = item.GlobalPercentUnlocked,
                    Points = item.Points,
                    Unlocked = item.IsUnlocked,
                    UnlockTimeUtc = item.UnlockTime
                };
                achievements.Add(detail);
            }

            return new GameAchievementData
            {
                LastUpdatedUtc = DateTime.UtcNow,
                ProviderName = providerName,
                LibrarySourceName = game != null ? game.PluginId.ToString() : string.Empty,
                HasAchievements = achievements.Count > 0,
                GameName = game?.Name ?? PlayniteGameName,
                AppId = int.TryParse(SourceGameId, out var appId) ? appId : 0,
                PlayniteGameId = game?.Id,
                Game = game,
                Achievements = achievements
            };
        }

        private void CloseDialog(bool result)
        {
            DialogResult = result;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

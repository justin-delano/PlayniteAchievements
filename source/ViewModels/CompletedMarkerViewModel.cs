using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels
{
    public sealed class CompletedMarkerViewModel : ObservableObject
    {
        private const string HiddenIconPath = "pack://application:,,,/PlayniteAchievements;component/Resources/HiddenAchIcon.png";
        private const string HiddenDescription = "???";

        private readonly Guid _gameId;
        private readonly AchievementManager _achievementManager;
        private readonly IPlayniteAPI _playniteApi;
        private readonly ILogger _logger;
        private readonly HashSet<string> _revealedApiNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler RequestClose;

        public CompletedMarkerViewModel(
            Guid gameId,
            AchievementManager achievementManager,
            IPlayniteAPI playniteApi,
            ILogger logger)
        {
            _gameId = gameId;
            _achievementManager = achievementManager ?? throw new ArgumentNullException(nameof(achievementManager));
            _playniteApi = playniteApi;
            _logger = logger;

            SetMarkerCommand = new RelayCommand(
                _ => SetCompletedMarker(),
                _ => SelectedOption != null);

            ClearMarkerCommand = new RelayCommand(_ => ClearCompletedMarker());

            DoneCommand = new RelayCommand(_ => RequestClose?.Invoke(this, EventArgs.Empty));

            ToggleRevealCommand = new RelayCommand(ToggleReveal);

            LoadData();
        }

        public ObservableCollection<CompletedMarkerOptionItem> AchievementOptions { get; } =
            new ObservableCollection<CompletedMarkerOptionItem>();

        private CompletedMarkerOptionItem _selectedOption;
        public CompletedMarkerOptionItem SelectedOption
        {
            get => _selectedOption;
            set
            {
                if (SetValueAndReturn(ref _selectedOption, value))
                {
                    (SetMarkerCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private string _gameName;
        public string GameName
        {
            get => _gameName;
            private set
            {
                if (SetValueAndReturn(ref _gameName, value))
                {
                    OnPropertyChanged(nameof(WindowTitle));
                }
            }
        }

        private string _currentMarkerText;
        public string CurrentMarkerText
        {
            get => _currentMarkerText;
            private set => SetValue(ref _currentMarkerText, value);
        }

        public string WindowTitle
        {
            get
            {
                var titleFormat = ResourceProvider.GetString("LOCPlayAch_CompletedMarker_WindowTitle");
                if (string.IsNullOrWhiteSpace(titleFormat))
                {
                    titleFormat = "Completed Marker - {0}";
                }

                return string.Format(titleFormat, GameName ?? ResourceProvider.GetString("LOCPlayAch_Text_UnknownGame"));
            }
        }

        public ICommand SetMarkerCommand { get; }
        public ICommand ClearMarkerCommand { get; }
        public ICommand DoneCommand { get; }
        public ICommand ToggleRevealCommand { get; }

        private void LoadData()
        {
            try
            {
                var game = _playniteApi?.Database?.Games?.Get(_gameId);
                GameName = game?.Name ?? ResourceProvider.GetString("LOCPlayAch_Text_UnknownGame") ?? "Unknown Game";

                var gameData = _achievementManager.GetGameAchievementData(_gameId);
                var achievements = gameData?.Achievements ?? new List<AchievementDetail>();
                var currentMarkerApiName = NormalizeText(gameData?.CompletedMarkerApiName);

                var sortedAchievements = achievements
                    .Where(a => a != null && !string.IsNullOrWhiteSpace(a.ApiName))
                    .OrderByDescending(a => a.Unlocked)
                    .ThenBy(a => a.DisplayName ?? a.ApiName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                AchievementOptions.Clear();
                CompletedMarkerOptionItem currentMarkerOption = null;
                for (int i = 0; i < sortedAchievements.Count; i++)
                {
                    var achievement = sortedAchievements[i];
                    var isRevealed = _revealedApiNames.Contains(achievement.ApiName);
                    var option = new CompletedMarkerOptionItem
                    {
                        ApiName = achievement.ApiName.Trim(),
                        DisplayName = ResolveDisplayName(achievement, isRevealed),
                        Description = ResolveDescription(achievement, isRevealed),
                        IconPath = ResolveIconPath(achievement, isRevealed),
                        Unlocked = achievement.Unlocked,
                        Hidden = achievement.Hidden,
                        IsRevealed = isRevealed,
                        IsCurrentMarker = string.Equals(
                            achievement.ApiName.Trim(),
                            currentMarkerApiName,
                            StringComparison.OrdinalIgnoreCase)
                    };

                    if (option.IsCurrentMarker)
                    {
                        currentMarkerOption = option;
                    }

                    AchievementOptions.Add(option);
                }

                SelectedOption = currentMarkerOption;
                SetCurrentMarkerText(currentMarkerOption?.DisplayName);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed loading completed marker options for gameId={_gameId}");
                SetCurrentMarkerText(null);
            }
        }

        private void ToggleReveal(object parameter)
        {
            if (parameter is CompletedMarkerOptionItem item && item.Hidden)
            {
                if (_revealedApiNames.Contains(item.ApiName))
                {
                    _revealedApiNames.Remove(item.ApiName);
                }
                else
                {
                    _revealedApiNames.Add(item.ApiName);
                }

                ReloadSingleItem(item);
            }
        }

        private void ReloadSingleItem(CompletedMarkerOptionItem item)
        {
            var gameData = _achievementManager.GetGameAchievementData(_gameId);
            var achievements = gameData?.Achievements ?? new List<AchievementDetail>();
            var achievement = achievements.FirstOrDefault(a =>
                string.Equals(a?.ApiName?.Trim(), item.ApiName, StringComparison.OrdinalIgnoreCase));

            if (achievement == null)
            {
                return;
            }

            var isRevealed = _revealedApiNames.Contains(item.ApiName);
            item.DisplayName = ResolveDisplayName(achievement, isRevealed);
            item.Description = ResolveDescription(achievement, isRevealed);
            item.IconPath = ResolveIconPath(achievement, isRevealed);
            item.IsRevealed = isRevealed;

            OnPropertyChanged(nameof(AchievementOptions));
        }

        private void SetCompletedMarker()
        {
            if (SelectedOption == null || string.IsNullOrWhiteSpace(SelectedOption.ApiName))
            {
                ShowError(ResourceProvider.GetString("LOCPlayAch_CompletedMarker_Error_SelectRequired"));
                return;
            }

            var result = _achievementManager.SetCompletedMarker(_gameId, SelectedOption.ApiName);
            if (!result.Success)
            {
                ShowError(ResolveErrorMessage(result));
                return;
            }

            LoadData();
        }

        private void ClearCompletedMarker()
        {
            var result = _achievementManager.SetCompletedMarker(_gameId, null);
            if (!result.Success)
            {
                ShowError(ResolveErrorMessage(result));
                return;
            }

            LoadData();
        }

        private string ResolveErrorMessage(CacheWriteResult result)
        {
            var defaultMessage = ResourceProvider.GetString("LOCPlayAch_CompletedMarker_Error_SaveFailed");
            if (result == null)
            {
                return defaultMessage;
            }

            switch ((result.ErrorCode ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "invalid_game_id":
                    return ResourceProvider.GetString("LOCPlayAch_CompletedMarker_Error_InvalidGame") ?? defaultMessage;
                case "game_not_cached":
                    return ResourceProvider.GetString("LOCPlayAch_CompletedMarker_Error_NotCached") ?? defaultMessage;
                case "marker_not_found":
                    return ResourceProvider.GetString("LOCPlayAch_CompletedMarker_Error_MarkerNotFound") ?? defaultMessage;
                default:
                    if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                    {
                        return result.ErrorMessage;
                    }

                    return defaultMessage;
            }
        }

        private void ShowError(string message)
        {
            _playniteApi?.Dialogs?.ShowMessage(
                message ?? ResourceProvider.GetString("LOCPlayAch_CompletedMarker_Error_SaveFailed"),
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }

        private static string ResolveDisplayName(AchievementDetail achievement, bool isRevealed)
        {
            if (achievement.Hidden && !achievement.Unlocked && !isRevealed)
            {
                return ResourceProvider.GetString("LOCPlayAch_Achievements_HiddenTitle") ?? "Hidden Achievement";
            }

            return string.IsNullOrWhiteSpace(achievement.DisplayName)
                ? achievement.ApiName
                : achievement.DisplayName.Trim();
        }

        private static string ResolveDescription(AchievementDetail achievement, bool isRevealed)
        {
            if (achievement.Hidden && !achievement.Unlocked && !isRevealed)
            {
                return HiddenDescription;
            }

            return string.IsNullOrWhiteSpace(achievement.Description)
                ? string.Empty
                : achievement.Description.Trim();
        }

        private static string ResolveIconPath(AchievementDetail achievement, bool isRevealed)
        {
            if (achievement == null)
            {
                return HiddenIconPath;
            }

            if (achievement.Hidden && !achievement.Unlocked && !isRevealed)
            {
                return HiddenIconPath;
            }

            var icon = achievement.Unlocked
                ? FirstNonEmpty(achievement.UnlockedIconPath, achievement.LockedIconPath)
                : FirstNonEmpty(achievement.LockedIconPath, achievement.UnlockedIconPath);

            if (string.IsNullOrWhiteSpace(icon))
            {
                return HiddenIconPath;
            }

            if (!achievement.Unlocked)
            {
                return AchievementIconResolver.ApplyGrayPrefix(icon);
            }

            return icon;
        }

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private void SetCurrentMarkerText(string markerDisplayName)
        {
            var fallback = ResourceProvider.GetString("LOCPlayAch_CompletedMarker_CurrentMarker_None") ?? "None";
            var markerText = string.IsNullOrWhiteSpace(markerDisplayName) ? fallback : markerDisplayName.Trim();
            var format = ResourceProvider.GetString("LOCPlayAch_CompletedMarker_CurrentMarker");
            if (string.IsNullOrWhiteSpace(format))
            {
                format = "Current marker: {0}";
            }

            CurrentMarkerText = string.Format(format, markerText);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return null;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i].Trim();
                }
            }

            return null;
        }
    }

    public sealed class CompletedMarkerOptionItem : ObservableObject
    {
        private string _displayName;
        public string DisplayName
        {
            get => _displayName;
            set => SetValue(ref _displayName, value);
        }

        private string _description;
        public string Description
        {
            get => _description;
            set => SetValue(ref _description, value);
        }

        private string _iconPath;
        public string IconPath
        {
            get => _iconPath;
            set => SetValue(ref _iconPath, value);
        }

        private bool _isRevealed;
        public bool IsRevealed
        {
            get => _isRevealed;
            set => SetValue(ref _isRevealed, value);
        }

        public string ApiName { get; set; }
        public bool Unlocked { get; set; }
        public bool Hidden { get; set; }
        public bool IsCurrentMarker { get; set; }
    }
}

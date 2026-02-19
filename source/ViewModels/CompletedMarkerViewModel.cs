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

        private readonly Guid _gameId;
        private readonly AchievementManager _achievementManager;
        private readonly IPlayniteAPI _playniteApi;
        private readonly ILogger _logger;

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
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(this, EventArgs.Empty));

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
        public ICommand CancelCommand { get; }

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
                    var option = new CompletedMarkerOptionItem
                    {
                        ApiName = achievement.ApiName.Trim(),
                        DisplayName = string.IsNullOrWhiteSpace(achievement.DisplayName)
                            ? achievement.ApiName
                            : achievement.DisplayName.Trim(),
                        IconPath = ResolveIconPath(achievement),
                        Unlocked = achievement.Unlocked,
                        Hidden = achievement.Hidden,
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

            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void ClearCompletedMarker()
        {
            var result = _achievementManager.SetCompletedMarker(_gameId, null);
            if (!result.Success)
            {
                ShowError(ResolveErrorMessage(result));
                return;
            }

            RequestClose?.Invoke(this, EventArgs.Empty);
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

        private static string ResolveIconPath(AchievementDetail achievement)
        {
            if (achievement == null)
            {
                return HiddenIconPath;
            }

            if (achievement.Hidden && !achievement.Unlocked)
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

    public sealed class CompletedMarkerOptionItem
    {
        public string ApiName { get; set; }
        public string DisplayName { get; set; }
        public string IconPath { get; set; }
        public bool Unlocked { get; set; }
        public bool Hidden { get; set; }
        public bool IsCurrentMarker { get; set; }
    }
}

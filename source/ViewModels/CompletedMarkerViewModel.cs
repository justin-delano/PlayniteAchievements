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
        private readonly PlayniteAchievementsSettings _settings;

        public event EventHandler RequestClose;

        public CompletedMarkerViewModel(
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

            DoneCommand = new RelayCommand(_ => RequestClose?.Invoke(this, EventArgs.Empty));

            LoadData();
        }

        public ObservableCollection<CompletedMarkerOptionItem> AchievementOptions { get; } =
            new ObservableCollection<CompletedMarkerOptionItem>();

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

        private string _gameImagePath;
        public string GameImagePath
        {
            get => _gameImagePath;
            private set => SetValue(ref _gameImagePath, value);
        }

        private bool _useCoverAspect;
        public bool UseCoverAspect
        {
            get => _useCoverAspect;
            private set => SetValue(ref _useCoverAspect, value);
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

        public ICommand DoneCommand { get; }

        public void SetMarker(CompletedMarkerOptionItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ApiName))
            {
                return;
            }

            var result = _achievementManager.SetCompletedMarker(_gameId, item.ApiName);
            if (!result.Success)
            {
                ShowError(ResolveErrorMessage(result));
                return;
            }

            UpdateMarkerSelection(item);
        }

        public void ClearMarker()
        {
            var result = _achievementManager.SetCompletedMarker(_gameId, null);
            if (!result.Success)
            {
                ShowError(ResolveErrorMessage(result));
                return;
            }

            UpdateMarkerSelection(null);
        }

        public void ToggleReveal(CompletedMarkerOptionItem item)
        {
            if (item != null && item.CanReveal)
            {
                item.ToggleReveal();
            }
        }

        private void UpdateMarkerSelection(CompletedMarkerOptionItem newMarker)
        {
            foreach (var option in AchievementOptions)
            {
                option.IsCurrentMarker = option == newMarker;
            }

            SetCurrentMarkerText(newMarker?.DisplayName);
        }

        private void LoadData()
        {
            try
            {
                var game = _playniteApi?.Database?.Games?.Get(_gameId);
                GameName = game?.Name ?? ResourceProvider.GetString("LOCPlayAch_Text_UnknownGame") ?? "Unknown Game";

                UseCoverAspect = _settings?.Persisted?.UseCoverImages ?? false;

                if (game != null)
                {
                    if (UseCoverAspect && !string.IsNullOrEmpty(game.CoverImage))
                    {
                        GameImagePath = _playniteApi?.Database?.GetFullFilePath(game.CoverImage);
                    }
                    else if (!string.IsNullOrEmpty(game.Icon))
                    {
                        GameImagePath = _playniteApi?.Database?.GetFullFilePath(game.Icon);
                    }
                }

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
                    var option = CreateOptionItem(achievement, currentMarkerApiName);

                    if (option.IsCurrentMarker)
                    {
                        currentMarkerOption = option;
                    }

                    AchievementOptions.Add(option);
                }

                SetCurrentMarkerText(currentMarkerOption?.DisplayName);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed loading completed marker options for gameId={_gameId}");
                SetCurrentMarkerText(null);
            }
        }

        private CompletedMarkerOptionItem CreateOptionItem(AchievementDetail achievement, string currentMarkerApiName)
        {
            var actualIcon = ResolveActualIconPath(achievement);
            var hidden = achievement.Hidden && !achievement.Unlocked;

            return new CompletedMarkerOptionItem
            {
                ApiName = achievement.ApiName.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(achievement.DisplayName)
                    ? achievement.ApiName
                    : achievement.DisplayName.Trim(),
                Description = string.IsNullOrWhiteSpace(achievement.Description)
                    ? string.Empty
                    : achievement.Description.Trim(),
                ActualIconPath = actualIcon,
                HiddenIconPath = HiddenIconPath,
                Unlocked = achievement.Unlocked,
                Hidden = hidden,
                IsCurrentMarker = string.Equals(
                    achievement.ApiName.Trim(),
                    currentMarkerApiName,
                    StringComparison.OrdinalIgnoreCase)
            };
        }

        private static string ResolveActualIconPath(AchievementDetail achievement)
        {
            if (achievement == null)
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
            set
            {
                if (SetValueAndReturn(ref _displayName, value))
                {
                    OnPropertyChanged(nameof(DisplayNameResolved));
                }
            }
        }

        private string _description;
        public string Description
        {
            get => _description;
            set
            {
                if (SetValueAndReturn(ref _description, value))
                {
                    OnPropertyChanged(nameof(DescriptionResolved));
                }
            }
        }

        private string _actualIconPath;
        public string ActualIconPath
        {
            get => _actualIconPath;
            set
            {
                if (SetValueAndReturn(ref _actualIconPath, value))
                {
                    OnPropertyChanged(nameof(DisplayIcon));
                }
            }
        }

        private string _hiddenIconPath;
        public string HiddenIconPath
        {
            get => _hiddenIconPath;
            set
            {
                if (SetValueAndReturn(ref _hiddenIconPath, value))
                {
                    OnPropertyChanged(nameof(DisplayIcon));
                }
            }
        }

        private bool _isRevealed;
        public bool IsRevealed
        {
            get => _isRevealed;
            set
            {
                if (SetValueAndReturn(ref _isRevealed, value))
                {
                    OnPropertyChanged(nameof(CanReveal));
                    OnPropertyChanged(nameof(IsEffectivelyHidden));
                    OnPropertyChanged(nameof(DisplayIcon));
                    OnPropertyChanged(nameof(DisplayNameResolved));
                    OnPropertyChanged(nameof(DescriptionResolved));
                }
            }
        }

        private bool _isCurrentMarker;
        public bool IsCurrentMarker
        {
            get => _isCurrentMarker;
            set => SetValue(ref _isCurrentMarker, value);
        }

        public string ApiName { get; set; }
        public bool Unlocked { get; set; }
        public bool Hidden { get; set; }

        /// <summary>
        /// True if the achievement can be revealed (hidden and not unlocked).
        /// </summary>
        public bool CanReveal => Hidden && !Unlocked;

        /// <summary>
        /// True if the achievement is hidden and not yet revealed.
        /// </summary>
        public bool IsEffectivelyHidden => CanReveal && !IsRevealed;

        /// <summary>
        /// The icon to display - shows hidden icon if effectively hidden, otherwise actual icon.
        /// </summary>
        public string DisplayIcon => IsEffectivelyHidden ? HiddenIconPath : ActualIconPath;

        /// <summary>
        /// Display name - shows "Hidden Achievement" if effectively hidden.
        /// </summary>
        public string DisplayNameResolved
        {
            get
            {
                if (IsEffectivelyHidden)
                {
                    return ResourceProvider.GetString("LOCPlayAch_Achievements_HiddenTitle") ?? "Hidden Achievement";
                }
                return DisplayName;
            }
        }

        /// <summary>
        /// Description - shows "???" if effectively hidden.
        /// </summary>
        public string DescriptionResolved
        {
            get
            {
                if (IsEffectivelyHidden)
                {
                    return "???";
                }
                return Description;
            }
        }

        public void ToggleReveal()
        {
            if (CanReveal)
            {
                IsRevealed = !IsRevealed;
            }
        }
    }
}

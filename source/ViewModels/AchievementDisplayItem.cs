using System;
using System.Collections.Generic;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Achievements;
using Playnite.SDK;

using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.ViewModels
{
    /// <summary>
    /// Display model for achievements in views.
    /// </summary>
    public class AchievementDisplayItem : ObservableObject
    {
        private string _gameName;
        public string GameName { get => _gameName; set => SetValue(ref _gameName, value); }

        private Guid? _playniteGameId;
        public Guid? PlayniteGameId { get => _playniteGameId; set => SetValue(ref _playniteGameId, value); }

        private string _displayName;
        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (SetValueAndReturn(ref _displayName, value))
                {
                    OnPropertyChanged(nameof(Name));
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

        private string _iconPath;
        public string IconPath
        {
            get => _iconPath;
            set
            {
                if (SetValueAndReturn(ref _iconPath, value))
                {
                    OnPropertyChanged(nameof(DisplayIcon));
                    OnPropertyChanged(nameof(Icon));
                }
            }
        }

        private DateTime? _unlockTimeUtc;
        public DateTime? UnlockTimeUtc
        {
            get => _unlockTimeUtc;
            set
            {
                if (SetValueAndReturn(ref _unlockTimeUtc, value))
                {
                    OnPropertyChanged(nameof(UnlockTimeText));
                    OnPropertyChanged(nameof(DateUnlocked));
                }
            }
        }

        private double? _globalPercentUnlocked;
        public double? GlobalPercentUnlocked
        {
            get => _globalPercentUnlocked;
            set
            {
                if (SetValueAndReturn(ref _globalPercentUnlocked, value))
                {
                    OnPropertyChanged(nameof(GlobalPercentText));
                    OnPropertyChanged(nameof(Percent));
                    OnPropertyChanged(nameof(GamerScore));
                    OnPropertyChanged(nameof(RarityIconKey));
                }
            }
        }

        private bool _unlocked;
        public bool Unlocked
        {
            get => _unlocked;
            set
            {
                if (SetValueAndReturn(ref _unlocked, value))
                {
                    OnPropertyChanged(nameof(CanReveal));
                    OnPropertyChanged(nameof(IsHidden));
                    OnPropertyChanged(nameof(DisplayIcon));
                    OnPropertyChanged(nameof(Icon));
                    OnPropertyChanged(nameof(DisplayNameResolved));
                    OnPropertyChanged(nameof(DescriptionResolved));
                    OnPropertyChanged(nameof(IsUnlock));
                }
            }
        }

        private bool _hidden;
        public bool Hidden
        {
            get => _hidden;
            set
            {
                if (SetValueAndReturn(ref _hidden, value))
                {
                    OnPropertyChanged(nameof(CanReveal));
                    OnPropertyChanged(nameof(IsHidden));
                    OnPropertyChanged(nameof(DisplayIcon));
                    OnPropertyChanged(nameof(Icon));
                    OnPropertyChanged(nameof(DisplayNameResolved));
                    OnPropertyChanged(nameof(DescriptionResolved));
                }
            }
        }

        private string _apiName;
        public string ApiName { get => _apiName; set => SetValue(ref _apiName, value); }

        // Hidden achievement visibility settings
        private bool _hideHiddenIcon;
        public bool HideHiddenIcon
        {
            get => _hideHiddenIcon;
            set
            {
                if (SetValueAndReturn(ref _hideHiddenIcon, value))
                {
                    OnPropertyChanged(nameof(CanReveal));
                    OnPropertyChanged(nameof(IsHidden));
                    OnPropertyChanged(nameof(DisplayIcon));
                    OnPropertyChanged(nameof(Icon));
                }
            }
        }

        private bool _hideHiddenTitle;
        public bool HideHiddenTitle
        {
            get => _hideHiddenTitle;
            set
            {
                if (SetValueAndReturn(ref _hideHiddenTitle, value))
                {
                    OnPropertyChanged(nameof(CanReveal));
                    OnPropertyChanged(nameof(IsHidden));
                    OnPropertyChanged(nameof(DisplayNameResolved));
                }
            }
        }

        private bool _hideHiddenDescription;
        public bool HideHiddenDescription
        {
            get => _hideHiddenDescription;
            set
            {
                if (SetValueAndReturn(ref _hideHiddenDescription, value))
                {
                    OnPropertyChanged(nameof(CanReveal));
                    OnPropertyChanged(nameof(IsHidden));
                    OnPropertyChanged(nameof(DescriptionResolved));
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
                    OnPropertyChanged(nameof(IsHidden));
                    // Only notify icon changes if icon hiding is enabled
                    if (HideHiddenIcon)
                    {
                        OnPropertyChanged(nameof(DisplayIcon));
                        OnPropertyChanged(nameof(Icon));
                    }
                    // Only notify name changes if title hiding is enabled
                    if (HideHiddenTitle)
                    {
                        OnPropertyChanged(nameof(DisplayNameResolved));
                    }
                    // Only notify description changes if description hiding is enabled
                    if (HideHiddenDescription)
                    {
                        OnPropertyChanged(nameof(DescriptionResolved));
                    }
                }
            }
        }

        private int? _progressNum;
        public int? ProgressNum
        {
            get => _progressNum;
            set
            {
                if (SetValueAndReturn(ref _progressNum, value))
                {
                    OnPropertyChanged(nameof(HasProgress));
                    OnPropertyChanged(nameof(ProgressText));
                    OnPropertyChanged(nameof(ProgressPercent));
                }
            }
        }

        private int? _progressDenom;
        public int? ProgressDenom
        {
            get => _progressDenom;
            set
            {
                if (SetValueAndReturn(ref _progressDenom, value))
                {
                    OnPropertyChanged(nameof(HasProgress));
                    OnPropertyChanged(nameof(ProgressText));
                    OnPropertyChanged(nameof(ProgressPercent));
                }
            }
        }

        /// <summary>
        /// True if this achievement has progress data (both numerator and denominator are set).
        /// </summary>
        public bool HasProgress => ProgressNum.HasValue && ProgressDenom.HasValue && ProgressDenom.Value > 0;

        /// <summary>
        /// Text representation of progress as "ProgressNum / ProgressDenom".
        /// </summary>
        public string ProgressText => HasProgress ? $"{ProgressNum.Value} / {ProgressDenom.Value}" : string.Empty;

        /// <summary>
        /// Progress percentage (0-100) for progress bar binding.
        /// Returns 0 when no progress data exists.
        /// </summary>
        public double ProgressPercent => HasProgress ? (ProgressNum.Value * 100.0 / ProgressDenom.Value) : 0;

        /// <summary>
        /// True if the achievement can be revealed (is locked and at least one hiding setting is enabled).
        /// </summary>
        public bool CanReveal => (HideHiddenIcon || HideHiddenTitle || HideHiddenDescription) && !Unlocked && Hidden;

        /// <summary>
        /// True if the achievement details are currently hidden (can reveal and not yet revealed).
        /// </summary>
        public bool IsHidden => CanReveal && !IsRevealed;

        public string DisplayNameResolved
        {
            get
            {
                if (IsHidden && HideHiddenTitle) return ResourceProvider.GetString("LOCPlayAch_Achievements_HiddenTitle");
                return DisplayName;
            }
        }

        public string DescriptionResolved
        {
            get
            {
                if (IsHidden && HideHiddenDescription) return ResourceProvider.GetString("LOCPlayAch_Achievements_ClickToReveal");
                return Description;
            }
        }

        /// <summary>
        /// Toggles the revealed state if the achievement can be revealed.
        /// </summary>
        public void ToggleReveal()
        {
            if (CanReveal)
            {
                IsRevealed = !IsRevealed;
            }
        }
        
        /// <summary>
        /// Updates this item's properties from a source Achievement object.
        /// This is used to synchronize data without recreating the entire object, preventing UI flicker.
        /// </summary>
        public void UpdateFrom(Models.Achievements.AchievementDetail source, string gameName, Guid? playniteGameId, bool hideIcon, bool hideTitle, bool hideDescription)
        {
            GameName = gameName;
            PlayniteGameId = playniteGameId;
            DisplayName = source.DisplayName ?? source.ApiName ?? "Unknown Achievement";
            Description = source.Description ?? "No description";
            IconPath = source.IconPath;
            UnlockTimeUtc = source.UnlockTimeUtc;
            GlobalPercentUnlocked = source.GlobalPercentUnlocked;
            Unlocked = source.Unlocked;
            Hidden = source.Hidden;
            ApiName = source.ApiName;
            HideHiddenIcon = hideIcon;
            HideHiddenTitle = hideTitle;
            HideHiddenDescription = hideDescription;
            ProgressNum = source.ProgressNum;
            ProgressDenom = source.ProgressDenom;
        }

        public string UnlockTimeText =>
            UnlockTimeUtc.HasValue ? $"{DateTimeUtilities.AsLocalFromUtc(UnlockTimeUtc.Value):g}" : string.Empty;

        public string GlobalPercentText =>
            GlobalPercentUnlocked.HasValue ? $"{GlobalPercentUnlocked.Value:F1}%" : "?%";

        public string RarityIconKey => RarityHelper.GetRarityIconKey(GlobalPercentUnlocked ?? 100);

        private static string DefaultIcon => "pack://application:,,,/PlayniteAchievements;component/Resources/HiddenAchIcon.png";

        /// <summary>
        /// Returns the appropriate icon based on unlock state and hide settings.
        /// When hiding is enabled and achievement is locked and not revealed, shows the gray/hidden icon.
        /// Otherwise, uses IconPath with grayscale prefix when locked.
        /// </summary>
        public string DisplayIcon
        {
            get
            {
                if (IsHidden && HideHiddenIcon)
                {
                    return DefaultIcon;
                }

                var candidate = IconPath;
                if (!Unlocked && !string.IsNullOrWhiteSpace(candidate))
                {
                    candidate = AchievementIconResolver.ApplyGrayPrefix(candidate);
                }

                return !string.IsNullOrWhiteSpace(candidate) ? candidate : DefaultIcon;
            }
        }

        private static bool IsHttpUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The unlock time for sorting purposes.
        /// </summary>
        public DateTime UnlockTime => UnlockTimeUtc ?? DateTime.MinValue;

        /// <summary>
        /// The global percent for sorting purposes.
        /// </summary>
        public double GlobalPercent => GlobalPercentUnlocked ?? 100;

        // --- Theme integration compatibility (SuccessStory-style bindings) ---

        /// <summary>
        /// Alias for themes expecting a "Name" field (e.g. SuccessStory).
        /// </summary>
        public string Name => DisplayName;

        /// <summary>
        /// Alias for themes expecting an "Icon" field (e.g. SuccessStory).
        /// </summary>
        public string Icon => DisplayIcon;

        /// <summary>
        /// Alias for themes expecting a numeric "Percent" field (0-100).
        /// </summary>
        public double Percent => GlobalPercentUnlocked ?? 100;

        /// <summary>
        /// Alias for themes expecting a "DateUnlocked" field (local time).
        /// </summary>
        public DateTime? DateUnlocked => UnlockTimeUtc.HasValue ? DateTimeUtilities.AsLocalFromUtc(UnlockTimeUtc.Value) : (DateTime?)null;

        /// <summary>
        /// Alias for themes expecting an "IsUnlock" boolean.
        /// </summary>
        public bool IsUnlock => Unlocked;

        /// <summary>
        /// Theme-facing score used by some themes to select trophy visuals.
        /// This intentionally maps rarity tiers to a small set of expected values.
        /// </summary>
        public int GamerScore
        {
            get
            {
                var tier = RarityHelper.GetRarityTier(GlobalPercentUnlocked ?? 100);
                return tier switch
                {
                    RarityTier.UltraRare => 180,
                    RarityTier.Rare => 90,
                    RarityTier.Uncommon => 30,
                    _ => 10
                };
            }
        }

        /// <summary>
        /// Alias for themes expecting ImageUnlocked field (SuccessStory compatibility).
        /// Always returns IconPath (unlocked state uses the color icon).
        /// </summary>
        public string ImageUnlocked
        {
            get
            {
                var candidate = IconPath;
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return DefaultIcon;
                }

                return candidate;
            }
        }

        /// <summary>
        /// Alias for themes expecting ImageLocked field (SuccessStory compatibility).
        /// Returns IconPath with grayscale prefix for locked state.
        /// </summary>
        public string ImageLocked
        {
            get
            {
                var candidate = IconPath;
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return DefaultIcon;
                }

                return AchievementIconResolver.ApplyGrayPrefix(candidate);
            }
        }
    }
}

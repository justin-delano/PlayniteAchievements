using System;
using System.Collections.Generic;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Achievement;
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

        private string _unlockedIconUrl;
        public string UnlockedIconUrl
        {
            get => _unlockedIconUrl;
            set
            {
                if (SetValueAndReturn(ref _unlockedIconUrl, value))
                {
                    OnPropertyChanged(nameof(DisplayIcon));
                    OnPropertyChanged(nameof(Icon));
                }
            }
        }

        private string _lockedIconUrl;
        public string LockedIconUrl
        {
            get => _lockedIconUrl;
            set
            {
                if (SetValueAndReturn(ref _lockedIconUrl, value))
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

        // Hide locked achievements setting integration
        private bool _hideAchievementsLockedForSelf;
        public bool HideAchievementsLockedForSelf
        {
            get => _hideAchievementsLockedForSelf;
            set
            {
                if (SetValueAndReturn(ref _hideAchievementsLockedForSelf, value))
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
                    OnPropertyChanged(nameof(DisplayIcon));
                    OnPropertyChanged(nameof(Icon));
                    OnPropertyChanged(nameof(DisplayNameResolved));
                    OnPropertyChanged(nameof(DescriptionResolved));
                }
            }
        }

        /// <summary>
        /// True if the achievement can be revealed (is locked and hiding is enabled).
        /// </summary>
        public bool CanReveal => HideAchievementsLockedForSelf && !Unlocked && Hidden;

        /// <summary>
        /// True if the achievement details are currently hidden (can reveal and not yet revealed).
        /// </summary>
        public bool IsHidden => CanReveal && !IsRevealed;

        public string DisplayNameResolved
        {
            get
            {
                if (IsHidden) return ResourceProvider.GetString("LOCPlayAch_Achievements_HiddenTitle");
                return DisplayName;
            }
        }

        public string DescriptionResolved
        {
            get
            {
                if (IsHidden) return ResourceProvider.GetString("LOCPlayAch_Achievements_ClickToReveal");
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
        public void UpdateFrom(Models.Achievements.AchievementDetail source, string gameName, Guid? playniteGameId, bool hideLockedSetting)
        {
            GameName = gameName;
            PlayniteGameId = playniteGameId;
            DisplayName = source.DisplayName ?? source.ApiName ?? "Unknown Achievement";
            Description = source.Description ?? "No description";
            UnlockedIconUrl = source.UnlockedIconUrl;
            LockedIconUrl = source.LockedIconUrl;
            UnlockTimeUtc = source.UnlockTimeUtc;
            GlobalPercentUnlocked = source.GlobalPercentUnlocked;
            Unlocked = source.Unlocked;
            Hidden = source.Hidden;
            ApiName = source.ApiName;
            HideAchievementsLockedForSelf = hideLockedSetting;
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
        /// </summary>
        public string DisplayIcon
        {
            get
            {
                // If hiding locked achievements and this one is locked and not yet revealed, show placeholder icon
                if (IsHidden)
                {
                    return DefaultIcon;
                }

                // Normal logic: unlocked shows full icon, locked shows locked icon (grayscaled if identical).
                var candidate = Unlocked ? UnlockedIconUrl : LockedIconUrl;

                if (!Unlocked && AchievementIconResolver.AreSameIcon(LockedIconUrl, UnlockedIconUrl))
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
        /// </summary>
        public string ImageUnlocked
        {
            get
            {
                var candidate = UnlockedIconUrl;
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return DefaultIcon;
                }

                return candidate;
            }
        }

        /// <summary>
        /// Alias for themes expecting ImageLocked field (SuccessStory compatibility).
        /// </summary>
        public string ImageLocked
        {
            get
            {
                var candidate = LockedIconUrl;
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return DefaultIcon;
                }

                if (AchievementIconResolver.AreSameIcon(LockedIconUrl, UnlockedIconUrl))
                {
                    candidate = AchievementIconResolver.ApplyGrayPrefix(candidate);
                }

                return candidate;
            }
        }
    }
}

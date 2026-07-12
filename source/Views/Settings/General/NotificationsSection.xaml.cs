using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Views.Settings.General
{
    /// <summary>
    /// General settings: Notifications section. Hosts notification toggles, the live toast
    /// mockup and example toast buttons, toast behavior/appearance options, and screenshots.
    /// </summary>
    public partial class NotificationsSection : UserControl, IDisposable
    {
        /// <summary>
        /// Persisted setting names that affect the toast mockup rendering.
        /// </summary>
        private static readonly string[] ToastRefreshProperties =
        {
            nameof(PersistedSettings.ShowCompactListRarityBar),
            nameof(PersistedSettings.ShowHiddenIcon),
            nameof(PersistedSettings.ShowHiddenTitle),
            nameof(PersistedSettings.ShowHiddenDescription),
            nameof(PersistedSettings.ShowHiddenSuffix),
            nameof(PersistedSettings.ShowLockedIcon),
            nameof(PersistedSettings.UseSeparateLockedIconsWhenAvailable),
            nameof(PersistedSettings.UseUniformRarityBadges),
            nameof(PersistedSettings.RarityColors),
            nameof(PersistedSettings.ToastShowHeader),
            nameof(PersistedSettings.ToastShowName),
            nameof(PersistedSettings.ToastShowRarityBadge),
            nameof(PersistedSettings.ToastShowRarityGlow),
            nameof(PersistedSettings.ToastRarityColoredName),
            nameof(PersistedSettings.ToastShowRarityPercent),
            nameof(PersistedSettings.ToastShowDescription),
            nameof(PersistedSettings.ToastShowCategory),
            nameof(PersistedSettings.ToastShowGameName)
        };

        private readonly PlayniteAchievementsSettings _settings;
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly AchievementToastTemplateResolver _toastTemplateResolver;
        private readonly PersistedSettingsSubscription _persistedSubscription;

        public NotificationsSection()
        {
            InitializeComponent();
        }

        internal NotificationsSection(
            PlayniteAchievementsSettings settings,
            PlayniteAchievementsPlugin plugin,
            ILogger logger)
            : this()
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));

            _toastTemplateResolver = new AchievementToastTemplateResolver(plugin.PlayniteApi, logger);

            _persistedSubscription = new PersistedSettingsSubscription(
                _settings,
                OnPersistedPropertyChanged,
                UpdateToastMockup);

            Loaded += (s, e) => UpdateToastMockup();
        }

        private void OnPersistedPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (ToastRefreshProperties.Contains(e.PropertyName))
            {
                UpdateToastMockup();
            }
        }

        /// <summary>
        /// Rebuilds the inline toast mockup from the current persisted settings so the preview
        /// reflects appearance toggles (glow, rarity color, shown fields, badge colors) live.
        /// </summary>
        private void UpdateToastMockup()
        {
            if (ToastMockupHost == null)
            {
                return;
            }

            var persisted = _settings?.Persisted;
            if (persisted == null)
            {
                return;
            }

            ToastMockupHost.ContentTemplate = _toastTemplateResolver.ResolveTemplate();
            ToastMockupHost.Content = new AchievementToastViewModel(BuildToastPreviewArgs("mockup"), persisted);
        }

        private void ShowToastPreview(AchievementUnlockedEventArgs args)
        {
            if (args == null)
            {
                return;
            }

            PlayniteAchievementsPlugin.NotifyAchievementUnlocked(args);
        }

        private AchievementUnlockedEventArgs BuildToastPreviewArgs(string kind)
        {
            var sampleGame = L("LOCPlayAch_Settings_ToastPreviewSampleGame", "Sample Game");
            var sampleCategory = L("LOCPlayAch_Settings_ToastPreviewSampleCategory", "Sample Category");
            var sampleTitle = L("LOCPlayAch_Settings_ToastPreviewSampleTitle", "Example Achievement");
            var sampleDescription = L("LOCPlayAch_Settings_ToastPreviewSampleDescription", "An example achievement description.");

            switch (kind)
            {
                case "common":
                    return SampleUnlock("Common", 61.4, false);
                case "uncommon":
                    return SampleUnlock("Uncommon", 28.7, false);
                case "rare":
                    return SampleUnlock("Rare", 9.3, false);
                case "ultrarare":
                    return SampleUnlock("UltraRare", 1.8, false);
                case "capstone":
                    var capstone = SampleUnlock("UltraRare", 1.2, true);
                    capstone.GameCompleted = true;
                    return capstone;
                case "friend":
                    var friend = SampleUnlock("Rare", 7.5, false);
                    friend.IsFriendUnlock = true;
                    friend.FriendDisplayName = L("LOCPlayAch_Settings_ToastPreviewSampleFriend", "Friend");
                    friend.FriendAvatarUrl =
                        "pack://application:,,,/PlayniteAchievements;component/Resources/UnlockedAchIcon.png";
                    return friend;
                case "mockup":
                default:
                    return SampleUnlock("Rare", 9.3, false);
            }

            AchievementUnlockedEventArgs SampleUnlock(string rarity, double percent, bool capstone)
            {
                return new AchievementUnlockedEventArgs
                {
                    IsPreview = true,
                    GameName = sampleGame,
                    Category = sampleCategory,
                    DisplayName = sampleTitle,
                    Description = sampleDescription,
                    RarityTier = rarity,
                    GlobalPercent = percent,
                    IsCapstone = capstone,
                    UnlockedCount = 27,
                    TotalCount = 40
                };
            }
        }

        private void ToastPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { CommandParameter: string rarity })
            {
                ShowToastPreview(BuildToastPreviewArgs(rarity));
            }
        }

        private void ScreenshotDirectory_Browse_Click(object sender, RoutedEventArgs e)
        {
            var settings = _settings?.Persisted;
            if (settings == null)
            {
                return;
            }

            var selected = _plugin?.PlayniteApi?.Dialogs?.SelectFolder();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                settings.UnlockScreenshotDirectory = selected;
            }
        }

        public void Dispose()
        {
            _persistedSubscription?.Dispose();
        }

        private static string L(string key, string fallback)
        {
            var value = ResourceProvider.GetString(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}

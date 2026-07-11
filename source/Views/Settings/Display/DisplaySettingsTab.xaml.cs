using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Views.Settings.Navigation;

namespace PlayniteAchievements.Views.Settings.Display
{
    /// <summary>
    /// Display settings tab: a master-detail navigation over the six Display sections. Sections
    /// are created lazily when first selected.
    /// </summary>
    public partial class DisplaySettingsTab : UserControl, IDisposable
    {
        private ObservableCollection<SettingsNavigationItem> _navigationItems;

        private DisplayGeneralSection _generalSection;
        private AppearanceSection _appearanceSection;
        private OverviewDisplaySection _overviewSection;
        private FriendsOverviewDisplaySection _friendsOverviewSection;
        private AchievementsWindowDisplaySection _achievementsWindowSection;
        private StartPageDisplaySection _startPageSection;

        public DisplaySettingsTab()
        {
            InitializeComponent();
        }

        internal DisplaySettingsTab(
            PlayniteAchievementsSettings settings,
            PlayniteAchievementsPlugin plugin,
            ILogger logger,
            Func<Window, string, string> pickColor)
            : this()
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (plugin == null) throw new ArgumentNullException(nameof(plugin));
            if (pickColor == null) throw new ArgumentNullException(nameof(pickColor));

            _navigationItems = new ObservableCollection<SettingsNavigationItem>
            {
                new SettingsNavigationItem(
                    "General",
                    ResourceProvider.GetString("LOCPlayAch_Common_General"),
                    iconGlyph: "",
                    viewFactory: () => _generalSection =
                        new DisplayGeneralSection(settings, plugin, logger, OnDisplaySettingsReset)),
                new SettingsNavigationItem(
                    "Appearance",
                    ResourceProvider.GetString("LOCPlayAch_Settings_Appearance"),
                    iconGlyph: "",
                    viewFactory: () => _appearanceSection =
                        new AppearanceSection(settings, pickColor)),
                new SettingsNavigationItem(
                    "Overview",
                    ResourceProvider.GetString("LOCPlayAch_Settings_Display_OverviewSection"),
                    iconGlyph: "",
                    viewFactory: () => _overviewSection =
                        new OverviewDisplaySection()),
                new SettingsNavigationItem(
                    "FriendsOverview",
                    ResourceProvider.GetString("LOCPlayAch_Settings_Display_FriendsOverviewSection"),
                    iconGlyph: "",
                    viewFactory: () => _friendsOverviewSection =
                        new FriendsOverviewDisplaySection()),
                new SettingsNavigationItem(
                    "AchievementsWindow",
                    ResourceProvider.GetString("LOCPlayAch_Settings_ViewAchievementsWindow"),
                    iconGlyph: "",
                    viewFactory: () => _achievementsWindowSection =
                        new AchievementsWindowDisplaySection(settings)),
                new SettingsNavigationItem(
                    "StartPage",
                    ResourceProvider.GetString("LOCPlayAch_Settings_Display_StartPageSection"),
                    iconGlyph: "",
                    viewFactory: () => _startPageSection =
                        new StartPageDisplaySection(settings))
            };

            MasterDetail.ItemsSource = _navigationItems;
            MasterDetail.SelectedItem = _navigationItems[0];
        }

        /// <summary>
        /// Refreshes already-created sections after the General section reset display settings
        /// to defaults. Sections that do not exist yet pick up the new values on creation.
        /// </summary>
        private void OnDisplaySettingsReset()
        {
            _appearanceSection?.RefreshAppearanceEditorFromPersisted();
            _achievementsWindowSection?.RefreshMockPreviews();
        }

        public void Dispose()
        {
            _generalSection?.Dispose();
            _appearanceSection?.Dispose();
            _achievementsWindowSection?.Dispose();
            _startPageSection?.Dispose();
        }
    }
}

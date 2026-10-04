using System;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Views.Settings.Navigation;
using PlayniteAchievements.Views.Workshop;

namespace PlayniteAchievements.Views.Settings.Workshop
{
    /// <summary>
    /// Workshop settings tab: everything the community Workshop offers in one place, as five
    /// left-nav pages. Browse and Installed host the same control the scoped Workshop window
    /// uses, one pane each; Themes bundles and unbundles the global look; Presets lists every
    /// saved preset across kinds; Account holds the sharer identity and the endpoint overrides.
    /// Pages are created lazily when first selected, so the index is fetched only when Browse
    /// or Installed is opened.
    /// </summary>
    public partial class WorkshopSettingsTab : UserControl, IDisposable
    {
        private ObservableCollection<SettingsNavigationItem> _navigationItems;

        private WorkshopControl _browse;
        private WorkshopControl _installed;
        private WorkshopThemesSection _themes;
        private WorkshopPresetsSection _presets;
        private WorkshopAccountSection _account;

        public WorkshopSettingsTab()
        {
            InitializeComponent();
        }

        internal WorkshopSettingsTab(
            PlayniteAchievementsSettings settings,
            PlayniteAchievementsPlugin plugin,
            ILogger logger)
            : this()
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (plugin == null) throw new ArgumentNullException(nameof(plugin));

            _navigationItems = new ObservableCollection<SettingsNavigationItem>
            {
                new SettingsNavigationItem(
                    "Browse",
                    ResourceProvider.GetString("LOCPlayAch_Workshop_Tab_Browse"),
                    iconGlyph: "",
                    viewFactory: () => _browse =
                        new WorkshopControl(plugin, logger, null, null, WorkshopPane.Browse)),
                new SettingsNavigationItem(
                    "Installed",
                    ResourceProvider.GetString("LOCPlayAch_Workshop_Tab_Installed"),
                    iconGlyph: "",
                    viewFactory: () => _installed =
                        new WorkshopControl(plugin, logger, null, null, WorkshopPane.Installed)),
                new SettingsNavigationItem(
                    "Themes",
                    ResourceProvider.GetString("LOCPlayAch_Workshop_Kind_Theme"),
                    iconGlyph: "",
                    viewFactory: () => _themes =
                        new WorkshopThemesSection(settings, plugin, logger)),
                new SettingsNavigationItem(
                    "Presets",
                    ResourceProvider.GetString("LOCPlayAch_Presets_Header"),
                    iconGlyph: "",
                    viewFactory: () => _presets =
                        new WorkshopPresetsSection(plugin, logger)),
                new SettingsNavigationItem(
                    "Account",
                    ResourceProvider.GetString("LOCPlayAch_Workshop_Account"),
                    iconGlyph: "",
                    viewFactory: () => _account =
                        new WorkshopAccountSection(settings, plugin, logger))
            };

            MasterDetail.ItemsSource = _navigationItems;
            MasterDetail.SelectedItem = _navigationItems[0];
        }

        public void Dispose()
        {
            _browse?.Cleanup();
            _installed?.Cleanup();
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Views.Settings.Navigation;
using PlayniteAchievements.Views.Workshop;

namespace PlayniteAchievements.Views.Settings.Workshop
{
    /// <summary>
    /// Workshop settings tab: everything the community Workshop offers in one place, as five
    /// left-nav pages. Browse and My submissions host the same control the scoped Workshop
    /// window uses, one pane each; Library lists every saved look and Workshop item with where
    /// it is used; Bundles packs and unpacks the global look; Account holds the sharer identity
    /// and the endpoint overrides. Pages are created lazily when first selected, so the index
    /// is fetched only when one of the Workshop panes is opened.
    /// </summary>
    public partial class WorkshopSettingsTab : UserControl, IDisposable
    {
        /// <summary>The key of the Library page.</summary>
        public const string LibraryPageKey = "Library";

        private ObservableCollection<SettingsNavigationItem> _navigationItems;

        private WorkshopControl _browse;
        private LibraryControl _library;
        private WorkshopControl _submissions;
        private WorkshopBundlesSection _bundles;
        private WorkshopAccountSection _account;

        public WorkshopSettingsTab()
        {
            InitializeComponent();
        }

        internal WorkshopSettingsTab(
            PlayniteAchievementsSettings settings,
            PlayniteAchievementsPlugin plugin,
            ILogger logger,
            string initialPageKey = null)
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
                    LibraryPageKey,
                    ResourceProvider.GetString("LOCPlayAch_Showcase_Template_Library"),
                    iconGlyph: "",
                    viewFactory: () => _library = new LibraryControl(plugin, logger)),
                new SettingsNavigationItem(
                    "Submissions",
                    ResourceProvider.GetString("LOCPlayAch_Workshop_MySubmissions"),
                    iconGlyph: "",
                    viewFactory: () => _submissions =
                        new WorkshopControl(plugin, logger, null, null, WorkshopPane.Submissions)),
                new SettingsNavigationItem(
                    "Bundles",
                    ResourceProvider.GetString("LOCPlayAch_Workshop_Kind_Bundle"),
                    iconGlyph: "",
                    viewFactory: () => _bundles =
                        new WorkshopBundlesSection(settings, plugin, logger)),
                new SettingsNavigationItem(
                    "Account",
                    ResourceProvider.GetString("LOCPlayAch_Workshop_Account"),
                    iconGlyph: "",
                    viewFactory: () => _account =
                        new WorkshopAccountSection(settings, plugin, logger))
            };

            // Pages are created on selection, so a settings window opened on another page starts
            // there rather than building Browse first.
            MasterDetail.ItemsSource = _navigationItems;
            MasterDetail.SelectedItem = FindPage(initialPageKey) ?? _navigationItems[0];
        }

        /// <summary>Selects the page with the given key (e.g. <see cref="LibraryPageKey"/>).</summary>
        public void NavigateToPage(string key)
        {
            var item = FindPage(key);
            if (item != null)
            {
                MasterDetail.SelectedItem = item;
            }
        }

        private SettingsNavigationItem FindPage(string key)
        {
            return _navigationItems?.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
        }

        public void Dispose()
        {
            _browse?.Cleanup();
            _library?.Cleanup();
            _submissions?.Cleanup();
        }
    }
}

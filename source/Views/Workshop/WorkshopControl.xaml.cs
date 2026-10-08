using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Services.Workshop;
using PlayniteAchievements.ViewModels.Workshop;
using PlayniteAchievements.Views.Dialogs;
using PlayniteAchievements.Views.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PlayniteAchievements.Views.Workshop
{
    /// <summary>Which of the Workshop panes a control shows: every tab, or Browse alone without the tab strip.</summary>
    public enum WorkshopPane
    {
        Full,
        Browse
    }

    /// <summary>
    /// The Workshop window: browse and install community items (the ones this install shared
    /// among them, under Mine), and the library they go into.
    /// </summary>
    public partial class WorkshopControl : UserControl
    {
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly ILogger _logger;
        private LibraryControl _library;
        private bool _loadedOnce;

        public WorkshopControl()
        {
            InitializeComponent();
        }

        internal WorkshopControl(PlayniteAchievementsPlugin plugin, ILogger logger, Guid? focusGameId, WorkshopItemKind? focusKind, WorkshopPane pane = WorkshopPane.Full)
            : this()
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;

            var viewModel = new WorkshopViewModel(plugin, logger, focusGameId, focusKind)
            {
                PickBundleParts = PickBundleParts,
                PickGame = PickGame,
                Confirm = Confirm,
                ChooseMergeOrReplace = ChooseMergeOrReplace,
                ShowPreview = (row, model) => WorkshopPreviewDialog.Show(plugin, row, model, Window.GetWindow(this))
            };
            DataContext = viewModel;
            viewModel.ItemsView.CollectionChanged += ItemsView_CollectionChanged;

            if (pane != WorkshopPane.Full)
            {
                // Hosted as the Browse settings page: that tab alone, with the tab strip hidden. A
                // collapsed TabItem still presents its content while it is the selected one.
                Tabs.SelectedItem = BrowseTab;
                foreach (var item in Tabs.Items.OfType<TabItem>())
                {
                    item.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                // The window's Library tab; settings host the Library page on its own. Opened for
                // a game or for game data, it starts on game data, with that game's selected.
                var focusGameData = focusGameId.HasValue || focusKind == WorkshopItemKind.GameCustomData;
                _library = new LibraryControl(
                    plugin,
                    logger,
                    focusGameData ? Services.Library.LibraryItemKind.GameData : LibraryKindOf(focusKind),
                    focusGameId);
                LibraryTab.Content = _library;
            }
        }

        /// <summary>The library kind a window scoped to one Workshop kind starts filtered to; bundles have none.</summary>
        private static Services.Library.LibraryItemKind? LibraryKindOf(WorkshopItemKind? kind)
        {
            switch (kind)
            {
                case WorkshopItemKind.Colors: return Services.Library.LibraryItemKind.Colors;
                case WorkshopItemKind.UnlockSounds: return Services.Library.LibraryItemKind.Sounds;
                case WorkshopItemKind.NotificationStyle: return Services.Library.LibraryItemKind.Toast;
                case WorkshopItemKind.ScreenshotFrame: return Services.Library.LibraryItemKind.Frame;
                case WorkshopItemKind.ShowcasePage: return Services.Library.LibraryItemKind.ShowcasePage;
                default: return null;
            }
        }

        private WorkshopViewModel ViewModel => DataContext as WorkshopViewModel;

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_loadedOnce || ViewModel == null)
            {
                return;
            }

            _loadedOnce = true;
            await ViewModel.LoadAsync();
            UpdateEmptyText();
        }

        public void Cleanup()
        {
            if (ViewModel != null)
            {
                ViewModel.ItemsView.CollectionChanged -= ItemsView_CollectionChanged;
                ViewModel.Dispose();
            }

            _library?.Cleanup();
        }

        private void ItemsView_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateEmptyText();
        }

        private void UpdateEmptyText()
        {
            var viewModel = ViewModel;
            if (viewModel == null || EmptyText == null)
            {
                return;
            }

            var any = viewModel.ItemsView.Cast<object>().Any();
            EmptyText.Visibility = !any && !viewModel.IsLoading && !viewModel.HasError ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Opens the clicked cover or preview, whose path the border carries in Tag, full size.</summary>
        private void DetailImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is string path)
            {
                MediaLightboxPresenter.Show(this, path, false);
                e.Handled = true;
            }
        }

        private BundleParts? PickBundleParts(WorkshopItemViewModel item, BundleParts available)
        {
            // From a part's tab only that part starts ticked; from Bundles or All, everything does.
            var preferred = ViewModel?.PreferredBundleParts ?? BundleParts.All;
            PartPickerItem Part(BundleParts part, string key) =>
                new PartPickerItem(part, ResourceProvider.GetString(key), isChecked: preferred.HasFlag(part), isEnabled: available.HasFlag(part));

            var items = new[]
            {
                Part(BundleParts.Colors, "LOCPlayAch_Settings_Display_Colors"),
                Part(BundleParts.Sounds, "LOCPlayAch_Workshop_Share_Sounds"),
                Part(BundleParts.Toast, "LOCPlayAch_Settings_Style_ToastTab"),
                Part(BundleParts.Frame, "LOCPlayAch_Settings_FrameHeader")
            };

            var selected = PartPickerDialog.Show(item.Name, item.Description, items, Window.GetWindow(this));
            if (selected == null)
            {
                return null;
            }

            return selected.OfType<BundleParts>().Aggregate(BundleParts.None, (acc, part) => acc | part);
        }

        private Game PickGame(WorkshopItemViewModel item, IReadOnlyList<Game> games)
        {
            if (games == null || games.Count == 0)
            {
                _plugin.PlayniteApi?.Dialogs?.ShowMessage(
                    string.Format(ResourceProvider.GetString("LOCPlayAch_Workshop_NoGameMatch"), item.GameName ?? item.Name),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return null;
            }

            return PlayniteGamePickerDialog.Pick(
                Window.GetWindow(this),
                games,
                ResourceProvider.GetString("LOCPlayAch_Workshop_PickGame"),
                item.GameName ?? string.Empty);
        }

        private WorkshopGameDataInstallMode? ChooseMergeOrReplace(string message)
        {
            var dialogs = _plugin.PlayniteApi?.Dialogs;
            if (dialogs == null)
            {
                return null;
            }

            var merge = new MessageBoxOption(ResourceProvider.GetString("LOCPlayAch_Common_Merge"), isDefault: true);
            var replace = new MessageBoxOption(ResourceProvider.GetString("LOCPlayAch_Button_Replace"));
            var cancel = new MessageBoxOption(ResourceProvider.GetString("LOCCancelLabel"), isCancel: true);
            var picked = dialogs.ShowMessage(
                message,
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                MessageBoxImage.Question,
                new List<MessageBoxOption> { merge, replace, cancel });
            if (picked == merge)
            {
                return WorkshopGameDataInstallMode.MergeKeepingExisting;
            }

            return picked == replace ? WorkshopGameDataInstallMode.Replace : (WorkshopGameDataInstallMode?)null;
        }

        private bool Confirm(string message)
        {
            return _plugin.PlayniteApi?.Dialogs?.ShowMessage(
                       message,
                       ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                       MessageBoxButton.YesNo,
                       MessageBoxImage.Question) == MessageBoxResult.Yes;
        }
    }
}

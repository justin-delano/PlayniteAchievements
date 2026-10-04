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

namespace PlayniteAchievements.Views.Workshop
{
    /// <summary>Which of the Workshop panes a control shows: both tabs, or one of them without the tab strip.</summary>
    public enum WorkshopPane
    {
        Full,
        Browse,
        Installed
    }

    /// <summary>
    /// The Workshop window: browse and install community items, see what is installed and revert
    /// recent installs, and share this install's customizations.
    /// </summary>
    public partial class WorkshopControl : UserControl
    {
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly ILogger _logger;
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
                PickThemeParts = PickThemeParts,
                PickGame = PickGame,
                Confirm = Confirm
            };
            DataContext = viewModel;
            viewModel.ItemsView.CollectionChanged += ItemsView_CollectionChanged;

            if (pane != WorkshopPane.Full)
            {
                // Hosted as one settings page: that tab alone, with the tab strip hidden. A
                // collapsed TabItem still presents its content while it is the selected one.
                Tabs.SelectedIndex = pane == WorkshopPane.Browse ? 0 : 1;
                foreach (var item in Tabs.Items.OfType<TabItem>())
                {
                    item.Visibility = Visibility.Collapsed;
                }
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

        private ThemePackParts? PickThemeParts(WorkshopItemViewModel item, ThemePackParts available)
        {
            // From a part's tab only that part starts ticked; from Themes or All, everything does.
            var preferred = ViewModel?.PreferredThemeParts ?? ThemePackParts.All;
            PartPickerItem Part(ThemePackParts part, string key) =>
                new PartPickerItem(part, ResourceProvider.GetString(key), isChecked: preferred.HasFlag(part), isEnabled: available.HasFlag(part));

            var items = new[]
            {
                Part(ThemePackParts.Colors, "LOCPlayAch_Settings_Display_Colors"),
                Part(ThemePackParts.Sounds, "LOCPlayAch_Workshop_Share_Sounds"),
                Part(ThemePackParts.Toast, "LOCPlayAch_Settings_Style_ToastTab"),
                Part(ThemePackParts.Frame, "LOCPlayAch_Settings_FrameHeader")
            };

            var selected = PartPickerDialog.Show(item.Name, item.Description, items, Window.GetWindow(this));
            if (selected == null)
            {
                return null;
            }

            return selected.OfType<ThemePackParts>().Aggregate(ThemePackParts.None, (acc, part) => acc | part);
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

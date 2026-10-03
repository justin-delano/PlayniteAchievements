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

        internal WorkshopControl(PlayniteAchievementsPlugin plugin, ILogger logger, Guid? focusGameId)
            : this()
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;

            var viewModel = new WorkshopViewModel(plugin, logger, focusGameId)
            {
                PickThemeParts = PickThemeParts,
                PickGame = PickGame,
                Confirm = Confirm
            };
            DataContext = viewModel;
            viewModel.ItemsView.CollectionChanged += ItemsView_CollectionChanged;
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
            var items = new[]
            {
                new PartPickerItem(ThemePackParts.Colors, ResourceProvider.GetString("LOCPlayAch_Settings_Display_Colors"), isEnabled: available.HasFlag(ThemePackParts.Colors)),
                new PartPickerItem(ThemePackParts.Sounds, ResourceProvider.GetString("LOCPlayAch_Workshop_Share_Sounds"), isEnabled: available.HasFlag(ThemePackParts.Sounds)),
                new PartPickerItem(ThemePackParts.Toast, ResourceProvider.GetString("LOCPlayAch_Settings_Style_ToastTab"), isEnabled: available.HasFlag(ThemePackParts.Toast)),
                new PartPickerItem(ThemePackParts.Frame, ResourceProvider.GetString("LOCPlayAch_Settings_FrameHeader"), isEnabled: available.HasFlag(ThemePackParts.Frame))
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

        private void Share_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is WorkshopShareCandidate candidate) || ViewModel == null)
            {
                return;
            }

            var dialog = new WorkshopShareDialog(_plugin, _logger, candidate, ViewModel.ShareService, ViewModel.Registry);
            var window = PlayniteUiProvider.CreateExtensionWindow(
                ResourceProvider.GetString("LOCPlayAch_Workshop_Share"),
                dialog,
                new WindowOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false,
                    ShowCloseButton = true,
                    CanBeResizable = true,
                    Width = 620,
                    Height = 640
                });

            try
            {
                if (window.Owner == null)
                {
                    window.Owner = Window.GetWindow(this);
                }
            }
            catch (InvalidOperationException)
            {
            }

            dialog.RequestClose += (s, args) => window.Close();
            window.ShowDialog();
            ViewModel.OnShared();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.StartPage
{
    public partial class StartPageGamesOverviewGridView : UserControl, IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private ColumnWidthPersistenceService _columnPersistence;
        private DataGridRow _pendingRightClickRow;
        private bool _isAttached;

        private static readonly IReadOnlyDictionary<string, double> DefaultWidthSeeds =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["OverviewGameName"] = 280,
                ["OverviewProgression"] = 190,
                ["UnlockedAchievements"] = 110,
                ["TotalAchievements"] = 90,
                ["LastUnlock"] = 150,
                ["OverviewProvider"] = 130,
                ["OverviewCollectionScore"] = 125,
                ["OverviewPrestigeScore"] = 120
            };

        public StartPageGamesOverviewGridView()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_isAttached)
            {
                return;
            }

            var settings = PlayniteAchievementsPlugin.Instance?.Settings;
            if (settings?.Persisted == null || GamesOverviewDataGrid == null)
            {
                return;
            }

            _columnPersistence = new ColumnWidthPersistenceService(
                GamesOverviewDataGrid,
                Logger,
                () => settings.Persisted.StartPageGamesOverviewColumnWidths,
                map => settings.Persisted.StartPageGamesOverviewColumnWidths = map,
                () => settings.Persisted.StartPageGamesOverviewColumnVisibility,
                map => settings.Persisted.StartPageGamesOverviewColumnVisibility = map,
                () => SavePluginSettings(settings),
                DefaultWidthSeeds);
            _columnPersistence.Attach();
            _isAttached = true;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Dispose();
        }

        private void DataGridColumnMenu_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is DataGrid grid))
            {
                return;
            }

            var header = VisualTreeHelpers.FindVisualParent<DataGridColumnHeader>(
                e.OriginalSource as DependencyObject);
            if (header?.Column == null)
            {
                return;
            }

            e.Handled = true;
            var menu = _columnPersistence?.BuildColumnVisibilityMenu();
            if (menu == null || menu.Items.Count == 0)
            {
                return;
            }

            menu.Placement = PlacementMode.Bottom;
            menu.PlacementTarget = header;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 0;
            menu.IsOpen = true;
        }

        private void GridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!TryResolveContextMenuRow(sender, e, out var row))
            {
                return;
            }

            e.Handled = true;
            _pendingRightClickRow = row;
        }

        private void GridRow_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!TryResolveContextMenuRow(sender, e, out var row))
            {
                return;
            }

            e.Handled = true;
            var targetRow = _pendingRightClickRow ?? row;
            _pendingRightClickRow = null;
            OpenContextMenuForRow(targetRow);
        }

        private static bool TryResolveContextMenuRow(object sender, MouseButtonEventArgs e, out DataGridRow row)
        {
            row = sender as DataGridRow
                  ?? e?.Source as DataGridRow
                  ?? VisualTreeHelpers.FindVisualParent<DataGridRow>(e?.OriginalSource as DependencyObject);
            return row != null;
        }

        private bool OpenContextMenuForRow(DataGridRow row)
        {
            if (row == null || !row.IsLoaded || row.DataContext == null)
            {
                return false;
            }

            var menu = PlayniteAchievementsPlugin.Instance?.BuildStartPageRowContextMenu(
                row.DataContext,
                this,
                RefreshAfterRowOptionsChanged);
            if (menu == null || menu.Items.Count == 0)
            {
                return false;
            }

            row.ContextMenu = menu;
            menu.Placement = PlacementMode.MousePoint;
            menu.PlacementTarget = row;
            menu.IsOpen = true;
            return true;
        }

        private void RefreshAfterRowOptionsChanged()
        {
            PlayniteAchievementsPlugin.Instance?.PersistSettingsForUi();
            PlayniteAchievementsPlugin.Instance?.InvalidateStartPageDataForUi();
            _columnPersistence?.Refresh();
        }

        private static void SavePluginSettings(PlayniteAchievementsSettings settings)
        {
            var plugin = PlayniteAchievementsPlugin.Instance;
            if (plugin == null || settings == null)
            {
                return;
            }

            try
            {
                plugin.SavePluginSettings(settings);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to persist StartPage games overview column settings.");
            }
        }

        public void Dispose()
        {
            if (!_isAttached)
            {
                return;
            }

            _columnPersistence?.Dispose();
            _columnPersistence = null;
            _isAttached = false;
        }
    }
}

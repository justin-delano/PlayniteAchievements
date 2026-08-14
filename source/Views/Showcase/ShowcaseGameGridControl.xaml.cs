using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels.Items;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.Showcase
{
    /// <summary>
    /// Hosts the shared game-summaries grid inside showcase widget bodies and owns the
    /// row context menu (shared game row menu plus pin reorder when enabled).
    /// </summary>
    public partial class ShowcaseGameGridControl : UserControl
    {
        private DataGridRow _pendingRightClickRow;

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(IEnumerable),
                typeof(ShowcaseGameGridControl),
                new PropertyMetadata(null));

        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty ColumnSettingsKeyProperty =
            DependencyProperty.Register(
                nameof(ColumnSettingsKey),
                typeof(string),
                typeof(ShowcaseGameGridControl),
                new PropertyMetadata("ShowcaseGameSummaries"));

        public string ColumnSettingsKey
        {
            get => (string)GetValue(ColumnSettingsKeyProperty);
            set => SetValue(ColumnSettingsKeyProperty, value);
        }

        public static readonly DependencyProperty UseCoverImagesProperty =
            DependencyProperty.Register(
                nameof(UseCoverImages),
                typeof(bool),
                typeof(ShowcaseGameGridControl),
                new PropertyMetadata(false));

        public bool UseCoverImages
        {
            get => (bool)GetValue(UseCoverImagesProperty);
            set => SetValue(UseCoverImagesProperty, value);
        }

        public static readonly DependencyProperty ShowColumnHeadersProperty =
            DependencyProperty.Register(
                nameof(ShowColumnHeaders),
                typeof(bool),
                typeof(ShowcaseGameGridControl),
                new PropertyMetadata(true));

        public bool ShowColumnHeaders
        {
            get => (bool)GetValue(ShowColumnHeadersProperty);
            set => SetValue(ShowColumnHeadersProperty, value);
        }

        public static readonly DependencyProperty FixedRowHeightProperty =
            DependencyProperty.Register(
                nameof(FixedRowHeight),
                typeof(double?),
                typeof(ShowcaseGameGridControl),
                new PropertyMetadata(null));

        public double? FixedRowHeight
        {
            get => (double?)GetValue(FixedRowHeightProperty);
            set => SetValue(FixedRowHeightProperty, value);
        }

        public static readonly DependencyProperty EnablePinReorderProperty =
            DependencyProperty.Register(
                nameof(EnablePinReorder),
                typeof(bool),
                typeof(ShowcaseGameGridControl),
                new PropertyMetadata(false));

        /// <summary>Adds move earlier/later items for games that are current showcase pins.</summary>
        public bool EnablePinReorder
        {
            get => (bool)GetValue(EnablePinReorderProperty);
            set => SetValue(EnablePinReorderProperty, value);
        }

        public ShowcaseGameGridControl()
        {
            InitializeComponent();
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

        private void OpenContextMenuForRow(DataGridRow row)
        {
            if (row == null || !row.IsLoaded || row.DataContext == null)
            {
                return;
            }

            var menu = PlayniteAchievementsPlugin.Instance?.BuildStartPageRowContextMenu(
                row.DataContext,
                this,
                RefreshAfterRowOptionsChanged);
            if (menu == null)
            {
                return;
            }

            AppendPinReorderItems(menu, row.DataContext);
            if (menu.Items.Count == 0)
            {
                return;
            }

            ContextMenuStyleHelper.ApplyAchievementContextMenuStyle(InnerGrid, menu);
            row.ContextMenu = menu;
            menu.Placement = PlacementMode.MousePoint;
            menu.PlacementTarget = row;
            menu.IsOpen = true;
        }

        private void AppendPinReorderItems(ContextMenu menu, object data)
        {
            if (!EnablePinReorder ||
                data is FriendGameSummaryItem ||
                !(data is GameSummaryItem game) ||
                game.PlayniteGameId.HasValue == false)
            {
                return;
            }

            var gameId = game.PlayniteGameId.Value;
            var showcase = PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.Showcase;
            if (showcase == null || !ShowcasePinService.IsGamePinned(showcase, gameId))
            {
                return;
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMoveItem("LOCPlayAch_Showcase_MoveEarlier", -1, gameId));
            menu.Items.Add(CreateMoveItem("LOCPlayAch_Showcase_MoveLater", 1, gameId));
        }

        private MenuItem CreateMoveItem(string headerKey, int direction, System.Guid gameId)
        {
            var item = new MenuItem();
            item.SetResourceReference(HeaderedItemsControl.HeaderProperty, headerKey);
            item.Click += (_, __) =>
            {
                var showcase = PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.Showcase;
                if (showcase != null &&
                    ShowcasePinService.MoveGame(showcase, gameId, direction))
                {
                    ShowcaseConfigurationCommit.Commit();
                }
            };
            return item;
        }

        private void RefreshAfterRowOptionsChanged()
        {
            PlayniteAchievementsPlugin.Instance?.PersistSettingsForUi();
            ShowcaseConfigurationEvents.RaiseChanged();
            PlayniteAchievementsPlugin.Instance?.InvalidateStartPageDataForUi();
            InnerGrid?.Refresh();
        }
    }
}

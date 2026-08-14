using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.Showcase
{
    /// <summary>
    /// Hosts the shared achievement grid inside showcase widget bodies and owns the
    /// row context menu (shared row options plus pin reorder when enabled).
    /// </summary>
    public partial class ShowcaseAchievementGridControl : UserControl
    {
        private DataGridRow _pendingRightClickRow;

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(IEnumerable),
                typeof(ShowcaseAchievementGridControl),
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
                typeof(ShowcaseAchievementGridControl),
                new PropertyMetadata("ShowcaseRecentAchievements"));

        public string ColumnSettingsKey
        {
            get => (string)GetValue(ColumnSettingsKeyProperty);
            set => SetValue(ColumnSettingsKeyProperty, value);
        }

        public static readonly DependencyProperty UseCoverImagesProperty =
            DependencyProperty.Register(
                nameof(UseCoverImages),
                typeof(bool),
                typeof(ShowcaseAchievementGridControl),
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
                typeof(ShowcaseAchievementGridControl),
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
                typeof(ShowcaseAchievementGridControl),
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
                typeof(ShowcaseAchievementGridControl),
                new PropertyMetadata(false));

        /// <summary>Adds move earlier/later items for rows that are current showcase pins.</summary>
        public bool EnablePinReorder
        {
            get => (bool)GetValue(EnablePinReorderProperty);
            set => SetValue(EnablePinReorderProperty, value);
        }

        public ShowcaseAchievementGridControl()
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
                !ShowcasePinService.TryGetAchievementIdentity(
                    data,
                    out var gameId,
                    out var apiName,
                    out _,
                    out _,
                    out _))
            {
                return;
            }

            var showcase = PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.Showcase;
            if (showcase == null ||
                !ShowcasePinService.IsAchievementPinned(showcase, gameId, apiName))
            {
                return;
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMoveItem("LOCPlayAch_Showcase_MoveEarlier", -1, gameId, apiName));
            menu.Items.Add(CreateMoveItem("LOCPlayAch_Showcase_MoveLater", 1, gameId, apiName));
        }

        private MenuItem CreateMoveItem(string headerKey, int direction, System.Guid gameId, string apiName)
        {
            var item = new MenuItem();
            item.SetResourceReference(HeaderedItemsControl.HeaderProperty, headerKey);
            item.Click += (_, __) =>
            {
                var showcase = PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.Showcase;
                if (showcase != null &&
                    ShowcasePinService.MoveAchievement(showcase, gameId, apiName, direction))
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

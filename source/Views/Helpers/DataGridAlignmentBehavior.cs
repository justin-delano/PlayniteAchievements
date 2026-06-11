using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.Helpers
{
    public static class DataGridAlignmentBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(DataGridAlignmentBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static readonly DependencyProperty HeaderHorizontalAlignmentProperty =
            DependencyProperty.RegisterAttached(
                "HeaderHorizontalAlignment",
                typeof(HorizontalAlignment),
                typeof(DataGridAlignmentBehavior),
                new FrameworkPropertyMetadata(
                    HorizontalAlignment.Center,
                    FrameworkPropertyMetadataOptions.Inherits));

        public static readonly DependencyProperty CellHorizontalAlignmentProperty =
            DependencyProperty.RegisterAttached(
                "CellHorizontalAlignment",
                typeof(HorizontalAlignment),
                typeof(DataGridAlignmentBehavior),
                new FrameworkPropertyMetadata(
                    HorizontalAlignment.Left,
                    FrameworkPropertyMetadataOptions.Inherits));

        public static readonly DependencyProperty CellVerticalAlignmentProperty =
            DependencyProperty.RegisterAttached(
                "CellVerticalAlignment",
                typeof(VerticalAlignment),
                typeof(DataGridAlignmentBehavior),
                new FrameworkPropertyMetadata(
                    VerticalAlignment.Center,
                    FrameworkPropertyMetadataOptions.Inherits));

        public static readonly DependencyProperty CellTextAlignmentProperty =
            DependencyProperty.RegisterAttached(
                "CellTextAlignment",
                typeof(TextAlignment),
                typeof(DataGridAlignmentBehavior),
                new FrameworkPropertyMetadata(
                    TextAlignment.Left,
                    FrameworkPropertyMetadataOptions.Inherits));

        public static readonly DependencyProperty ColumnCellAlignmentOverridesProviderProperty =
            DependencyProperty.RegisterAttached(
                "ColumnCellAlignmentOverridesProvider",
                typeof(Func<Dictionary<string, GridAlignment>>),
                typeof(DataGridAlignmentBehavior),
                new PropertyMetadata(null, OnColumnCellAlignmentOverridesProviderChanged));

        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached(
                "State",
                typeof(AlignmentState),
                typeof(DataGridAlignmentBehavior),
                new PropertyMetadata(null));

        public static bool GetIsEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsEnabledProperty, value);
        }

        public static HorizontalAlignment GetHeaderHorizontalAlignment(DependencyObject obj)
        {
            return (HorizontalAlignment)obj.GetValue(HeaderHorizontalAlignmentProperty);
        }

        public static void SetHeaderHorizontalAlignment(DependencyObject obj, HorizontalAlignment value)
        {
            obj.SetValue(HeaderHorizontalAlignmentProperty, value);
        }

        public static HorizontalAlignment GetCellHorizontalAlignment(DependencyObject obj)
        {
            return (HorizontalAlignment)obj.GetValue(CellHorizontalAlignmentProperty);
        }

        public static void SetCellHorizontalAlignment(DependencyObject obj, HorizontalAlignment value)
        {
            obj.SetValue(CellHorizontalAlignmentProperty, value);
        }

        public static VerticalAlignment GetCellVerticalAlignment(DependencyObject obj)
        {
            return (VerticalAlignment)obj.GetValue(CellVerticalAlignmentProperty);
        }

        public static void SetCellVerticalAlignment(DependencyObject obj, VerticalAlignment value)
        {
            obj.SetValue(CellVerticalAlignmentProperty, value);
        }

        public static TextAlignment GetCellTextAlignment(DependencyObject obj)
        {
            return (TextAlignment)obj.GetValue(CellTextAlignmentProperty);
        }

        public static void SetCellTextAlignment(DependencyObject obj, TextAlignment value)
        {
            obj.SetValue(CellTextAlignmentProperty, value);
        }

        public static Func<Dictionary<string, GridAlignment>> GetColumnCellAlignmentOverridesProvider(DependencyObject obj)
        {
            return (Func<Dictionary<string, GridAlignment>>)obj.GetValue(ColumnCellAlignmentOverridesProviderProperty);
        }

        public static void SetColumnCellAlignmentOverridesProvider(
            DependencyObject obj,
            Func<Dictionary<string, GridAlignment>> value)
        {
            obj.SetValue(ColumnCellAlignmentOverridesProviderProperty, value);
        }

        public static void Refresh(DependencyObject obj)
        {
            if (obj is DataGrid grid)
            {
                GetState(grid)?.ApplyAlignment();
            }
        }

        private static AlignmentState GetState(DependencyObject obj)
        {
            return (AlignmentState)obj.GetValue(StateProperty);
        }

        private static void SetState(DependencyObject obj, AlignmentState value)
        {
            obj.SetValue(StateProperty, value);
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is DataGrid grid))
            {
                return;
            }

            var isEnabled = e.NewValue is bool value && value;
            var state = GetState(grid);
            if (isEnabled)
            {
                if (state == null)
                {
                    state = new AlignmentState(grid);
                    SetState(grid, state);
                }

                state.Attach();
                return;
            }

            state?.Detach();
            SetState(grid, null);
        }

        private static void OnColumnCellAlignmentOverridesProviderChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            Refresh(d);
        }

        private static PlayniteAchievementsSettings ResolveSettingsSource(DependencyObject source)
        {
            var current = source;
            while (current != null)
            {
                if (current is FrameworkElement element)
                {
                    var settings = ResolveFromDataContext(element.DataContext);
                    if (settings != null)
                    {
                        return settings;
                    }
                }

                current = GetParent(current);
            }

            return PlayniteAchievementsPlugin.Instance?.Settings;
        }

        private static PlayniteAchievementsSettings ResolveFromDataContext(object dataContext)
        {
            if (dataContext is PlayniteAchievementsSettings settings)
            {
                return settings;
            }

            if (dataContext is ThemePreviewContext previewContext)
            {
                return previewContext.Settings;
            }

            return null;
        }

        private static DependencyObject GetParent(DependencyObject current)
        {
            if (current == null)
            {
                return null;
            }

            DependencyObject parent = null;
            try
            {
                parent = VisualTreeHelper.GetParent(current);
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            return parent ?? LogicalTreeHelper.GetParent(current);
        }

        private sealed class AlignmentState
        {
            private readonly DataGrid _grid;
            private bool _isAttached;
            private PlayniteAchievementsSettings _settings;
            private PersistedSettings _persisted;

            public AlignmentState(DataGrid grid)
            {
                _grid = grid;
            }

            public void Attach()
            {
                if (_isAttached)
                {
                    return;
                }

                _grid.Loaded += OnLoaded;
                _grid.Unloaded += OnUnloaded;
                _grid.DataContextChanged += OnDataContextChanged;
                _grid.LoadingRow += OnLoadingRow;
                _isAttached = true;

                if (_grid.IsLoaded)
                {
                    AttachSettings();
                }

                ApplyAlignment();
            }

            public void Detach()
            {
                if (!_isAttached)
                {
                    return;
                }

                _grid.Loaded -= OnLoaded;
                _grid.Unloaded -= OnUnloaded;
                _grid.DataContextChanged -= OnDataContextChanged;
                _grid.LoadingRow -= OnLoadingRow;
                DetachSettings();
                _isAttached = false;
            }

            private void OnLoaded(object sender, RoutedEventArgs e)
            {
                AttachSettings();
                ApplyAlignment();
            }

            private void OnUnloaded(object sender, RoutedEventArgs e)
            {
                DetachSettings();
            }

            private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
            {
                AttachSettings();
                ApplyAlignment();
            }

            private void OnLoadingRow(object sender, DataGridRowEventArgs e)
            {
                QueueApplyColumnCellAlignments(e.Row, retryWhenEmpty: true);
            }

            private void AttachSettings()
            {
                var settings = ResolveSettingsSource(_grid);
                var persisted = settings?.Persisted;
                if (ReferenceEquals(settings, _settings) &&
                    ReferenceEquals(persisted, _persisted))
                {
                    return;
                }

                DetachSettings();

                _settings = settings;
                _persisted = persisted;

                if (_settings != null)
                {
                    _settings.PropertyChanged += Settings_PropertyChanged;
                }

                if (_persisted != null)
                {
                    _persisted.PropertyChanged += Persisted_PropertyChanged;
                }
            }

            private void DetachSettings()
            {
                if (_settings != null)
                {
                    _settings.PropertyChanged -= Settings_PropertyChanged;
                }

                if (_persisted != null)
                {
                    _persisted.PropertyChanged -= Persisted_PropertyChanged;
                }

                _settings = null;
                _persisted = null;
            }

            private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                if (string.IsNullOrEmpty(e?.PropertyName) ||
                    e.PropertyName == nameof(PlayniteAchievementsSettings.Persisted))
                {
                    AttachSettings();
                    ApplyAlignment();
                }
            }

            private void Persisted_PropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                if (string.IsNullOrEmpty(e?.PropertyName) ||
                    e.PropertyName == nameof(PersistedSettings.GridColumnHeaderAlignment) ||
                    e.PropertyName == nameof(PersistedSettings.GridCellAlignment) ||
                    e.PropertyName == nameof(PersistedSettings.GridCellVerticalAlignment) ||
                    IsColumnCellAlignmentProperty(e.PropertyName))
                {
                    ApplyAlignment();
                }
            }

            public void ApplyAlignment()
            {
                var headerAlignment = _persisted?.GridColumnHeaderAlignment ?? GridAlignment.Center;
                var cellAlignment = _persisted?.GridCellAlignment ?? GridAlignment.Left;
                var cellVerticalAlignment = _persisted?.GridCellVerticalAlignment ?? GridVerticalAlignment.Center;

                SetHeaderHorizontalAlignment(
                    _grid,
                    GridAlignmentMapping.ToHorizontalAlignment(headerAlignment));
                SetCellHorizontalAlignment(
                    _grid,
                    GridAlignmentMapping.ToHorizontalAlignment(cellAlignment));
                SetCellVerticalAlignment(
                    _grid,
                    GridAlignmentMapping.ToVerticalAlignment(cellVerticalAlignment));
                SetCellTextAlignment(
                    _grid,
                    GridAlignmentMapping.ToTextAlignment(cellAlignment));

                ApplyColumnCellAlignments();
            }

            private void ApplyColumnCellAlignments()
            {
                if (_grid?.Items == null)
                {
                    return;
                }

                foreach (var item in _grid.Items)
                {
                    if (_grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                    {
                        ApplyColumnCellAlignments(row);
                    }
                }
            }

            private void QueueApplyColumnCellAlignments(DataGridRow row, bool retryWhenEmpty = false)
            {
                if (row == null)
                {
                    return;
                }

                row.Dispatcher.BeginInvoke(
                    new Action(() => ApplyColumnCellAlignments(row, retryWhenEmpty)),
                    DispatcherPriority.Loaded);
            }

            private void ApplyColumnCellAlignments(DataGridRow row, bool retryWhenEmpty = false)
            {
                if (row == null)
                {
                    return;
                }

                var cells = FindVisualChildren<DataGridCell>(row).ToList();
                if (cells.Count == 0)
                {
                    if (retryWhenEmpty)
                    {
                        row.Dispatcher.BeginInvoke(
                            new Action(() => ApplyColumnCellAlignments(row)),
                            DispatcherPriority.ContextIdle);
                    }

                    return;
                }

                var overrides = GetColumnCellAlignmentOverridesProvider(_grid)?.Invoke();
                foreach (var cell in cells)
                {
                    ApplyColumnCellAlignment(cell, overrides);
                }
            }

            private void ApplyColumnCellAlignment(
                DataGridCell cell,
                IReadOnlyDictionary<string, GridAlignment> overrides)
            {
                if (cell?.Column == null)
                {
                    return;
                }

                var key = ResolveColumnKey(cell.Column);
                if (string.IsNullOrWhiteSpace(key) ||
                    overrides == null ||
                    !overrides.TryGetValue(key, out var alignment))
                {
                    cell.ClearValue(CellHorizontalAlignmentProperty);
                    cell.ClearValue(CellTextAlignmentProperty);
                    return;
                }

                SetCellHorizontalAlignment(cell, GridAlignmentMapping.ToHorizontalAlignment(alignment));
                SetCellTextAlignment(cell, GridAlignmentMapping.ToTextAlignment(alignment));
            }

            private static string ResolveColumnKey(DataGridColumn column)
            {
                if (column == null)
                {
                    return null;
                }

                var key = ColumnVisibilityHelper.GetColumnKey(column);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    return key;
                }

                return !string.IsNullOrWhiteSpace(column.SortMemberPath)
                    ? column.SortMemberPath
                    : null;
            }

            private static bool IsColumnCellAlignmentProperty(string propertyName)
            {
                return propertyName == nameof(PersistedSettings.OverviewRecentAchievementColumnAlignments) ||
                       propertyName == nameof(PersistedSettings.OverviewSelectedGameAchievementColumnAlignments) ||
                       propertyName == nameof(PersistedSettings.SingleGameColumnAlignments) ||
                       propertyName == nameof(PersistedSettings.DesktopThemeColumnAlignments) ||
                       propertyName == nameof(PersistedSettings.GameSummariesColumnAlignments) ||
                       propertyName == nameof(PersistedSettings.StartPageAchievementColumnAlignments) ||
                       propertyName == nameof(PersistedSettings.StartPageGameSummariesColumnAlignments);
            }

            private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
                where T : DependencyObject
            {
                if (parent == null)
                {
                    yield break;
                }

                var count = 0;
                try
                {
                    count = VisualTreeHelper.GetChildrenCount(parent);
                }
                catch (InvalidOperationException)
                {
                    yield break;
                }

                for (var i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    if (child is T typed)
                    {
                        yield return typed;
                    }

                    foreach (var nested in FindVisualChildren<T>(child))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }
}

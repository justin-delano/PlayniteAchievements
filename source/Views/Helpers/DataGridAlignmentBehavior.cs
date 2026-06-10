using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
                    e.PropertyName == nameof(PersistedSettings.GridCellVerticalAlignment))
                {
                    ApplyAlignment();
                }
            }

            private void ApplyAlignment()
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
            }
        }
    }
}

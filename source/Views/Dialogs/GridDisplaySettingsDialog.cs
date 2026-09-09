using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Views.Helpers;
using PlayniteAchievements.Views.Settings.Controls;

namespace PlayniteAchievements.Views.Dialogs
{
    /// <summary>
    /// The per-grid display settings popup, opened from a grid's own context menu.
    ///
    /// Edits the LIVE grid options catalog records rather than clones, so changes apply to the
    /// grid behind the window immediately. Persistence is therefore not this window's Save button
    /// (it has none) but one of two paths:
    ///
    /// - With no settings edit session pending, a debounced write, matching the showcase widget
    ///   options control.
    /// - While a settings window holds a pending edit snapshot, no write at all: the settings
    ///   window's OK/Cancel decides, so a grid changed behind an open settings window follows that
    ///   window's Save/Revert. A Revert replaces the whole persisted instance, which this window
    ///   detects and rebinds to.
    /// </summary>
    public sealed class GridDisplaySettingsDialog : UserControl
    {
        private readonly GridOptionKind _kind;
        private readonly string _surfaceKey;
        private readonly string _categorySurfaceKey;
        private readonly List<INotifyPropertyChanged> _records = new List<INotifyPropertyChanged>();

        private GridOptionsEditor _primaryEditor;
        private GridOptionsEditor _categoryEditor;
        private CheckBox _hideCategoryRowCheckBox;
        private DispatcherTimer _persistTimer;
        private PersistedSettingsSubscription _persistedSubscription;

        private GridDisplaySettingsDialog(GridOptionKind kind, string surfaceKey, string categorySurfaceKey)
        {
            _kind = kind;
            _surfaceKey = surfaceKey;
            _categorySurfaceKey = categorySurfaceKey;

            // The editor merges its own dictionaries, but this control's own header and checkbox
            // need the settings window's keyed and implicit styles too.
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/PlayniteAchievements;component/Resources/SettingsScopedControlStyles.xaml",
                    UriKind.Absolute)
            });

            Content = BuildContent();
            FormattingCulture.Apply(this);

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// Opens the display settings for one grid surface. <paramref name="categorySurfaceKey"/>
        /// adds the grid's nested category sub-grid section, and applies only to achievement
        /// surfaces.
        /// </summary>
        public static void Show(GridOptionKind kind, string surfaceKey, string categorySurfaceKey)
        {
            if (string.IsNullOrEmpty(surfaceKey))
            {
                return;
            }

            var editor = new GridDisplaySettingsDialog(kind, surfaceKey, categorySurfaceKey);
            var title = string.Format(
                FormattingCulture.Current,
                Localize("LOCPlayAch_Showcase_WidgetSettingsTitle"),
                Localize(GridDisplaySurfaces.ResolveTitleKey(kind, surfaceKey)));

            var window = PlayniteUiProvider.CreateExtensionWindow(
                title,
                editor,
                new WindowOptions
                {
                    Width = WindowWidth,
                    Height = editor.MeasureContentHeight(),
                    CanBeResizable = true,
                    ShowCloseButton = true,
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false
                });
            window.ShowDialog();
        }

        private const double WindowWidth = 480;
        private const double ContentWidth = WindowWidth - 28;
        private const double FallbackHeight = 620;
        private const double MinimumHeight = 260;

        private static string Localize(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var value = ResourceProvider.GetString(key);
            return string.IsNullOrWhiteSpace(value) ? key : value;
        }

        private static GridOptionsCatalog Catalog =>
            PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.GridOptions;

        private UIElement BuildContent()
        {
            var panel = new StackPanel();

            _primaryEditor = new GridOptionsEditor
            {
                SurfaceKind = _kind,
                SurfaceKey = _surfaceKey
            };
            panel.Children.Add(_primaryEditor);

            // Only achievement grids host a category sub-grid, and only that grid's own record
            // carries the hide-after-selection flag.
            if (_kind == GridOptionKind.Achievement && !string.IsNullOrEmpty(_categorySurfaceKey))
            {
                var header = new TextBlock
                {
                    Text = Localize("LOCPlayAch_Settings_CategoryGrid"),
                    Margin = new Thickness(0, 12, 0, 6)
                };
                header.SetResourceReference(StyleProperty, "SubSectionHeaderStyle");
                panel.Children.Add(header);

                _hideCategoryRowCheckBox = new CheckBox
                {
                    Content = Localize("LOCPlayAch_Settings_HideCategorySummaryRow"),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                panel.Children.Add(_hideCategoryRowCheckBox);

                _categoryEditor = new GridOptionsEditor
                {
                    SurfaceKind = GridOptionKind.CategorySummaries,
                    SurfaceKey = _categorySurfaceKey
                };
                panel.Children.Add(_categoryEditor);
            }

            BindRecords();

            return new ScrollViewer
            {
                Margin = new Thickness(12),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = panel
            };
        }

        /// <summary>
        /// Points the editors at the current catalog records. Called again when the persisted
        /// instance is replaced, so the window keeps editing the tree that is actually live.
        /// </summary>
        private void BindRecords()
        {
            DetachRecords();

            var catalog = Catalog;
            if (catalog == null)
            {
                return;
            }

            var primary = ResolveRecord(catalog, _kind, _surfaceKey);
            if (_primaryEditor != null)
            {
                _primaryEditor.Options = primary;
            }

            Track(primary);

            if (_categoryEditor != null)
            {
                var category = catalog.GetCategorySummaries(_categorySurfaceKey);
                _categoryEditor.Options = category;
                Track(category);
            }

            if (_hideCategoryRowCheckBox != null)
            {
                // Rebound rather than left pointing at the previous record's property.
                BindingOperations.ClearBinding(_hideCategoryRowCheckBox, ToggleButton.IsCheckedProperty);
                if (primary != null)
                {
                    _hideCategoryRowCheckBox.SetBinding(ToggleButton.IsCheckedProperty, new Binding(
                        nameof(AchievementGridOptions.HideCategorySummaryRow))
                    {
                        Source = primary,
                        Mode = BindingMode.TwoWay
                    });
                }
            }
        }

        private static object ResolveRecord(GridOptionsCatalog catalog, GridOptionKind kind, string surfaceKey)
        {
            switch (kind)
            {
                case GridOptionKind.Achievement:
                    return catalog.GetAchievement(surfaceKey);
                case GridOptionKind.GameSummaries:
                    return catalog.GetGameSummaries(surfaceKey);
                case GridOptionKind.FriendSummaries:
                    return catalog.GetFriendSummaries(surfaceKey);
                case GridOptionKind.CategorySummaries:
                    return catalog.GetCategorySummaries(surfaceKey);
                default:
                    return null;
            }
        }

        private void Track(object record)
        {
            if (record is INotifyPropertyChanged observable)
            {
                _records.Add(observable);
                if (IsLoaded)
                {
                    observable.PropertyChanged -= OnRecordChanged;
                    observable.PropertyChanged += OnRecordChanged;
                }
            }
        }

        private void DetachRecords()
        {
            foreach (var record in _records)
            {
                record.PropertyChanged -= OnRecordChanged;
            }

            _records.Clear();
        }

        /// <summary>
        /// Sizes the window to the rows the surface actually offers. The editor's root is a stack
        /// panel and its visible row count is fixed by the capability table, so a measure pass is
        /// accurate; the window is resizable and the content scrolls, so an inaccurate measure
        /// only costs some empty space.
        /// </summary>
        private double MeasureContentHeight()
        {
            try
            {
                Measure(new Size(ContentWidth, double.PositiveInfinity));
                var desired = DesiredSize.Height + 56;
                if (double.IsNaN(desired) || double.IsInfinity(desired) || desired <= 0)
                {
                    return FallbackHeight;
                }

                var ceiling = SystemParameters.WorkArea.Height * 0.85;
                return Math.Min(Math.Max(desired, MinimumHeight), ceiling);
            }
            catch (Exception)
            {
                return FallbackHeight;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            foreach (var record in _records)
            {
                record.PropertyChanged -= OnRecordChanged;
                record.PropertyChanged += OnRecordChanged;
            }

            var settings = PlayniteAchievementsPlugin.Instance?.Settings;
            if (settings != null && _persistedSubscription == null)
            {
                _persistedSubscription = new PersistedSettingsSubscription(
                    settings,
                    null,
                    OnPersistedInstanceChanged);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DetachRecords();
            _persistedSubscription?.Dispose();
            _persistedSubscription = null;
            FlushPendingPersist();
        }

        /// <summary>
        /// A settings window Revert replaces the whole persisted instance, orphaning the records
        /// these editors were bound to. Drop the pending write (the reverted state is the intended
        /// one) and rebind to the new tree so the window shows the reverted values.
        /// </summary>
        private void OnPersistedInstanceChanged()
        {
            _persistTimer?.Stop();
            BindRecords();
        }

        private void OnRecordChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_persistTimer == null)
            {
                _persistTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(600)
                };
                _persistTimer.Tick += (_, __) => FlushPendingPersist();
            }

            // Restart the window on every edit so a burst of toggles produces one write.
            _persistTimer.Stop();
            _persistTimer.Start();
        }

        private void FlushPendingPersist()
        {
            if (_persistTimer == null || !_persistTimer.IsEnabled)
            {
                return;
            }

            _persistTimer.Stop();

            var plugin = PlayniteAchievementsPlugin.Instance;
            if (plugin == null || plugin.IsSettingsEditSessionActive)
            {
                // A settings window owns the write; its OK/Cancel governs these edits.
                return;
            }

            plugin.PersistSettingsForUi();
        }
    }
}

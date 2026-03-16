using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK.Models;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.Desktop
{
    /// <summary>
    /// Desktop PlayniteAchievements list control for theme integration.
    /// Displays achievements in a DataGrid with sorting and virtualization.
    /// Clones items to maintain independent reveal state per control instance.
    /// </summary>
    public partial class AchievementDataGridControl : ThemeControlBase
    {
        // Cache the source reference to avoid unnecessary cloning when data hasn't changed
        private List<AchievementDisplayItem> _lastSourceItems;

        // Sort state tracking
        private string _currentSortPath;
        private ListSortDirection? _currentSortDirection;

        /// <summary>
        /// Identifies the DisplayItems dependency property.
        /// </summary>
        public static readonly DependencyProperty DisplayItemsProperty =
            DependencyProperty.Register(nameof(DisplayItems), typeof(ObservableCollection<AchievementDisplayItem>),
                typeof(AchievementDataGridControl), new PropertyMetadata(null));

        /// <summary>
        /// Gets the display items for the list.
        /// </summary>
        public ObservableCollection<AchievementDisplayItem> DisplayItems
        {
            get => (ObservableCollection<AchievementDisplayItem>)GetValue(DisplayItemsProperty);
            private set => SetValue(DisplayItemsProperty, value);
        }

        #region ThemeDataOverride Property

        /// <summary>
        /// Identifies the ThemeDataOverride dependency property.
        /// When set, this override is used instead of Plugin.Settings.Theme for data binding.
        /// Used by settings preview to inject mock data.
        /// </summary>
        public static readonly DependencyProperty ThemeDataOverrideProperty =
            DependencyProperty.Register(nameof(ThemeDataOverride), typeof(ThemeData),
                typeof(AchievementDataGridControl), new PropertyMetadata(null, OnThemeDataOverrideChanged));

        /// <summary>
        /// Gets or sets a ThemeData override for preview purposes.
        /// When null (default), uses Plugin.Settings.Theme.
        /// When set, uses this instance instead (for settings preview).
        /// </summary>
        public ThemeData ThemeDataOverride
        {
            get => (ThemeData)GetValue(ThemeDataOverrideProperty);
            set => SetValue(ThemeDataOverrideProperty, value);
        }

        private static void OnThemeDataOverrideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AchievementDataGridControl control && control.IsLoaded)
            {
                control._lastSourceItems = null;
                control.LoadData();
            }
        }

        #endregion

        public AchievementDataGridControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        /// <summary>
        /// Gets a value indicating whether this control should subscribe to theme data change notifications.
        /// </summary>
        protected override bool EnableAutomaticThemeDataUpdates => true;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateMaxHeight();
            LoadData();
        }

        private void UpdateMaxHeight()
        {
            var settings = Plugin?.Settings?.Persisted;
            if (settings == null || AchievementsGrid == null)
            {
                return;
            }

            // Convert nullable double to double (null = NaN means unlimited)
            AchievementsGrid.DataGridMaxHeight = settings.AchievementDataGridMaxHeight ?? double.NaN;
        }

        private void LoadData()
        {
            var theme = ThemeDataOverride ?? Plugin?.Settings?.Theme;
            var sourceItems = theme?.AllAchievementDisplayItems;
            if (sourceItems == null)
            {
                _lastSourceItems = null;
                if (DisplayItems == null)
                {
                    DisplayItems = new ObservableCollection<AchievementDisplayItem>();
                }
                else
                {
                    DisplayItems.Clear();
                }
                return;
            }

            // Skip cloning if the source reference hasn't changed
            if (ReferenceEquals(sourceItems, _lastSourceItems))
            {
                return;
            }

            _lastSourceItems = sourceItems;
            var clonedItems = sourceItems.Select(item => item.Clone()).ToList();

            // Reapply current sort if active
            if (!string.IsNullOrWhiteSpace(_currentSortPath) && _currentSortDirection.HasValue)
            {
                AchievementDisplayItemSorter.SortItems(
                    clonedItems,
                    _currentSortPath,
                    _currentSortDirection.Value,
                    ref _currentSortPath,
                    ref _currentSortDirection);
            }

            // Initialize or synchronize collection
            if (DisplayItems == null)
            {
                DisplayItems = new ObservableCollection<AchievementDisplayItem>(clonedItems);
            }
            else
            {
                CollectionHelper.SynchronizeCollection(DisplayItems, clonedItems);
            }
        }

        /// <summary>
        /// Determines whether a change raised from ThemeData should trigger a refresh.
        /// </summary>
        protected override bool ShouldHandleThemeDataChange(string propertyName)
        {
            return propertyName == nameof(Models.ThemeIntegration.ThemeData.AllAchievementDisplayItems);
        }

        /// <summary>
        /// Called when theme data changes and the list should be refreshed.
        /// </summary>
        protected override void OnThemeDataUpdated()
        {
            LoadData();
        }

        /// <summary>
        /// Called when the game context changes for this control.
        /// </summary>
        public override void GameContextChanged(Game oldContext, Game newContext)
        {
            if (IsLoaded)
            {
                LoadData();
            }
        }

        private void AchievementsGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            // Handle sort indicators
            var sortDirection = DataGridSortingHelper.HandleSorting(sender, e, AchievementsGrid?.InternalDataGrid);
            if (sortDirection == null) return;

            // Perform the actual sorting
            ApplySorting(e.Column.SortMemberPath, sortDirection.Value);

            // Mark as handled to prevent DataGrid's default sorting
            e.Handled = true;
        }

        private void ApplySorting(string sortMemberPath, ListSortDirection direction)
        {
            if (DisplayItems == null || DisplayItems.Count == 0) return;

            // Sort to a new list
            var items = DisplayItems.ToList();
            AchievementDisplayItemSorter.SortItems(
                items,
                sortMemberPath,
                direction,
                ref _currentSortPath,
                ref _currentSortDirection);

            // Synchronize in place to trigger efficient UI updates
            CollectionHelper.SynchronizeCollection(DisplayItems, items);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.Desktop
{
    /// <summary>
    /// Base class for compact list controls that get data directly from AchievementService.
    /// Provides filtering by unlock state and overflow limiting.
    /// </summary>
    public abstract class AchievementCompactListControlBase : ThemeControlBase
    {
        /// <summary>
        /// Gets a value indicating whether this control should subscribe to theme data change notifications.
        /// </summary>
        protected override bool EnableAutomaticThemeDataUpdates => true;

        #region Dependency Properties

        /// <summary>
        /// Identifies the IconSize dependency property.
        /// </summary>
        public static readonly DependencyProperty IconSizeProperty =
            DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(AchievementCompactListControlBase),
                new PropertyMetadata(48.0));

        /// <summary>
        /// Gets or sets the size of each achievement icon.
        /// Default is 48 to match legacy SuccessStory styling.
        /// </summary>
        public double IconSize
        {
            get => (double)GetValue(IconSizeProperty);
            set => SetValue(IconSizeProperty, value);
        }

        /// <summary>
        /// Identifies the VisibleCount dependency property.
        /// </summary>
        public static readonly DependencyProperty VisibleCountProperty =
            DependencyProperty.Register(nameof(VisibleCount), typeof(int), typeof(AchievementCompactListControlBase),
                new PropertyMetadata(0, OnVisibleCountChanged));

        /// <summary>
        /// Gets or sets the maximum number of items to display.
        /// Default is 0 (show all). When greater than 0, limits visible items and shows overflow badge.
        /// </summary>
        public int VisibleCount
        {
            get => (int)GetValue(VisibleCountProperty);
            set => SetValue(VisibleCountProperty, value);
        }

        private static void OnVisibleCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AchievementCompactListControlBase control && control._isLoaded)
            {
                control.LoadData();
            }
        }

        #endregion

        #region Display Properties

        private List<AchievementDisplayItem> _displayItems = new List<AchievementDisplayItem>();
        /// <summary>
        /// Gets or sets the display items for the list.
        /// </summary>
        public List<AchievementDisplayItem> DisplayItems
        {
            get => _displayItems;
            protected set
            {
                _displayItems = value ?? new List<AchievementDisplayItem>();
            }
        }

        private int _overflowCount;
        /// <summary>
        /// Gets the count of items that exceed VisibleCount.
        /// </summary>
        public int OverflowCount
        {
            get => _overflowCount;
            protected set
            {
                if (_overflowCount != value)
                {
                    _overflowCount = value;
                    OnPropertyChanged(new DependencyPropertyChangedEventArgs(
                        OverflowCountProperty, value, value));
                }
            }
        }

        /// <summary>
        /// Identifies the OverflowCount dependency property for binding.
        /// </summary>
        public static readonly DependencyProperty OverflowCountProperty =
            DependencyProperty.Register(nameof(OverflowCount), typeof(int), typeof(AchievementCompactListControlBase),
                new PropertyMetadata(0));

        private bool _hasOverflow;
        /// <summary>
        /// Gets a value indicating whether there are more items than VisibleCount.
        /// </summary>
        public bool HasOverflow
        {
            get => _hasOverflow;
            protected set
            {
                if (_hasOverflow != value)
                {
                    _hasOverflow = value;
                }
            }
        }

        #endregion

        private bool _isLoaded;
        private bool _isSubscribedToCacheEvents;

        protected AchievementCompactListControlBase()
        {
            DataContext = this;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            SubscribeToCacheEvents();
            LoadData();

            // Attach mouse wheel handler for horizontal scrolling
            PreviewMouseWheel += OnPreviewMouseWheel;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
            UnsubscribeFromCacheEvents();
            PreviewMouseWheel -= OnPreviewMouseWheel;
        }

        private void SubscribeToCacheEvents()
        {
            if (_isSubscribedToCacheEvents || Plugin?.AchievementService == null)
                return;

            Plugin.AchievementService.GameCacheUpdated += OnGameCacheUpdated;
            Plugin.AchievementService.CacheDeltaUpdated += OnCacheDeltaUpdated;
            _isSubscribedToCacheEvents = true;
        }

        private void UnsubscribeFromCacheEvents()
        {
            if (!_isSubscribedToCacheEvents || Plugin?.AchievementService == null)
                return;

            Plugin.AchievementService.GameCacheUpdated -= OnGameCacheUpdated;
            Plugin.AchievementService.CacheDeltaUpdated -= OnCacheDeltaUpdated;
            _isSubscribedToCacheEvents = false;
        }

        private void OnGameCacheUpdated(object sender, Services.GameCacheUpdatedEventArgs e)
        {
            if (!_isLoaded) return;

            var game = Plugin?.Settings?.SelectedGame;
            if (game == null) return;

            // Check if the updated game matches our current game
            if (e.GameId != null && Guid.TryParse(e.GameId, out var gameId) && gameId == game.Id)
            {
                Dispatcher.BeginInvoke(new Action(LoadData));
            }
        }

        private void OnCacheDeltaUpdated(object sender, Models.CacheDeltaEventArgs e)
        {
            if (!_isLoaded) return;

            var game = Plugin?.Settings?.SelectedGame;
            if (game == null) return;

            // Check if any delta affects our current game (Key is the game ID)
            if (e.Key != null && Guid.TryParse(e.Key, out var gameId) && gameId == game.Id)
            {
                Dispatcher.BeginInvoke(new Action(LoadData));
            }
        }

        /// <summary>
        /// Filters achievements for display. Override to provide custom filtering.
        /// Default returns true (show all achievements).
        /// </summary>
        protected virtual bool FilterAchievement(AchievementDetail achievement) => true;

        /// <summary>
        /// Loads data from AchievementService and applies filtering.
        /// </summary>
        protected virtual void LoadData()
        {
            var game = Plugin?.Settings?.SelectedGame;
            if (game == null)
            {
                ClearItems();
                return;
            }

            var gameData = Plugin?.AchievementService?.GetGameAchievementData(game.Id);
            if (gameData == null || !gameData.HasAchievements)
            {
                ClearItems();
                return;
            }

            var options = AchievementProjectionService.CreateOptions(Plugin.Settings, gameData);
            var displayItems = new List<AchievementDisplayItem>();

            foreach (var ach in gameData.Achievements)
            {
                if (FilterAchievement(ach))
                {
                    var item = AchievementProjectionService.CreateDisplayItem(gameData, ach, options, game.Id);
                    if (item != null)
                        displayItems.Add(item);
                }
            }

            // Apply VisibleCount limit
            if (VisibleCount > 0 && displayItems.Count > VisibleCount)
            {
                DisplayItems = displayItems.Take(VisibleCount).ToList();
                OverflowCount = displayItems.Count - VisibleCount;
                HasOverflow = true;
            }
            else
            {
                DisplayItems = displayItems;
                OverflowCount = 0;
                HasOverflow = false;
            }

            // Refresh the ItemsControl binding
            RefreshItemsSource();
        }

        /// <summary>
        /// Clears all items and resets overflow state.
        /// </summary>
        protected void ClearItems()
        {
            DisplayItems = new List<AchievementDisplayItem>();
            OverflowCount = 0;
            HasOverflow = false;
            RefreshItemsSource();
        }

        /// <summary>
        /// Refreshes the ItemsControl ItemsSource binding. Override to provide control-specific implementation.
        /// </summary>
        protected virtual void RefreshItemsSource()
        {
            // Derived classes should override this to refresh their ItemsControl
        }

        /// <summary>
        /// Handles mouse wheel for horizontal scrolling.
        /// </summary>
        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta != 0)
            {
                var scrollViewer = FindScrollViewer(this);
                if (scrollViewer != null)
                {
                    e.Handled = true;
                    scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
                }
            }
        }

        private static ScrollViewer FindScrollViewer(DependencyObject parent)
        {
            if (parent == null) return null;

            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>
        /// Determines whether a change raised from ThemeData should trigger a refresh.
        /// </summary>
        protected override bool ShouldHandleThemeDataChange(string propertyName)
        {
            // Refresh when achievement data changes
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
            // Load data for the new game context directly
            if (_isLoaded)
            {
                LoadData();
            }
        }
    }
}

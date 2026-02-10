// --SUCCESSSTORY--
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PlayniteAchievements.Common;
using Playnite.SDK.Controls;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Views.ThemeIntegration.Base;
using PlayniteAchievements.Views.ThemeIntegration.Legacy.Controls;

namespace PlayniteAchievements.Views.ThemeIntegration.Base
{
    /// <summary>
    /// Generic base class for compact achievement controls that display achievements in a horizontal grid.
    /// Programmatically creates grid columns to fit achievements, with "+X more" in the last column.
    /// </summary>
    /// <typeparam name="T">The type of achievement collection (List&lt;AchievementDetail&gt; or similar).</typeparam>
    public abstract class CompactAchievementControlBase : ThemeControlBase, INotifyPropertyChanged
    {
        private static readonly List<AchievementDetail> EmptyAchievements = new List<AchievementDetail>();
        private List<AchievementDetail> _visibleAchievements = EmptyAchievements;

        public event PropertyChangedEventHandler PropertyChanged;

        #region IconHeight Property

        public static readonly DependencyProperty IconHeightProperty = DependencyProperty.Register(
            nameof(IconHeight),
            typeof(double),
            typeof(CompactAchievementControlBase),
            new FrameworkPropertyMetadata(64.0));

        public double IconHeight
        {
            get => (double)GetValue(IconHeightProperty);
            set
            {
                SetValue(IconHeightProperty, value);
                UpdateCompactLayout();
            }
        }

        #endregion

        /// <summary>
        /// Gets the source collection to display. Derived classes implement this to provide
        /// either the filtered locked achievements or the unlocked achievements.
        /// </summary>
        protected abstract List<AchievementDetail> GetSourceAchievements();

        /// <summary>
        /// Gets the total count of achievements for display calculation.
        /// </summary>
        protected abstract int GetTotalCount();

        /// <summary>
        /// Gets the property name to notify when achievement data changes.
        /// Used for HasX property (e.g., "HasLocked" or "HasUnlocked").
        /// </summary>
        protected abstract string GetHasAchievementPropertyName();

        /// <summary>
        /// Gets the property name to notify when total count changes.
        /// Used for TotalX property (e.g., "TotalLocked" or "TotalUnlocked").
        /// </summary>
        protected abstract string GetTotalCountPropertyName();

        /// <summary>
        /// Gets the property names to watch for settings changes that should trigger an update.
        /// </summary>
        protected abstract string[] GetSettingsPropertyNames();

        /// <summary>
        /// Gets the compact view grid that displays achievements.
        /// Derived classes must implement this to provide access to their XAML-defined grid.
        /// </summary>
        protected abstract Grid CompactViewGrid { get; }

        /// <summary>
        /// Creates an achievement image control for display in the grid.
        /// Derived classes can override to customize icon behavior.
        /// </summary>
        protected virtual AchievementImage CreateAchievementImage(AchievementDetail achievement)
        {
            return new AchievementImage
            {
                Width = IconHeight,
                Height = IconHeight,
                Margin = new Thickness(4),
                ToolTip = achievement.DisplayName,
                // Icon is for unlocked state, IconCustom is for locked state
                Icon = achievement.UnlockedIconDisplay,
                IconCustom = achievement.LockedIconDisplay,
                IsLocked = false,
                Percent = achievement.GlobalPercentUnlocked ?? 0,
                EnableRaretyIndicator = true,
                DisplayRaretyValue = false
            };
        }

        protected CompactAchievementControlBase()
        {
            IconHeight = 64.0;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += OnSizeChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (Plugin?.Settings?.LegacyTheme != null)
            {
                Plugin.Settings.LegacyTheme.PropertyChanged -= Settings_PropertyChanged;
                Plugin.Settings.LegacyTheme.PropertyChanged += Settings_PropertyChanged;
            }

            UpdateFilteredAchievements();

            // Also trigger a layout update when the control is rendered
            Dispatcher?.BeginInvoke(new Action(() =>
            {
                if (_visibleAchievements.Count > 0)
                {
                    UpdateCompactLayout();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (Plugin?.Settings?.LegacyTheme != null)
            {
                Plugin.Settings.LegacyTheme.PropertyChanged -= Settings_PropertyChanged;
            }
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var watchedProperties = GetSettingsPropertyNames();
            foreach (var prop in watchedProperties)
            {
                if (e.PropertyName == prop)
                {
                    Dispatcher?.InvokeIfNeeded(UpdateFilteredAchievements);
                    return;
                }
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged || e.HeightChanged)
            {
                // When size changes, the element has been rendered, so update immediately
                UpdateCompactLayout();
            }
        }

        private void UpdateFilteredAchievements()
        {
            _visibleAchievements = GetSourceAchievements() ?? EmptyAchievements;
            OnPropertyChanged(nameof(VisibleAchievements));
            OnPropertyChanged(GetHasAchievementPropertyName());
            OnPropertyChanged(GetTotalCountPropertyName());
            OnPropertyChanged(nameof(RemainingCount));

            // Defer layout update to ensure the element has been rendered
            Dispatcher?.BeginInvoke(new Action(() => UpdateCompactLayout()), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Gets the actual total count of achievements for remaining calculation.
        /// This can be overridden by derived classes if the visible achievements
        /// are just a preview (e.g., locked achievements).
        /// </summary>
        protected virtual int GetRemainingTotalCount()
        {
            return GetTotalCount();
        }

        private void UpdateCompactLayout()
        {
            if (CompactViewGrid == null || double.IsNaN(CompactViewGrid.ActualWidth))
            {
                return;
            }

            if (_visibleAchievements.Count == 0)
            {
                CompactViewGrid.Children.Clear();
                CompactViewGrid.ColumnDefinitions.Clear();
                return;
            }

            CompactViewGrid.Children.Clear();
            CompactViewGrid.ColumnDefinitions.Clear();

            double actualWidth = CompactViewGrid.ActualWidth;
            if (actualWidth <= 0)
            {
                actualWidth = IconHeight + 8;
            }
            double iconSize = IconHeight + 8; // icon + margin (4px each side)
            int nbGrid = Math.Max(1, (int)(actualWidth / iconSize));

            for (int i = 0; i < nbGrid; i++)
            {
                ColumnDefinition gridCol = new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                };
                CompactViewGrid.ColumnDefinitions.Add(gridCol);

                if (i < _visibleAchievements.Count)
                {
                    if (i < nbGrid - 1)
                    {
                        // Show achievement image
                        var achievement = _visibleAchievements[i];
                        var achievementImage = CreateAchievementImage(achievement);
                        achievementImage.SetValue(Grid.ColumnProperty, i);
                        CompactViewGrid.Children.Add(achievementImage);
                    }
                    else
                    {
                        // Show "+X more" label in the last column
                        int remaining = GetRemainingTotalCount() - i;
                        if (remaining > 0)
                        {
                            var more = CreateMoreBadge(remaining);
                            more.SetValue(Grid.ColumnProperty, i);
                            CompactViewGrid.Children.Add(more);
                        }
                        break;
                    }
                }
                else if (_visibleAchievements.Count > 0)
                {
                    // Show remaining count if we have achievements but filled all slots
                    if (i == nbGrid - 1)
                    {
                        int remaining = GetRemainingTotalCount() - i;
                        if (remaining > 0)
                        {
                            var more = CreateMoreBadge(remaining);
                            more.SetValue(Grid.ColumnProperty, i);
                            CompactViewGrid.Children.Add(more);
                        }
                    }
                }
            }
        }

        private Border CreateMoreBadge(int remaining)
        {
            // SuccessStory-like: dim tile overlay with centered "+N".
            var foreground = (Application.Current?.TryFindResource("TextBrush") as Brush) ?? Brushes.White;
            var background = new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0));

            var badge = new Border
            {
                Width = IconHeight,
                Height = IconHeight,
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(6),
                Background = background,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true
            };

            var grid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            grid.Children.Add(new TextBlock
            {
                Text = $"+{remaining}",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });

            badge.Child = grid;
            ToolTipService.SetToolTip(badge, $"{remaining} more");

            return badge;
        }

        public List<AchievementDetail> VisibleAchievements => _visibleAchievements;

        public int RemainingCount
        {
            get
            {
                double actualWidth = CompactViewGrid?.ActualWidth ?? 0;
                if (actualWidth <= 0 || IconHeight <= 0) return 0;
                int nbGrid = Math.Max(1, (int)(actualWidth / (IconHeight + 8)));
                int total = GetRemainingTotalCount();
                if (nbGrid >= total) return 0;
                return Math.Max(0, total - (nbGrid - 1));
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
// --END SUCCESSSTORY--

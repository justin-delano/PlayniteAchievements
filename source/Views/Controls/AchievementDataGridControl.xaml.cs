using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// Reusable DataGrid control for displaying achievements with sorting,
    /// column visibility, and width persistence.
    /// </summary>
    public partial class AchievementDataGridControl : UserControl, IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private ColumnWidthPersistenceService _columnPersistence;
        private bool _isAttached;

        private static readonly IReadOnlyDictionary<string, double> DefaultColumnWidthSeeds =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Achievement"] = 460,
                ["UnlockDate"] = 240,
                ["CategoryType"] = 210,
                ["CategoryLabel"] = 210,
                ["Rarity"] = 170,
                ["Points"] = 100
            };

        /// <summary>
        /// Identifies the ItemsSource dependency property.
        /// </summary>
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<AchievementDisplayItem>),
                typeof(AchievementDataGridControl), new PropertyMetadata(null));

        /// <summary>
        /// Gets or sets the achievement items to display.
        /// </summary>
        public IEnumerable<AchievementDisplayItem> ItemsSource
        {
            get => (IEnumerable<AchievementDisplayItem>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        /// <summary>
        /// Identifies the RevealCommand dependency property.
        /// </summary>
        public static readonly DependencyProperty RevealCommandProperty =
            DependencyProperty.Register(nameof(RevealCommand), typeof(System.Windows.Input.ICommand),
                typeof(AchievementDataGridControl), new PropertyMetadata(null));

        /// <summary>
        /// Gets or sets the command to execute when revealing a hidden achievement.
        /// The command parameter will be the AchievementDisplayItem.
        /// </summary>
        public System.Windows.Input.ICommand RevealCommand
        {
            get => (System.Windows.Input.ICommand)GetValue(RevealCommandProperty);
            set => SetValue(RevealCommandProperty, value);
        }

        /// <summary>
        /// Identifies the ColumnSettingsKey dependency property.
        /// Used to separate persisted column settings for different contexts.
        /// </summary>
        public static readonly DependencyProperty ColumnSettingsKeyProperty =
            DependencyProperty.Register(nameof(ColumnSettingsKey), typeof(string),
                typeof(AchievementDataGridControl), new PropertyMetadata("Default"));

        /// <summary>
        /// Gets or sets the key used to persist column settings separately per control instance.
        /// </summary>
        public string ColumnSettingsKey
        {
            get => (string)GetValue(ColumnSettingsKeyProperty);
            set => SetValue(ColumnSettingsKeyProperty, value);
        }

        public AchievementDataGridControl()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_isAttached)
            {
                return;
            }

            AttachColumnPersistence();
            _isAttached = true;
        }

        private void AttachColumnPersistence()
        {
            var settings = PlayniteAchievementsPlugin.Instance?.Settings;
            if (settings == null)
            {
                return;
            }

            _columnPersistence = new ColumnWidthPersistenceService(
                AchievementsDataGrid,
                Logger,
                () => GetMergedWidths(settings),
                map => SetWidthsByKey(settings, map),
                () => settings.Persisted.DataGridColumnVisibility,
                map => settings.Persisted.DataGridColumnVisibility = map,
                () => SavePluginSettings(settings),
                DefaultColumnWidthSeeds);

            _columnPersistence.Attach();
        }

        private Dictionary<string, double> GetMergedWidths(PlayniteAchievementsSettings settings)
        {
            var merged = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            // Use key-specific widths if available
            var keyMap = GetWidthsByKey(settings);
            if (keyMap != null)
            {
                foreach (var pair in keyMap)
                {
                    if (IsValidWidth(pair.Value))
                    {
                        merged[pair.Key] = pair.Value;
                    }
                }
            }

            // Fall back to single game widths for any missing keys
            var singleGameMap = settings?.Persisted?.SingleGameColumnWidths;
            if (singleGameMap != null)
            {
                foreach (var pair in singleGameMap)
                {
                    if (!merged.ContainsKey(pair.Key) && IsValidWidth(pair.Value))
                    {
                        merged[pair.Key] = pair.Value;
                    }
                }
            }

            return merged;
        }

        private Dictionary<string, double> GetWidthsByKey(PlayniteAchievementsSettings settings)
        {
            if (settings?.Persisted == null)
            {
                return null;
            }

            return ColumnSettingsKey switch
            {
                "DesktopTheme" => settings.Persisted.DesktopThemeColumnWidths,
                "SingleGame" => settings.Persisted.SingleGameColumnWidths,
                _ => settings.Persisted.SingleGameColumnWidths
            };
        }

        private void SetWidthsByKey(PlayniteAchievementsSettings settings, Dictionary<string, double> map)
        {
            if (settings?.Persisted == null)
            {
                return;
            }

            switch (ColumnSettingsKey)
            {
                case "DesktopTheme":
                    settings.Persisted.DesktopThemeColumnWidths = map;
                    break;
                case "SingleGame":
                    settings.Persisted.SingleGameColumnWidths = map;
                    break;
                default:
                    settings.Persisted.SingleGameColumnWidths = map;
                    break;
            }
        }

        private static bool IsValidWidth(double width)
        {
            return !double.IsNaN(width) && !double.IsInfinity(width) && width > 0;
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
                Logger.Warn(ex, "Failed to persist column layout settings.");
            }
        }

        private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            var sortDirection = DataGridSortingHelper.HandleSorting(sender, e);
            if (sortDirection == null)
            {
                return;
            }

            // Sort in-memory by reordering ItemsSource
            var items = ItemsSource?.ToList();
            if (items == null || items.Count == 0)
            {
                return;
            }

            var sortPath = e.Column.SortMemberPath;
            var sorted = sortDirection.Value == ListSortDirection.Ascending
                ? items.OrderBy(x => GetSortValue(x, sortPath))
                : items.OrderByDescending(x => GetSortValue(x, sortPath));

            ItemsSource = sorted.ToList();
        }

        private static object GetSortValue(AchievementDisplayItem item, string sortPath)
        {
            if (item == null || string.IsNullOrWhiteSpace(sortPath))
            {
                return null;
            }

            return sortPath switch
            {
                "DisplayName" => item.DisplayName ?? string.Empty,
                "UnlockTime" => item.UnlockTime,
                "GlobalPercent" => item.GlobalPercent,
                "CategoryType" => item.CategoryType ?? string.Empty,
                "CategoryLabel" => item.CategoryLabel ?? string.Empty,
                "TrophyType" => item.TrophyType ?? string.Empty,
                "Points" => item.Points,
                _ => null
            };
        }

        private void AchievementRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.DataContext is AchievementDisplayItem item)
            {
                if (item.CanReveal)
                {
                    var command = RevealCommand;
                    if (command != null && command.CanExecute(item))
                    {
                        command.Execute(item);
                    }
                    else
                    {
                        item.ToggleReveal();
                    }
                    e.Handled = true;
                }
            }
        }

        private void DataGridColumnMenu_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is DataGrid grid))
            {
                return;
            }

            var row = ItemsControl.ContainerFromElement(grid, e.OriginalSource as DependencyObject) as DataGridRow;
            if (row != null)
            {
                return;
            }

            e.Handled = true;

            var menu = _columnPersistence?.BuildColumnVisibilityMenu();
            if (menu == null || menu.Items.Count == 0)
            {
                return;
            }

            menu.Placement = PlacementMode.RelativePoint;
            menu.PlacementTarget = grid;
            menu.HorizontalOffset = e.GetPosition(grid).X;
            menu.VerticalOffset = e.GetPosition(grid).Y;
            menu.IsOpen = true;
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

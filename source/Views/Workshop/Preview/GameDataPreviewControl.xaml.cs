using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services.Achievements;
using PlayniteAchievements.Services.Workshop.Preview;
using PlayniteAchievements.ViewModels.Items;
using PlayniteAchievements.Views.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AchievementDataGridControl = PlayniteAchievements.Views.Controls.AchievementDataGridControl;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.Views.Workshop.Preview
{
    /// <summary>One package entry of a package-only game data preview, with what the row template shows precomputed.</summary>
    public sealed class GameDataPreviewRow
    {
        // IcoFont glyph from Playnite's shipped icofont.ttf: plus.
        private const string AddedGlyph = "";

        public GameDataPreviewRow(AchievementPreviewRow row)
        {
            Row = row ?? throw new ArgumentNullException(nameof(row));
            BadgeGlyph = row.Changes.HasFlag(AchievementPreviewChange.Added) ? AddedGlyph : null;
            IsCapstone = row.After?.IsCapstone == true;
            CategoryText = row.After?.Category?.Trim() ?? string.Empty;
        }

        public AchievementPreviewRow Row { get; }

        public AchievementPreviewState After => Row.After;

        public string BadgeGlyph { get; }

        public bool HasBadge => BadgeGlyph != null;

        public bool IsCapstone { get; }

        public string CategoryText { get; }
    }

    /// <summary>
    /// A previewed game data package (<see cref="GameCustomDataPreviewModel"/> as the DataContext):
    /// a summary of what it carries and the game it is compared against. Compared against a
    /// library game, the game's achievements show in the achievement grid as View Achievements
    /// shows them: one grid after the install and one as they are now, Before / After showing
    /// one of the two, changed rows only unless unchanged ones are asked for. Without a game, the package's own entries are listed.
    /// <see cref="MaxRows"/> caps either with an "and N more" line.
    /// </summary>
    public partial class GameDataPreviewControl : UserControl
    {
        /// <summary>The fixed height of one package entry row; the list never measures row content.</summary>
        public static readonly double RowHeight = 60;

        public static readonly GridLength BadgeColumnWidth = new GridLength(24);

        public static readonly GridLength CategoryColumnWidth = new GridLength(160);

        /// <summary>The grid's height cap: none, so it takes the space it is given.</summary>
        public static readonly double UnboundedHeight = double.PositiveInfinity;

        /// <summary>Decode size of a grid icon, as the grid's icon cell requests it.</summary>
        internal const int GridIconDecodePixel = 128;

        // The live grids keep a column layout of their own (right-click a header to show or hide
        // columns), so hiding a column here leaves View Achievements alone. The published image
        // uses a fixed set of columns, so neither the sharer's layout nor their unlock dates
        // reach it. Both keys start with "WorkshopPreview", which drops the Captures column.
        private const string LiveColumnSettingsKey = "WorkshopPreviewGrid";
        private const string NeutralColumnSettingsKey = "WorkshopPreview";

        private static readonly HashSet<string> NeutralColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Status", "Icon", "Achievement", "CategoryType", "CategoryLabel", "Trophy", "Rarity", "Points"
        };

        public static readonly DependencyProperty MaxRowsProperty = DependencyProperty.Register(
            nameof(MaxRows), typeof(int), typeof(GameDataPreviewControl),
            new PropertyMetadata(0, (d, e) => ((GameDataPreviewControl)d).RefreshRows()));

        public static readonly DependencyProperty NeutralRenderProperty = DependencyProperty.Register(
            nameof(NeutralRender), typeof(bool), typeof(GameDataPreviewControl),
            new PropertyMetadata(false, (d, e) => ((GameDataPreviewControl)d).Rebuild()));

        private GameCustomDataPreviewDiff _diff;
        private List<GameDataPreviewRow> _rows = new List<GameDataPreviewRow>();
        private HashSet<GameDataPreviewRow> _shown = new HashSet<GameDataPreviewRow>();
        private ICollectionView _view;
        private HashSet<string> _changedApiNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<AchievementDisplayItem> _afterItems;
        private List<AchievementDisplayItem> _beforeItems;
        private List<AchievementDisplayItem> _shownItems = new List<AchievementDisplayItem>();

        public GameDataPreviewControl()
        {
            InitializeComponent();
            var reveal = new RelayCommand(item => (item as AchievementDisplayItem)?.ToggleReveal());
            AfterGrid.RevealCommand = reveal;
            BeforeGrid.RevealCommand = reveal;
            DataContextChanged += (sender, args) => Rebuild();
        }

        /// <summary>The most rows shown, 0 for all; the rest are counted in an "and N more" line.</summary>
        public int MaxRows
        {
            get => (int)GetValue(MaxRowsProperty);
            set => SetValue(MaxRowsProperty, value);
        }

        /// <summary>
        /// True for the published preview image: no controls, the rows the package touches after
        /// the install, default settings, a fixed column set with a status cell that shows only
        /// the capstone badge, and the note that addresses the viewing user
        /// ("not in your library") hidden, since it describes the sharer's machine.
        /// </summary>
        public bool NeutralRender
        {
            get => (bool)GetValue(NeutralRenderProperty);
            set => SetValue(NeutralRenderProperty, value);
        }

        /// <summary>The icon sources the visible grid's rows load, for a render that decodes them ahead.</summary>
        internal IReadOnlyList<string> DisplayedIconUris => _shownItems
            .Select(item => item.DisplayIcon)
            .Where(uri => !string.IsNullOrWhiteSpace(uri))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        /// <summary>
        /// Puts decoded icons into the grid's laid-out image elements, keyed by the source each
        /// image loads. An offscreen tree never starts its own loads, so a render calls this after
        /// layout with the images it decoded from <see cref="DisplayedIconUris"/>.
        /// </summary>
        internal void ApplyPreloadedIcons(IReadOnlyDictionary<string, ImageSource> images)
        {
            if (images == null || images.Count == 0)
            {
                return;
            }

            ApplyPreloadedIcons(VisibleGrid, images);
        }

        /// <summary>The grid Before / After shows: always the after-install one in a neutral render.</summary>
        private AchievementDataGridControl VisibleGrid => ShowsBefore ? BeforeGrid : AfterGrid;

        private bool ShowsBefore => !NeutralRender && _diff?.BeforeData != null && BeforeButton.IsChecked == true;

        /// <summary>
        /// Gives the grid's star-sized columns fixed widths that share <paramref name="width"/>,
        /// the width the render gives this control. An offscreen DataGrid never loads, and its
        /// star columns stay at their minimum width or overflow without it, so a render calls this
        /// after the first layout pass and lays out again. The width is passed in because an
        /// element arranged narrower than it asked for reports the width it asked for.
        /// </summary>
        internal void FixStarColumnWidths(double width)
        {
            var dataGrid = VisibleGrid.AchievementsDataGrid;
            if (width <= 0)
            {
                return;
            }

            var visible = dataGrid.Columns.Where(column => column.Visibility == Visibility.Visible).ToList();
            var stars = visible.Where(column => column.Width.IsStar).ToList();
            if (stars.Count == 0)
            {
                return;
            }

            // Declared widths: before the columns settle, ActualWidth can still be the minimum.
            var fixedWidth = visible
                .Where(column => !column.Width.IsStar)
                .Sum(column => column.Width.IsAbsolute ? column.Width.Value : column.ActualWidth);
            var totalStars = stars.Sum(column => column.Width.Value);
            var remaining = Math.Max(0, width - fixedWidth - 2);
            foreach (var column in stars)
            {
                var share = Math.Floor(remaining * column.Width.Value / totalStars);
                column.Width = new DataGridLength(Math.Max(column.MinWidth, share), DataGridLengthUnitType.Pixel);
            }
        }

        private static void ApplyPreloadedIcons(DependencyObject parent, IReadOnlyDictionary<string, ImageSource> images)
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Image image &&
                    AsyncImage.GetUri(image) is string uri &&
                    images.TryGetValue(uri, out var source))
                {
                    image.Source = source;
                }

                ApplyPreloadedIcons(child, images);
            }
        }

        private void Rebuild()
        {
            _diff = (DataContext as GameCustomDataPreviewModel)?.Diff;
            _afterItems = null;
            _beforeItems = null;
            var diff = _diff;
            if (diff == null)
            {
                _rows = new List<GameDataPreviewRow>();
                RowList.ItemsSource = null;
                _view = null;
                _shownItems = new List<AchievementDisplayItem>();
                AfterGrid.ItemsSource = null;
                BeforeGrid.ItemsSource = null;
                SummaryText.Text = string.Empty;
                PackageOnlyText.Visibility = Visibility.Collapsed;
                GameText.Visibility = Visibility.Collapsed;
                KeptEditsText.Visibility = Visibility.Collapsed;
                ComparisonOptions.Visibility = Visibility.Collapsed;
                RowList.Visibility = Visibility.Collapsed;
                AfterGrid.Visibility = Visibility.Collapsed;
                BeforeGrid.Visibility = Visibility.Collapsed;
                MoreRowsText.Visibility = Visibility.Collapsed;
                return;
            }

            var culture = FormattingCulture.Current;
            SummaryText.Text = string.Format(
                culture,
                ResourceProvider.GetString("LOCPlayAch_Workshop_Preview_Summary"),
                diff.IconCount.ToString("N0", culture),
                diff.OverrideCount.ToString("N0", culture),
                diff.CategoryCount.ToString("N0", culture),
                diff.CapstoneCount.ToString("N0", culture),
                diff.CustomAchievementCount.ToString("N0", culture));

            PackageOnlyText.Visibility = diff.IsPackageOnly && !NeutralRender ? Visibility.Visible : Visibility.Collapsed;
            GameText.Visibility = diff.IsPackageOnly ? Visibility.Collapsed : Visibility.Visible;
            GameNameRun.Text = diff.ComparedAgainstGameName ?? string.Empty;

            KeptEditsText.Visibility = diff.KeptEditCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            KeptEditsText.Text = diff.KeptEditCount > 0
                ? string.Format(
                    culture,
                    ResourceProvider.GetString("LOCPlayAch_Workshop_Preview_KeptEdits"),
                    diff.KeptEditCount.ToString("N0", culture))
                : string.Empty;

            var compared = diff.AfterData != null;
            ComparisonOptions.Visibility = compared && !NeutralRender ? Visibility.Visible : Visibility.Collapsed;
            BeforeButton.Visibility = diff.BeforeData != null ? Visibility.Visible : Visibility.Collapsed;
            AfterButton.Visibility = BeforeButton.Visibility;
            RowList.Visibility = compared ? Visibility.Collapsed : Visibility.Visible;

            if (compared)
            {
                // The published image lists what the package touches in its after-install state;
                // shared from the game it was made on, the install itself changes nothing there.
                _changedApiNames = new HashSet<string>(
                    NeutralRender
                        ? diff.PackageTouchedApiNames
                        : diff.Rows.Select(row => row?.ApiName).Where(apiName => apiName != null),
                    StringComparer.OrdinalIgnoreCase);
                ConfigureGrid(AfterGrid);
                if (!NeutralRender)
                {
                    ConfigureGrid(BeforeGrid);
                }
                _rows = new List<GameDataPreviewRow>();
                _view = null;
                RowList.ItemsSource = null;
            }
            else
            {
                _rows = diff.Rows
                    .Where(row => row?.After != null)
                    .Select(row => new GameDataPreviewRow(row))
                    .ToList();
                _view = CollectionViewSource.GetDefaultView(_rows);
                _view.Filter = item => item is GameDataPreviewRow row && _shown.Contains(row);
                _shownItems = new List<AchievementDisplayItem>();
                AfterGrid.ItemsSource = null;
                BeforeGrid.ItemsSource = null;
                AfterGrid.Visibility = Visibility.Collapsed;
                BeforeGrid.Visibility = Visibility.Collapsed;
            }

            RefreshRows();
            if (!compared)
            {
                RowList.ItemsSource = _view;
            }
        }

        /// <summary>Settings and columns of a grid for the live dialog or the published image.</summary>
        private void ConfigureGrid(AchievementDataGridControl grid)
        {
            if (NeutralRender)
            {
                grid.ColumnSettingsKey = NeutralColumnSettingsKey;
                grid.AllowLayoutPersistence = false;
                grid.AllowColumnVisibilityMenu = false;
                grid.ShowRarityGlow = false;
                grid.ColorNamesByRarity = false;
                grid.ColorRarityColumnsByRarity = false;
                grid.FixedRowHeight = null;
                grid.ShowColumnHeaders = true;

                // The template's first-measure layout pass runs here, so the fixed column set
                // set below is what the render lays out.
                grid.ApplyTemplate();
                var dataGrid = grid.AchievementsDataGrid;
                foreach (var column in dataGrid.Columns)
                {
                    var key = ColumnVisibilityHelper.GetColumnKey(column) ?? string.Empty;
                    column.Visibility = NeutralColumns.Contains(key)
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                    // The status cell shows the capstone badge only: no check mark or padlock,
                    // so the image says nothing about the sharer's progress.
                    if (string.Equals(key, "Status", StringComparison.OrdinalIgnoreCase) &&
                        column is DataGridTemplateColumn statusColumn)
                    {
                        statusColumn.CellTemplate = (DataTemplate)FindResource("NeutralStatusTemplate");
                    }
                }

                // Every row is realized for the render, and no scroll bar is drawn beside them.
                dataGrid.EnableRowVirtualization = false;
                VirtualizingPanel.SetIsVirtualizing(dataGrid, false);
                ScrollViewer.SetVerticalScrollBarVisibility(dataGrid, ScrollBarVisibility.Hidden);
                return;
            }

            var persisted = PlayniteAchievementsPlugin.Instance?.Settings?.Persisted;
            grid.ColumnSettingsKey = LiveColumnSettingsKey;
            grid.ShowRarityGlow = persisted?.ViewAchievementsAchievementGridShowRarityGlow ?? true;
            grid.ColorNamesByRarity = persisted?.ViewAchievementsAchievementGridColorNamesByRarity ?? false;
            grid.ColorRarityColumnsByRarity = persisted?.ViewAchievementsAchievementGridColorRarityColumnsByRarity ?? false;
            grid.FixedRowHeight = persisted?.SingleGameGridRowHeight;
            grid.ShowColumnHeaders = persisted?.ShowViewAchievementsAchievementGridColumnHeaders ?? true;
        }

        private void RefreshRows()
        {
            var diff = _diff;
            if (diff == null)
            {
                MoreRowsText.Visibility = Visibility.Collapsed;
                return;
            }

            var hidden = diff.AfterData != null ? RefreshGrid(diff) : RefreshList();
            MoreRowsText.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
            MoreRowsText.Text = hidden > 0
                ? string.Format(
                    FormattingCulture.Current,
                    ResourceProvider.GetString("LOCPlayAch_Workshop_Preview_MoreRows"),
                    hidden.ToString("N0", FormattingCulture.Current))
                : string.Empty;
        }

        /// <summary>Shows the package entries up to <see cref="MaxRows"/>; returns how many are left out.</summary>
        private int RefreshList()
        {
            if (_view == null)
            {
                return 0;
            }

            var limit = MaxRows > 0 ? Math.Min(MaxRows, _rows.Count) : _rows.Count;
            _shown = new HashSet<GameDataPreviewRow>(_rows.Take(limit));
            _view.Refresh();
            return _rows.Count - limit;
        }

        /// <summary>
        /// Shows the grid Before / After picks, the other one hidden, with the changed rows, or
        /// every row with the changed ones highlighted, up to <see cref="MaxRows"/>. Returns how
        /// many are left out.
        /// </summary>
        private int RefreshGrid(GameCustomDataPreviewDiff diff)
        {
            var showBefore = ShowsBefore;
            var showUnchanged = !NeutralRender && ShowUnchangedCheckBox.IsChecked == true;
            var grid = showBefore ? BeforeGrid : AfterGrid;
            AfterGrid.Visibility = showBefore ? Visibility.Collapsed : Visibility.Visible;
            BeforeGrid.Visibility = showBefore ? Visibility.Visible : Visibility.Collapsed;
            var items = showBefore
                ? _beforeItems ?? (_beforeItems = BuildItems(diff.BeforeData))
                : _afterItems ?? (_afterItems = BuildItems(diff.AfterData));

            var wanted = showUnchanged
                ? items
                : items.Where(item => _changedApiNames.Contains(item.ApiName ?? string.Empty)).ToList();
            if (NeutralRender && wanted.Count == 0)
            {
                // A package that touches no single achievement (an order or category order only)
                // shows the game's first rows in their after-install order.
                wanted = items;
            }

            var limit = MaxRows > 0 ? Math.Min(MaxRows, wanted.Count) : wanted.Count;
            _shownItems = wanted.Take(limit).ToList();

            // Every row is a changed one unless unchanged rows are shown too.
            grid.HighlightedItems = showUnchanged
                ? new HashSet<AchievementDisplayItem>(_shownItems.Where(item => _changedApiNames.Contains(item.ApiName ?? string.Empty)))
                : null;
            grid.ItemsSource = _shownItems;
            return wanted.Count - limit;
        }

        /// <summary>
        /// The display rows of <paramref name="data"/> as View Achievements builds them: custom
        /// order applied, then the configured default sort with goals first.
        /// </summary>
        private List<AchievementDisplayItem> BuildItems(GameAchievementData data)
        {
            if (data?.Achievements == null)
            {
                return new List<AchievementDisplayItem>();
            }

            var settings = NeutralRender ? null : PlayniteAchievementsPlugin.Instance?.Settings;
            IEnumerable<AchievementDetail> source = data.Achievements.Where(achievement => achievement != null);
            if (data.AchievementOrder != null && data.AchievementOrder.Count > 0)
            {
                source = AchievementOrderHelper.ApplyOrder(source, achievement => achievement.ApiName, data.AchievementOrder);
            }

            var items = source
                .Select(achievement => AchievementDisplayItem.Create(data, achievement, settings, playniteGameIdOverride: data.PlayniteGameId))
                .Where(item => item != null)
                .ToList();
            AchievementSortHelper.OrderGameAchievementItems(
                items,
                columnSortPath: null,
                columnSortDirection: null,
                useSourceOrder: false,
                settings?.Persisted,
                AchievementSortSurface.SingleGame);
            return items;
        }

        private void Options_Changed(object sender, RoutedEventArgs e)
        {
            if (IsInitialized)
            {
                RefreshRows();
            }
        }
    }
}

using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Services.Workshop.Preview;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PlayniteAchievements.Views.Workshop.Preview
{
    /// <summary>One achievement row of a game data preview, with what the row template shows precomputed.</summary>
    public sealed class GameDataPreviewRow
    {
        // IcoFont glyphs from Playnite's shipped icofont.ttf: plus and minus.
        private const string AddedGlyph = "";
        private const string RemovedGlyph = "";

        public GameDataPreviewRow(AchievementPreviewRow row)
        {
            Row = row ?? throw new ArgumentNullException(nameof(row));
            var changes = row.Changes;
            BadgeGlyph = changes.HasFlag(AchievementPreviewChange.Added)
                ? AddedGlyph
                : changes.HasFlag(AchievementPreviewChange.Removed) ? RemovedGlyph : null;
            IsChanged = changes != AchievementPreviewChange.None;
            IsCapstone = (row.After ?? row.Before)?.IsCapstone == true;
            CategoryText = BuildCategoryText(row);
        }

        public AchievementPreviewRow Row { get; }

        public AchievementPreviewState Before => Row.Before;

        public AchievementPreviewState After => Row.After;

        public bool HasBefore => Row.Before != null;

        public bool HasAfter => Row.After != null;

        public string BadgeGlyph { get; }

        public bool HasBadge => BadgeGlyph != null;

        public bool IsChanged { get; }

        public bool IsCapstone { get; }

        /// <summary>The category, or "before → after" when the install changes it.</summary>
        public string CategoryText { get; }

        private static string BuildCategoryText(AchievementPreviewRow row)
        {
            var before = row.Before?.Category?.Trim();
            var after = row.After?.Category?.Trim();
            var categoryChanged = row.Before != null && row.After != null &&
                                  (row.Changes & (AchievementPreviewChange.Category | AchievementPreviewChange.CategoryType)) != 0;
            if (categoryChanged && !string.Equals(before, after, StringComparison.Ordinal))
            {
                return (before ?? string.Empty) + " → " + (after ?? string.Empty);
            }

            return after ?? before ?? string.Empty;
        }
    }

    /// <summary>
    /// A previewed game data package (<see cref="GameCustomDataPreviewModel"/> as the DataContext):
    /// a summary of what it carries, the game it is compared against (or that it is shown alone),
    /// and a list of the achievements the install changes, before and after. Unchanged
    /// achievements can be shown too; <see cref="MaxRows"/> caps the list with an "and N more" line.
    /// </summary>
    public partial class GameDataPreviewControl : UserControl
    {
        /// <summary>The fixed height of one row; the list never measures row content.</summary>
        public static readonly double RowHeight = 60;

        public static readonly GridLength BadgeColumnWidth = new GridLength(24);

        public static readonly GridLength ArrowColumnWidth = new GridLength(28);

        public static readonly GridLength CategoryColumnWidth = new GridLength(160);

        public static readonly DependencyProperty MaxRowsProperty = DependencyProperty.Register(
            nameof(MaxRows), typeof(int), typeof(GameDataPreviewControl),
            new PropertyMetadata(0, (d, e) => ((GameDataPreviewControl)d).RefreshRows()));

        private List<GameDataPreviewRow> _rows = new List<GameDataPreviewRow>();
        private HashSet<GameDataPreviewRow> _shown = new HashSet<GameDataPreviewRow>();
        private ICollectionView _view;

        public GameDataPreviewControl()
        {
            InitializeComponent();
            DataContextChanged += (sender, args) => Rebuild();
        }

        public static readonly DependencyProperty NeutralRenderProperty = DependencyProperty.Register(
            nameof(NeutralRender), typeof(bool), typeof(GameDataPreviewControl),
            new PropertyMetadata(false, (d, e) => ((GameDataPreviewControl)d).Rebuild()));

        /// <summary>The most rows the list shows, 0 for all; the rest are counted in an "and N more" line.</summary>
        public int MaxRows
        {
            get => (int)GetValue(MaxRowsProperty);
            set => SetValue(MaxRowsProperty, value);
        }

        /// <summary>
        /// True for the published preview image: the note that addresses the viewing user
        /// ("not in your library") is hidden, since it describes the sharer's machine.
        /// </summary>
        public bool NeutralRender
        {
            get => (bool)GetValue(NeutralRenderProperty);
            set => SetValue(NeutralRenderProperty, value);
        }

        private void Rebuild()
        {
            var diff = (DataContext as GameCustomDataPreviewModel)?.Diff;
            if (diff == null)
            {
                _rows = new List<GameDataPreviewRow>();
                RowList.ItemsSource = null;
                _view = null;
                SummaryText.Text = string.Empty;
                PackageOnlyText.Visibility = Visibility.Collapsed;
                GameText.Visibility = Visibility.Collapsed;
                KeptEditsText.Visibility = Visibility.Collapsed;
                ShowUnchangedCheckBox.Visibility = Visibility.Collapsed;
                ComparisonHeader.Visibility = Visibility.Collapsed;
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

            ShowUnchangedCheckBox.Visibility = !diff.IsPackageOnly && diff.UnchangedRows.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            ComparisonHeader.Visibility = diff.IsPackageOnly ? Visibility.Collapsed : Visibility.Visible;
            RowList.ItemTemplate = (DataTemplate)FindResource(diff.IsPackageOnly ? "PackageRowTemplate" : "ComparisonRowTemplate");

            // Changed rows in the order the game shows them after the install (removed last),
            // then the unchanged ones, which the filter hides until asked for.
            _rows = diff.Rows
                .Concat(diff.UnchangedRows)
                .Where(row => row != null)
                .Select(row => new GameDataPreviewRow(row))
                .ToList();
            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = item => item is GameDataPreviewRow row && _shown.Contains(row);
            RefreshRows();
            RowList.ItemsSource = _view;
        }

        private void RefreshRows()
        {
            if (_view == null)
            {
                MoreRowsText.Visibility = Visibility.Collapsed;
                return;
            }

            var showUnchanged = ShowUnchangedCheckBox.IsChecked == true;
            var wanted = _rows.Where(row => showUnchanged || row.IsChanged).ToList();
            var limit = MaxRows > 0 ? Math.Min(MaxRows, wanted.Count) : wanted.Count;
            _shown = new HashSet<GameDataPreviewRow>(wanted.Take(limit));
            _view.Refresh();

            var hidden = wanted.Count - limit;
            MoreRowsText.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
            MoreRowsText.Text = hidden > 0
                ? string.Format(
                    FormattingCulture.Current,
                    ResourceProvider.GetString("LOCPlayAch_Workshop_Preview_MoreRows"),
                    hidden.ToString("N0", FormattingCulture.Current))
                : string.Empty;
        }

        private void ShowUnchanged_Changed(object sender, RoutedEventArgs e)
        {
            RefreshRows();
        }
    }
}

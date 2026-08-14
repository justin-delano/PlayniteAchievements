using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Search;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Base for per-kind Showcase widget view models. The host control feeds a projection and the
    /// current viewport; subclasses expose bindable state rebuilt in <see cref="Refresh"/> whenever
    /// either changes. Keeping the data as a plain projection (not WPF types) lets the projection
    /// layer stay UI-free and off-thread-capable.
    /// </summary>
    public abstract class ShowcaseWidgetViewModelBase : ObservableObject
    {
        private ShowcaseWidgetProjection _projection;
        private WidgetViewportState _viewport = WidgetViewportState.Classify(0, 0);

        protected ShowcaseWidgetProjection Projection => _projection;

        protected WidgetViewportDensity Density => _viewport.Density;

        protected WidgetViewportOrientation Orientation => _viewport.Orientation;

        /// <summary>True outside compact, where widgets have room for labels and chrome.</summary>
        protected bool ShowChrome => Density != WidgetViewportDensity.Compact;

        public void Update(ShowcaseWidgetProjection projection, WidgetViewportState viewport)
        {
            _projection = projection;
            _viewport = viewport ?? WidgetViewportState.Classify(0, 0);
            Refresh();
        }

        protected abstract void Refresh();
    }

    /// <summary>
    /// Base for the widgets that render one of the shared data grids. They differ only in the
    /// row type, which projection slice feeds them, and their persisted column surface; the
    /// per-instance surface key and the grid's display-options record are handled here.
    /// </summary>
    public abstract class ShowcaseGridWidgetViewModelBase<TItem> : ShowcaseWidgetViewModelBase
    {
        private string _columnSettingsKey;
        private object _gridOptions;
        private GridControlBarViewModel _controlBar;

        protected ShowcaseGridWidgetViewModelBase()
        {
            _columnSettingsKey = BaseSurfaceKey;
        }

        public BulkObservableCollection<TItem> Items { get; } = new BulkObservableCollection<TItem>();

        /// <summary>Search/filter bar shown when the surface's ShowControlBar option is on.</summary>
        public GridControlBarViewModel ControlBar
        {
            get => _controlBar;
            protected set => SetValue(ref _controlBar, value);
        }

        /// <summary>
        /// Persisted column-layout surface. Widgets that allow multiple instances get one
        /// surface per instance so each placed grid keeps its own columns.
        /// </summary>
        public string ColumnSettingsKey
        {
            get => _columnSettingsKey;
            private set => SetValue(ref _columnSettingsKey, value);
        }

        /// <summary>
        /// The live display-options record for this widget's grid surface
        /// (AchievementGridOptions or GameSummaryGridOptions). Templates bind grid display
        /// DPs through it; the record raises its own PropertyChanged, so edits from the
        /// widget editor propagate without re-projection.
        /// </summary>
        public object GridOptions
        {
            get => _gridOptions;
            private set => SetValue(ref _gridOptions, value);
        }

        /// <summary>The widget kind's surface key, shared by every instance of that kind.</summary>
        protected abstract string BaseSurfaceKey { get; }

        /// <summary>False for single-instance widgets, whose surface never needs an instance suffix.</summary>
        protected virtual bool UsesPerInstanceSurface => true;

        protected abstract System.Collections.Generic.IEnumerable<TItem> SelectItems(
            ShowcaseWidgetProjection projection);

        protected override void Refresh()
        {
            // Must match ShowcaseGridSurfaces.ResolveWidgetSurface for this widget's kind:
            // the projection resolves GridWidgetOptions from that key, and the grid persists
            // its column layout under this one.
            ColumnSettingsKey = UsesPerInstanceSurface
                ? ShowcaseGridSurfaces.ForInstance(BaseSurfaceKey, Projection?.Instance?.InstanceId)
                : BaseSurfaceKey;
            GridOptions = Projection?.GridWidgetOptions;
            RefreshItems();
        }

        /// <summary>Filter hook for the control bar; runs before the MaxRows cap.</summary>
        protected virtual IEnumerable<TItem> FilterItems(IEnumerable<TItem> items) => items;

        /// <summary>
        /// Re-applies the control-bar filter and the surface's MaxRows cap over the projected
        /// rows. The filter runs before the cap so searching reaches rows beyond the cap.
        /// </summary>
        protected void RefreshItems()
        {
            var items = SelectItems(Projection) ?? Array.Empty<TItem>();
            var visible = DisplayGridRowLimitHelper.Limit(
                FilterItems(items),
                (GridOptions as GridCommonOptions)?.MaxRows);

            // Replacing the collection resets the grid, which rebuilds every row (and re-resolves
            // its art). The projection hands back the same row objects when nothing changed, so
            // an unrelated refresh - another widget's option, a resize, a pin toggle - leaves the
            // grid alone.
            if (!SameRows(visible))
            {
                Items.ReplaceAll(visible);
            }
        }

        private bool SameRows(System.Collections.Generic.IEnumerable<TItem> items)
        {
            var index = 0;
            foreach (var item in items)
            {
                if (index >= Items.Count || !ReferenceEquals(Items[index], item))
                {
                    return false;
                }

                index++;
            }

            return index == Items.Count;
        }
    }

    /// <summary>
    /// Grid widget base for achievement rows: contributes a search box that filters by game
    /// and achievement name before the MaxRows cap.
    /// </summary>
    public abstract class ShowcaseAchievementGridWidgetViewModelBase
        : ShowcaseGridWidgetViewModelBase<AchievementDisplayItem>
    {
        private readonly SearchTextIndex<AchievementDisplayItem> _searchIndex =
            new SearchTextIndex<AchievementDisplayItem>(item =>
                SearchTextBuilder.ForRecentAchievement(item?.GameName, item?.DisplayName));
        private string _searchText = string.Empty;

        protected ShowcaseAchievementGridWidgetViewModelBase()
        {
            ControlBar = new GridControlBarViewModel
            {
                Search = new GridSearchControl(
                    this,
                    nameof(SearchText),
                    () => SearchText,
                    value => SearchText = value,
                    ResourceProvider.GetString("LOCPlayAch_Filter_Achievements"),
                    () => SearchText = string.Empty)
            };
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(_searchText, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _searchText = normalized;
                OnPropertyChanged(nameof(SearchText));
                RefreshItems();
            }
        }

        protected override IEnumerable<AchievementDisplayItem> FilterItems(
            IEnumerable<AchievementDisplayItem> items)
        {
            var query = SearchQuery.From(SearchText);
            if (!query.HasValue)
            {
                return items;
            }

            var list = (items ?? Enumerable.Empty<AchievementDisplayItem>())
                .Where(item => item != null)
                .ToList();
            _searchIndex.Rebuild(list);
            return list.Where(item => _searchIndex.Matches(item, query));
        }
    }

    /// <summary>
    /// Grid widget base for game rows: contributes the shared game-summaries control bar
    /// (search plus provider, progress and activity filters) applied before the MaxRows cap.
    /// </summary>
    public abstract class ShowcaseGameGridWidgetViewModelBase
        : ShowcaseGridWidgetViewModelBase<GameSummaryItem>
    {
        private readonly GameSummaryGridControlBarAdapter _controlBarAdapter =
            new GameSummaryGridControlBarAdapter();

        protected ShowcaseGameGridWidgetViewModelBase()
        {
            _controlBarAdapter.FilterChanged += (_, __) => RefreshItems();
            ControlBar = _controlBarAdapter.ControlBar;
        }

        protected override IEnumerable<GameSummaryItem> FilterItems(IEnumerable<GameSummaryItem> items)
        {
            var list = (items ?? Enumerable.Empty<GameSummaryItem>())
                .Where(item => item != null)
                .ToList();
            _controlBarAdapter.UpdateOptions(list);
            return _controlBarAdapter.Apply(list);
        }
    }
}

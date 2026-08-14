using System;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services.Showcase;

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

        protected ShowcaseGridWidgetViewModelBase()
        {
            _columnSettingsKey = BaseSurfaceKey;
        }

        public BulkObservableCollection<TItem> Items { get; } = new BulkObservableCollection<TItem>();

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

            var items = SelectItems(Projection) ?? Array.Empty<TItem>();

            // Replacing the collection resets the grid, which rebuilds every row (and re-resolves
            // its art). The projection hands back the same row objects when nothing changed, so
            // an unrelated refresh - another widget's option, a resize, a pin toggle - leaves the
            // grid alone.
            if (!SameRows(items))
            {
                Items.ReplaceAll(items);
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
}

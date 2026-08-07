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

        public void Update(ShowcaseWidgetProjection projection, WidgetViewportState viewport)
        {
            _projection = projection;
            _viewport = viewport ?? WidgetViewportState.Classify(0, 0);
            Refresh();
        }

        protected abstract void Refresh();
    }
}

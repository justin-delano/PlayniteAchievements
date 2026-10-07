using System;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Views.Showcase
{
    /// <summary>
    /// What a <see cref="ShowcaseControl"/> edits and renders: the layout it owns, how that
    /// layout is normalized and saved, which snapshot each widget projects from, and the rules
    /// of its surface. The Showcase page and the overview's mini-showcase are the two hosts.
    /// </summary>
    internal abstract class ShowcaseLayoutHost : IDisposable
    {
        public abstract ShowcaseSettings Layout { get; }

        /// <summary>Brings the layout back into its host's shape after an edit.</summary>
        public abstract void Normalize();

        public abstract void Persist();

        /// <summary>
        /// One page of one row whose height the host owns. A strip has no page controls or
        /// Workshop actions, no row operations, and leaves the Showcase page's shared state
        /// (grid surfaces, control bar state, stored images, configuration broadcasts) alone.
        /// </summary>
        public virtual bool IsStrip => false;

        public virtual bool IsKindAllowed(ShowcaseWidgetKind kind) => true;

        /// <summary>The snapshot <paramref name="widget"/> projects from; null while none is ready.</summary>
        public abstract OverviewDataSnapshot SnapshotFor(ShowcaseWidgetInstanceSettings widget);

        /// <summary>Adjusts a freshly built projection before it reaches its widget.</summary>
        public virtual void Decorate(ShowcaseWidgetProjection projection)
        {
        }

        /// <summary>The strip's height in pixels; only a strip reads or writes it.</summary>
        public virtual double StripHeight
        {
            get => double.NaN;
            set { }
        }

        /// <summary>Raised when <see cref="SnapshotFor"/> may answer with different data.</summary>
        public event EventHandler DataChanged;

        protected void RaiseDataChanged() => DataChanged?.Invoke(this, EventArgs.Empty);

        public virtual void Dispose()
        {
        }
    }

    /// <summary>The Showcase page: every page of <see cref="PersistedSettings.Showcase"/> over the overview's snapshot.</summary>
    internal sealed class GlobalShowcaseLayoutHost : ShowcaseLayoutHost
    {
        private readonly OverviewViewModel _overview;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly Action _persist;

        public GlobalShowcaseLayoutHost(
            OverviewViewModel overview,
            PlayniteAchievementsSettings settings,
            Action persist)
        {
            _overview = overview ?? throw new ArgumentNullException(nameof(overview));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _persist = persist ?? throw new ArgumentNullException(nameof(persist));
            _overview.SnapshotChanged += Overview_SnapshotChanged;
        }

        public override ShowcaseSettings Layout => _settings.Persisted.Showcase;

        public override void Normalize() => ShowcaseLayoutService.Normalize(Layout);

        public override void Persist() => _persist();

        public override OverviewDataSnapshot SnapshotFor(ShowcaseWidgetInstanceSettings widget) =>
            _overview.LatestSnapshot;

        private void Overview_SnapshotChanged(object sender, EventArgs e) => RaiseDataChanged();

        public override void Dispose()
        {
            _overview.SnapshotChanged -= Overview_SnapshotChanged;
        }
    }
}

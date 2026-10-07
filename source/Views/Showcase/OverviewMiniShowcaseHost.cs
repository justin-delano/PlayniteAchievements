using System;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Views.Showcase
{
    /// <summary>
    /// The overview's mini-showcase: one row of widgets linked to the overview. Each widget
    /// projects from the overview's snapshot narrowed by every grid filter except the ones it
    /// sets itself, and by the selected game where the overview's own charts followed it.
    /// </summary>
    internal sealed class OverviewMiniShowcaseHost : ShowcaseLayoutHost
    {
        private readonly OverviewViewModel _overview;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly Action _persist;
        private int _revision;

        public OverviewMiniShowcaseHost(
            OverviewViewModel overview,
            PlayniteAchievementsSettings settings,
            Action persist)
        {
            _overview = overview ?? throw new ArgumentNullException(nameof(overview));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _persist = persist ?? throw new ArgumentNullException(nameof(persist));
            _overview.LinkedDataChanged += Overview_LinkedDataChanged;
        }

        public override ShowcaseSettings Layout => _settings.Persisted.OverviewMiniShowcase;

        public override bool IsStrip => true;

        public override void Normalize() => OverviewMiniShowcaseLayout.Normalize(Layout);

        public override void Persist() => _persist();

        public override bool IsKindAllowed(ShowcaseWidgetKind kind) => OverviewMiniShowcaseLayout.IsAllowedKind(kind);

        public override double StripHeight
        {
            get => _settings.Persisted.OverviewMiniShowcaseHeight;
            set => _settings.Persisted.OverviewMiniShowcaseHeight = value;
        }

        public override OverviewDataSnapshot SnapshotFor(ShowcaseWidgetInstanceSettings widget)
        {
            var (exclude, selection) = LinkRule(widget);
            return _overview.GetLinkedSnapshot(exclude, selection);
        }

        public override void Decorate(ShowcaseWidgetProjection projection)
        {
            if (projection?.Instance == null)
            {
                return;
            }

            var widget = projection.Instance;
            projection.IsLinked = true;
            projection.LinkedRevision = _revision;
            projection.ContextLabel = _overview.GetLinkedNarrowedGame(LinkRule(widget).Selection)?.GameName;
            switch (widget.Kind)
            {
                case ShowcaseWidgetKind.Pie:
                    projection.LinkedSliceKeys = _overview.GetLinkedSliceKeys(ShowcaseWidgetOptions.GetPieMode(widget));
                    break;
                case ShowcaseWidgetKind.Timeline:
                case ShowcaseWidgetKind.ActivityCalendar:
                    projection.HighlightedSpan = _overview.UnlockSpanFilter;
                    break;
            }
        }

        public override bool IsProjectionCurrent(ShowcaseWidgetProjection projection, OverviewDataSnapshot snapshot) =>
            base.IsProjectionCurrent(projection, snapshot) && projection.LinkedRevision == _revision;

        /// <summary>
        /// Which filters a widget leaves out and when it follows the selected game: each pie
        /// follows every filter except its own, the rarity and trophy pies switch to the selected
        /// game when it has that data, and the timeline and calendar mark the unlock-day filter
        /// they set rather than narrowing to it.
        /// </summary>
        private static (OverviewLinkedFilter Exclude, OverviewLinkedSelection Selection) LinkRule(
            ShowcaseWidgetInstanceSettings widget)
        {
            switch (widget?.Kind)
            {
                case ShowcaseWidgetKind.Pie:
                    switch (ShowcaseWidgetOptions.GetPieMode(widget))
                    {
                        case ShowcasePieMode.Provider:
                            return (OverviewLinkedFilter.Provider, OverviewLinkedSelection.Ignore);
                        case ShowcasePieMode.Rarity:
                            return (OverviewLinkedFilter.None, OverviewLinkedSelection.WhenRarityData);
                        case ShowcasePieMode.Trophy:
                            return (OverviewLinkedFilter.None, OverviewLinkedSelection.WhenTrophyData);
                        default:
                            return (OverviewLinkedFilter.Completeness, OverviewLinkedSelection.Ignore);
                    }

                case ShowcaseWidgetKind.Timeline:
                case ShowcaseWidgetKind.ActivityCalendar:
                    return (OverviewLinkedFilter.UnlockSpan, OverviewLinkedSelection.Always);
                default:
                    return (OverviewLinkedFilter.None, OverviewLinkedSelection.Always);
            }
        }

        private void Overview_LinkedDataChanged(object sender, EventArgs e)
        {
            _revision = unchecked(_revision + 1);
            RaiseDataChanged();
        }

        public override void Dispose()
        {
            _overview.LinkedDataChanged -= Overview_LinkedDataChanged;
        }
    }
}

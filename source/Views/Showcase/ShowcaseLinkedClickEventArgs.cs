using System.Windows;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Overview;

namespace PlayniteAchievements.Views.Showcase
{
    /// <summary>
    /// A click on a linked widget, translated from the chart's own vocabulary (a slice label, a
    /// column index, a day) into what the overview filters by. Exactly one of
    /// <see cref="SliceKey"/> and <see cref="Span"/> is set.
    /// </summary>
    public sealed class ShowcaseLinkedClickEventArgs : RoutedEventArgs
    {
        public ShowcaseLinkedClickEventArgs(RoutedEvent routedEvent, object source)
            : base(routedEvent, source)
        {
        }

        public ShowcaseWidgetInstanceSettings Widget { get; set; }

        /// <summary>The clicked pie's mode, with <see cref="SliceKey"/>.</summary>
        public ShowcasePieMode? PieMode { get; set; }

        /// <summary>A provider key or an <see cref="OverviewLinkedSliceKeys"/> value.</summary>
        public string SliceKey { get; set; }

        /// <summary>The days a clicked timeline column or calendar day covers.</summary>
        public UnlockDaySpan? Span { get; set; }
    }

    public delegate void ShowcaseLinkedClickEventHandler(object sender, ShowcaseLinkedClickEventArgs e);
}

using System;
using System.Windows;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// A click on one mark of a chart, bubbled so a host that does not own the chart (a widget
    /// inside a template) can act on it. Each chart fills the field that names its mark.
    /// </summary>
    public sealed class ChartClickEventArgs : RoutedEventArgs
    {
        public ChartClickEventArgs(RoutedEvent routedEvent, object source)
            : base(routedEvent, source)
        {
        }

        /// <summary>The clicked pie slice's label.</summary>
        public string Label { get; set; }

        /// <summary>The clicked column's position along the axis.</summary>
        public int Index { get; set; } = -1;

        /// <summary>The clicked calendar day.</summary>
        public DateTime Day { get; set; }
    }

    public delegate void ChartClickEventHandler(object sender, ChartClickEventArgs e);
}

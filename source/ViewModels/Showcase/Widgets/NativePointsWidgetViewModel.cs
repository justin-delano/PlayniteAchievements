using System;
using System.Linq;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services.Showcase;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>One provider/game points row: label, value, optional provider caption, and the
    /// bar length as a 0..1 fraction of the largest row.</summary>
    public sealed class ChartRowViewModel
    {
        public ChartRowViewModel(
            string label,
            string valueText,
            string secondaryText,
            bool showSecondary,
            double fraction)
        {
            Label = label;
            ValueText = valueText;
            SecondaryText = secondaryText;
            HasSecondary = showSecondary && !string.IsNullOrWhiteSpace(secondaryText);
            Fraction = fraction;
        }

        public string Label { get; }

        public string ValueText { get; }

        public string SecondaryText { get; }

        public bool HasSecondary { get; }

        public double Fraction { get; }
    }

    /// <summary>
    /// Backs the NativePoints widget: horizontal bars of top-N provider or per-game points, capped
    /// to 3 rows when compact and hiding the provider caption below the secondary-statistics band.
    /// </summary>
    public sealed class NativePointsWidgetViewModel : ShowcaseWidgetViewModelBase
    {
        public BulkObservableCollection<ChartRowViewModel> Rows { get; } =
            new BulkObservableCollection<ChartRowViewModel>();

        protected override void Refresh()
        {
            var entries = Projection?.ChartEntries ?? Array.Empty<ShowcaseChartEntry>();
            if (entries.Count == 0)
            {
                Rows.Clear();
                return;
            }

            var max = Math.Max(1, entries.Max(entry => entry.Value));
            var limit = Density == WidgetViewportDensity.Compact ? 3 : entries.Count;
            var showSecondary = Density != WidgetViewportDensity.Compact;
            Rows.ReplaceAll(entries
                .Take(limit)
                .Select(entry => new ChartRowViewModel(
                    string.IsNullOrWhiteSpace(entry.LabelKey)
                        ? entry.Label ?? string.Empty
                        : ResourceProvider.GetString(entry.LabelKey),
                    entry.Value.ToString("N0", FormattingCulture.Current),
                    entry.SecondaryText,
                    showSecondary,
                    entry.Value / max)));
        }
    }
}

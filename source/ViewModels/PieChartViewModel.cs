using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using LiveCharts;
using LiveCharts.Wpf;
using PlayniteAchievements.Models;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.ViewModels
{
    public class PieChartViewModel : ObservableObject
    {
        /// <summary>
        /// Internal data class for pie slice processing.
        /// Contains both the slice color (for rendering) and the legend color hex (for display).
        /// </summary>
        private sealed class PieSliceData
        {
            public string Label { get; set; }
            public int Count { get; set; }
            public string IconKey { get; set; }
            public Color Color { get; set; }
            public string ColorHex { get; set; }
            public double ChartValue { get; set; }
            public int UnlockedCount { get; set; }
            public int TotalCount { get; set; }
            public bool IsLocked { get; set; }
        }

        public SeriesCollection PieSeries { get; } = new SeriesCollection();
        public ObservableCollection<LegendItem> LegendItems { get; } = new ObservableCollection<LegendItem>();

        // Consistent transparent locked color for all pie charts
        private static readonly Color LockedTransparent = Color.FromArgb(0, 102, 102, 102);
        private const string LockedLegendColor = "#666666";

        /// <summary>
        /// Sets the pie chart data for Games completion (Completed vs Incomplete).
        /// </summary>
        public void SetGameData(int totalGames, int completedGames, string completedLabel, string incompleteLabel)
        {
            if (totalGames == 0)
            {
                SynchronizePieChartAndLegend(new List<PieSliceData>());
                return;
            }

            var incomplete = totalGames - completedGames;

            var dataPoints = new List<(string Label, int Count, string IconKey, Color Color, string OriginalColorHex, int UnlockedCount, int TotalCount, bool IsLocked)>
            {
                (completedLabel, completedGames, "BadgeCompletedGame", Color.FromRgb(33, 150, 243), string.Empty, completedGames, completedGames, false)
            };

            if (incomplete > 0)
            {
                dataPoints.Add((incompleteLabel, incomplete, "BadgeLocked", LockedTransparent, string.Empty, incomplete, incomplete, true));
            }

            ApplyMinimumVisibilityRule(dataPoints);
        }

        /// <summary>
        /// Sets the pie chart data for Rarity distribution (Ultra Rare, Rare, Uncommon, Common, Locked).
        /// </summary>
        public void SetRarityData(
            int commonUnlocked, int uncommonUnlocked, int rareUnlocked, int ultraRareUnlocked, int locked,
            int commonTotal, int uncommonTotal, int rareTotal, int ultraRareTotal,
            string commonLabel, string uncommonLabel, string rareLabel, string ultraRareLabel, string lockedLabel)
        {
            var dataPoints = new List<(string Label, int Count, string IconKey, Color Color, string OriginalColorHex, int UnlockedCount, int TotalCount, bool IsLocked)>();

            if (ultraRareUnlocked > 0 || ultraRareTotal > 0)
            {
                dataPoints.Add((ultraRareLabel, ultraRareUnlocked, "BadgePlatinumHexagon", Color.FromRgb(135, 206, 250), string.Empty, ultraRareUnlocked, ultraRareTotal, false));
            }

            if (rareUnlocked > 0 || rareTotal > 0)
            {
                dataPoints.Add((rareLabel, rareUnlocked, "BadgeGoldPentagon", Color.FromRgb(255, 193, 7), string.Empty, rareUnlocked, rareTotal, false));
            }

            if (uncommonUnlocked > 0 || uncommonTotal > 0)
            {
                dataPoints.Add((uncommonLabel, uncommonUnlocked, "BadgeSilverSquare", Color.FromRgb(158, 158, 158), string.Empty, uncommonUnlocked, uncommonTotal, false));
            }

            if (commonUnlocked > 0 || commonTotal > 0)
            {
                dataPoints.Add((commonLabel, commonUnlocked, "BadgeBronzeTriangle", Color.FromRgb(139, 69, 19), string.Empty, commonUnlocked, commonTotal, false));
            }

            if (locked > 0)
            {
                dataPoints.Add((lockedLabel, locked, "BadgeLocked", LockedTransparent, string.Empty, locked, locked, true));
            }

            ApplyMinimumVisibilityRule(dataPoints);
        }

        /// <summary>
        /// Sets the pie chart data for Provider distribution.
        /// </summary>
        /// <param name="unlockedByProvider">Dictionary of provider name to unlocked count</param>
        /// <param name="totalByProvider">Dictionary of provider name to total count (including locked)</param>
        /// <param name="totalLocked">Total locked achievements</param>
        /// <param name="lockedLabel">Localized label for locked achievements</param>
        /// <param name="providerLookup">Dictionary of provider name to (iconKey, colorHex) tuple</param>
        public void SetProviderData(
            Dictionary<string, int> unlockedByProvider,
            Dictionary<string, int> totalByProvider,
            int totalLocked,
            string lockedLabel,
            Dictionary<string, (string iconKey, string colorHex)> providerLookup)
        {
            if (unlockedByProvider == null || !unlockedByProvider.Any())
            {
                SynchronizePieChartAndLegend(new List<PieSliceData>());
                return;
            }

            var dataPoints = new List<(string Label, int Count, string IconKey, Color Color, string OriginalColorHex, int UnlockedCount, int TotalCount, bool IsLocked)>();

            // Sort providers by count descending
            foreach (var provider in unlockedByProvider.OrderByDescending(p => p.Value))
            {
                var providerName = provider.Key;
                var unlockedCount = provider.Value;
                var totalCount = totalByProvider != null && totalByProvider.TryGetValue(providerName, out var total) ? total : unlockedCount;

                // Look up provider icon and color
                string colorHex = "#888888";
                string iconKey = string.Empty;
                if (providerLookup != null && providerLookup.TryGetValue(providerName, out var metadata))
                {
                    colorHex = metadata.colorHex;
                    iconKey = metadata.iconKey ?? string.Empty;
                }

                // Parse color hex to Color
                if (ColorConverter.ConvertFromString(colorHex) is Color color)
                {
                    dataPoints.Add((providerName, unlockedCount, iconKey, color, colorHex, unlockedCount, totalCount, false));
                }
                else
                {
                    dataPoints.Add((providerName, unlockedCount, iconKey, Colors.Gray, "#888888", unlockedCount, totalCount, false));
                }
            }

            if (totalLocked > 0)
            {
                dataPoints.Add((lockedLabel, totalLocked, "BadgeLocked", LockedTransparent, string.Empty, totalLocked, totalLocked, true));
            }

            ApplyMinimumVisibilityRule(dataPoints);
        }

        private void ApplyMinimumVisibilityRule(List<(string Label, int Count, string IconKey, Color Color, string OriginalColorHex, int UnlockedCount, int TotalCount, bool IsLocked)> dataPoints)
        {
            if (dataPoints == null || dataPoints.Count == 0)
            {
                SynchronizePieChartAndLegend(new List<PieSliceData>());
                return;
            }

            var totalCount = dataPoints.Sum(d => d.Count);
            var minSlice = totalCount * 0.05;
            var adjustments = new Dictionary<int, double>();
            var totalAdjustment = 0.0;

            // First pass: identify slices below 5% and calculate adjustments
            for (int i = 0; i < dataPoints.Count; i++)
            {
                if (dataPoints[i].Count < minSlice && dataPoints[i].Count > 0)
                {
                    var adjustment = minSlice - dataPoints[i].Count;
                    adjustments[i] = adjustment;
                    totalAdjustment += adjustment;
                }
            }

            // Find the largest slice to subtract from.
            int largestSliceIndex = dataPoints
                .Select((d, i) => (d.Count, i))
                .OrderByDescending(x => x.Item1)
                .First().Item2;

            var slices = new List<PieSliceData>(dataPoints.Count);
            for (int i = 0; i < dataPoints.Count; i++)
            {
                double chartValue;
                if (adjustments.ContainsKey(i))
                {
                    // Small slice: bump to 5%
                    chartValue = minSlice;
                }
                else if (i == largestSliceIndex && totalAdjustment > 0)
                {
                    // Largest slice: subtract the total adjustment
                    chartValue = dataPoints[i].Count - totalAdjustment;
                }
                else
                {
                    // Normal slice: use actual value
                    chartValue = dataPoints[i].Count;
                }

                // Determine legend color: use OriginalColorHex for providers, otherwise generate from Color
                string legendColor;
                if (!string.IsNullOrEmpty(dataPoints[i].OriginalColorHex))
                {
                    // Use original color hex from provider (e.g., "#B0B0B0")
                    legendColor = dataPoints[i].OriginalColorHex;
                }
                else if (dataPoints[i].IconKey == "BadgeLocked")
                {
                    // Locked items use consistent color
                    legendColor = LockedLegendColor;
                }
                else
                {
                    // Generate from Color (for badges)
                    legendColor = $"#{dataPoints[i].Color.R:X2}{dataPoints[i].Color.G:X2}{dataPoints[i].Color.B:X2}";
                }

                slices.Add(new PieSliceData
                {
                    Label = dataPoints[i].Label,
                    Count = dataPoints[i].Count,
                    IconKey = dataPoints[i].IconKey,
                    Color = dataPoints[i].Color,
                    ColorHex = legendColor,
                    ChartValue = chartValue,
                    UnlockedCount = dataPoints[i].UnlockedCount,
                    TotalCount = dataPoints[i].TotalCount,
                    IsLocked = dataPoints[i].IsLocked
                });
            }

            SynchronizePieChartAndLegend(slices);
        }

        private void SynchronizePieChartAndLegend(IReadOnlyList<PieSliceData> slices)
        {
            SynchronizePieSeries(slices);
            SynchronizeLegendItems(slices);
        }

        private void SynchronizePieSeries(IReadOnlyList<PieSliceData> slices)
        {
            for (int i = 0; i < slices.Count; i++)
            {
                LiveCharts.Wpf.PieSeries series = null;
                if (i < PieSeries.Count)
                {
                    series = PieSeries[i] as LiveCharts.Wpf.PieSeries;
                }

                if (series == null)
                {
                    series = new LiveCharts.Wpf.PieSeries();
                    if (i < PieSeries.Count)
                    {
                        PieSeries[i] = series;
                    }
                    else
                    {
                        PieSeries.Add(series);
                    }
                }

                var slice = slices[i];
                series.Title = slice.Label;
                series.DataLabels = false;

                var existingBrush = series.Fill as SolidColorBrush;
                if (existingBrush == null || existingBrush.Color != slice.Color)
                {
                    series.Fill = new SolidColorBrush(slice.Color);
                }

                var values = series.Values as ChartValues<PieSliceChartData>;
                if (values == null)
                {
                    values = new ChartValues<PieSliceChartData>();
                    series.Values = values;
                }

                var chartData = new PieSliceChartData
                {
                    Label = slice.Label,
                    Count = slice.Count,
                    IconKey = slice.IconKey,
                    ColorHex = slice.ColorHex,
                    ChartValue = slice.ChartValue,
                    UnlockedCount = slice.UnlockedCount,
                    TotalCount = slice.TotalCount,
                    IsLocked = slice.IsLocked
                };

                if (values.Count == 0)
                {
                    values.Add(chartData);
                }
                else
                {
                    values[0] = chartData;
                    while (values.Count > 1)
                    {
                        values.RemoveAt(values.Count - 1);
                    }
                }
            }

            while (PieSeries.Count > slices.Count)
            {
                PieSeries.RemoveAt(PieSeries.Count - 1);
            }
        }

        private void SynchronizeLegendItems(IReadOnlyList<PieSliceData> slices)
        {
            for (int i = 0; i < slices.Count; i++)
            {
                LegendItem item;
                if (i < LegendItems.Count)
                {
                    item = LegendItems[i];
                }
                else
                {
                    item = new LegendItem();
                    LegendItems.Add(item);
                }

                var slice = slices[i];
                item.Label = slice.Label;
                item.Count = slice.Count;
                item.IconKey = slice.IconKey;
                item.ColorHex = slice.ColorHex;
            }

            while (LegendItems.Count > slices.Count)
            {
                LegendItems.RemoveAt(LegendItems.Count - 1);
            }
        }
    }
}

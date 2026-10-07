using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services.Showcase;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>A single statistic tile: a formatted value over its localized label.</summary>
    public sealed class StatTileViewModel
    {
        public StatTileViewModel(string value, string label)
        {
            Value = value;
            Label = label;
        }

        public string Value { get; }

        public string Label { get; }
    }

    /// <summary>
    /// Backs the Statistics widget: a grid of stat tiles. Every size shows all tiles (the
    /// body scrolls); a Tall viewport arranges them in a single column.
    /// </summary>
    public sealed class StatisticsWidgetViewModel : ShowcaseWidgetViewModelBase
    {
        private int _columns = 2;

        public BulkObservableCollection<StatTileViewModel> Tiles { get; } =
            new BulkObservableCollection<StatTileViewModel>();

        public int Columns
        {
            get => _columns;
            private set => SetValue(ref _columns, value);
        }

        protected override void Refresh()
        {
            IEnumerable<ShowcaseStatistic> items = Projection?.Statistics ?? Array.Empty<ShowcaseStatistic>();
            // The widget's chosen statistics, in the chosen order; every one when none were chosen.
            var keys = ShowcaseWidgetOptions.GetStatisticsKeys(Projection?.Instance);
            if (keys != null)
            {
                var byKey = items.Where(item => item?.Key != null)
                    .GroupBy(item => item.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                items = keys.Where(byKey.ContainsKey).Select(key => byKey[key]).ToList();
            }

            Columns = Orientation == WidgetViewportOrientation.Tall ? 1 : 2;
            Tiles.ReplaceAll(items
                .Select(item => new StatTileViewModel(
                    ShowcaseStatisticFormatter.Format(item),
                    ResourceProvider.GetString(item.LabelKey))));
        }
    }
}

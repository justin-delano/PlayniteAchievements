using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Backs the Pie widget by reusing PieChartWithRadialIcons/PieChartViewModel. Builds the chart
    /// for the configured distribution (completed games / provider / rarity / trophy); the legend
    /// and its side are per-widget settings that the chart control renders. A fresh chart is
    /// produced per refresh so the bound control always reflects the latest data.
    /// </summary>
    public sealed class PieWidgetViewModel : ShowcaseWidgetViewModelBase, IDisposable
    {
        private PieChartViewModel _chart = new PieChartViewModel();

        public PieChartViewModel Chart { get => _chart; private set => SetValue(ref _chart, value); }

        protected override void Refresh()
        {
            var snapshot = Projection?.Snapshot ?? new OverviewDataSnapshot();
            var mode = ShowcaseWidgetOptions.GetPieMode(Projection?.Instance);
            var chart = new PieChartViewModel
            {
                // Both are applied by each Set*Data call, so they must be assigned before
                // the data.
                SmallSliceMode = ShowcaseWidgetOptions.GetPieSmallSliceMode(Projection?.Instance),
                IncludeLocked = ShowcaseWidgetOptions.GetPieIncludeLocked(Projection?.Instance),
                CenterMode = ShowcaseWidgetOptions.GetPieCenterMode(Projection?.Instance),
                ShowIcons = ShowcaseWidgetOptions.GetPieShowIcons(Projection?.Instance),
                ShowLegend = ShowcaseWidgetOptions.GetPieShowLegend(Projection?.Instance),
                LegendPosition = ShowcaseWidgetOptions.GetPieLegendPosition(Projection?.Instance)
            };
            switch (mode)
            {
                case ShowcasePieMode.Provider:
                    var games = snapshot.GameSummaries ?? new List<GameSummaryItem>();
                    var providerGroups = games
                        .Where(game => game != null && !string.IsNullOrWhiteSpace(game.ProviderKey))
                        .GroupBy(game => game.ProviderKey, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var metadata = providerGroups.ToDictionary(
                        group => group.Key,
                        group => (
                            group.Select(game => game.ProviderIconKey)
                                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                            group.Select(game => game.ProviderColorHex)
                                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "#888888"),
                        StringComparer.OrdinalIgnoreCase);
                    var providerNames = providerGroups.ToDictionary(
                        group => group.Key,
                        group => group.Select(game => game.Provider)
                            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? group.Key,
                        StringComparer.OrdinalIgnoreCase);
                    chart.SetProviderData(
                        snapshot.UnlockedByProvider,
                        snapshot.TotalByProvider,
                        snapshot.TotalLocked,
                        Localize("LOCPlayAch_Common_Locked"),
                        metadata,
                        providerNames);
                    break;
                case ShowcasePieMode.Rarity:
                    chart.SetRarityData(
                        snapshot.TotalCommon,
                        snapshot.TotalUncommon,
                        snapshot.TotalRare,
                        snapshot.TotalUltraRare,
                        snapshot.TotalLocked,
                        snapshot.TotalCommonPossible,
                        snapshot.TotalUncommonPossible,
                        snapshot.TotalRarePossible,
                        snapshot.TotalUltraRarePossible,
                        Localize("LOCPlayAch_Rarity_Common"),
                        Localize("LOCPlayAch_Rarity_Uncommon"),
                        Localize("LOCPlayAch_Rarity_Rare"),
                        Localize("LOCPlayAch_Rarity_UltraRare"),
                        Localize("LOCPlayAch_Common_Locked"));
                    break;
                case ShowcasePieMode.Trophy:
                    chart.SetTrophyData(
                        snapshot.TotalPlatinum,
                        snapshot.TotalGold,
                        snapshot.TotalSilver,
                        snapshot.TotalBronze,
                        snapshot.TotalPlatinumPossible,
                        snapshot.TotalGoldPossible,
                        snapshot.TotalSilverPossible,
                        snapshot.TotalBronzePossible,
                        Localize("LOCPlayAch_Trophy_Platinum"),
                        Localize("LOCPlayAch_Trophy_Gold"),
                        Localize("LOCPlayAch_Trophy_Silver"),
                        Localize("LOCPlayAch_Trophy_Bronze"),
                        Localize("LOCPlayAch_Common_Locked"));
                    break;
                default:
                    chart.SetGameData(
                        snapshot.PossibleCompletions,
                        snapshot.Completions,
                        Localize("LOCPlayAch_Completed"),
                        Localize("LOCPlayAch_Overview_Incomplete"));
                    break;
            }

            // The replaced chart is subscribed to the process-lifetime appearance event, so it
            // must be released explicitly; otherwise every refresh strands one chart view model
            // and its whole series/slice/legend graph in memory for the rest of the session.
            var previous = Chart;
            Chart = chart;
            if (!ReferenceEquals(previous, chart))
            {
                previous?.Dispose();
            }
        }

        /// <summary>
        /// Releases the current chart. Refresh only disposes the chart it replaces, so without
        /// this the last one survives whenever the widget view model itself is discarded (a kind
        /// swap, a deleted widget, dashboard teardown) and stays rooted by the process-lifetime
        /// appearance event for the rest of the session.
        /// </summary>
        public void Dispose()
        {
            _chart?.Dispose();
        }

        private static string Localize(string key) => ResourceProvider.GetString(key);
    }
}

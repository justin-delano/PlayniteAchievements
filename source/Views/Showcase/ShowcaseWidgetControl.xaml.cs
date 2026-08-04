using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;
using PlayniteAchievements.Views.Controls;
using PlayniteAchievements.Views.Dialogs;
using PlayniteAchievements.Views.Helpers;
using ChartAxis = LiveCharts.Wpf.Axis;
using ChartColumnSeries = LiveCharts.Wpf.ColumnSeries;
using ChartControl = LiveCharts.Wpf.CartesianChart;
using ChartSeparator = LiveCharts.Wpf.Separator;

namespace PlayniteAchievements.Views.Showcase
{
    public partial class ShowcaseWidgetControl : UserControl
    {
        public static readonly DependencyProperty ProjectionProperty =
            DependencyProperty.Register(
                nameof(Projection),
                typeof(ShowcaseWidgetProjection),
                typeof(ShowcaseWidgetControl),
                new PropertyMetadata(null, OnProjectionChanged));

        private ShowcaseWidgetProjection _projection;
        private WidgetViewportState _viewport = WidgetViewportState.Classify(0, 0);
        private TimelineViewModel _timelineViewModel;

        public ShowcaseWidgetControl()
        {
            InitializeComponent();
        }

        public ShowcaseWidgetProjection Projection
        {
            get => (ShowcaseWidgetProjection)GetValue(ProjectionProperty);
            set => SetValue(ProjectionProperty, value);
        }

        public void Apply(ShowcaseWidgetProjection projection)
        {
            Projection = projection;
        }

        private static void OnProjectionChanged(
            DependencyObject sender,
            DependencyPropertyChangedEventArgs e)
        {
            if (!(sender is ShowcaseWidgetControl control))
            {
                return;
            }

            control._projection = e.NewValue as ShowcaseWidgetProjection;
            control.UpdateTitle();
            control.RebuildBody();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            var next = WidgetViewportState.Classify(e.NewSize.Width, e.NewSize.Height);
            if (next.Density == _viewport.Density &&
                next.Orientation == _viewport.Orientation)
            {
                return;
            }

            _viewport = next;
            RebuildBody();
        }

        private void UpdateTitle()
        {
            if (_projection?.Instance == null)
            {
                TitleText.Text = string.Empty;
                GlyphText.Text = string.Empty;
                return;
            }

            var custom = _projection.Instance.CustomTitle?.Trim();
            TitleText.Text = !string.IsNullOrWhiteSpace(custom)
                ? custom
                : Localize(
                    ShowcaseWidgetCatalog.Get(_projection.Instance.Kind).NameKey,
                    Humanize(_projection.Instance.Kind));
            GlyphText.Text = GetWidgetGlyph(_projection.Instance.Kind);
        }

        private void RebuildBody()
        {
            if (_projection?.Instance == null)
            {
                BodyHost.Content = CreateEmptyText();
                return;
            }

            switch (_projection.Instance.Kind)
            {
                case ShowcaseWidgetKind.Profile:
                    BodyHost.Content = BuildProfile();
                    break;
                case ShowcaseWidgetKind.Scores:
                    BodyHost.Content = BuildScores();
                    break;
                case ShowcaseWidgetKind.Pie:
                    BodyHost.Content = BuildPie();
                    break;
                case ShowcaseWidgetKind.Timeline:
                    BodyHost.Content = BuildTimeline();
                    break;
                case ShowcaseWidgetKind.Statistics:
                    BodyHost.Content = BuildStatistics();
                    break;
                case ShowcaseWidgetKind.NativePoints:
                    BodyHost.Content = BuildChartRows(_projection.ChartEntries);
                    break;
                case ShowcaseWidgetKind.PinnedAchievements:
                    BodyHost.Content = BuildAchievements(_projection.Achievements);
                    break;
                case ShowcaseWidgetKind.FavoriteGames:
                    BodyHost.Content = BuildGames(_projection.Games);
                    break;
                case ShowcaseWidgetKind.IconMosaic:
                    BodyHost.Content = BuildMosaic(_projection.MosaicAchievements);
                    break;
                case ShowcaseWidgetKind.ScreenshotSlideshow:
                    BodyHost.Content = new ScreenshotSlideshowControl(_projection.Instance);
                    break;
                default:
                    BodyHost.Content = CreateEmptyText();
                    break;
            }
        }

        private UIElement BuildProfile()
        {
            var profile = _projection.Profile ?? new ShowcaseProfileSettings();
            var panel = new Grid();
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (!string.IsNullOrWhiteSpace(profile.BackgroundPath))
            {
                var background = CreateImage(profile.BackgroundPath, 160);
                background.Width = double.NaN;
                background.Height = double.NaN;
                background.Stretch = Stretch.UniformToFill;
                background.Opacity = 0.2;
                background.IsHitTestVisible = false;
                Grid.SetColumnSpan(background, 2);
                panel.Children.Add(background);
            }

            if (!string.IsNullOrWhiteSpace(profile.AvatarPath))
            {
                var avatar = CreateImage(profile.AvatarPath, _viewport.Density == WidgetViewportDensity.Compact ? 42 : 72);
                var avatarFrame = CreateImageFrame(avatar);
                avatarFrame.Margin = new Thickness(0, 0, 12, 0);
                panel.Children.Add(avatarFrame);
            }

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            text.Children.Add(CreateText(
                string.IsNullOrWhiteSpace(profile.DisplayName)
                    ? Localize("LOCPlayAch_Showcase_Profile_DefaultName", "Achievement Showcase")
                    : profile.DisplayName,
                18,
                FontWeights.SemiBold));
            if (!string.IsNullOrWhiteSpace(profile.Subtitle) &&
                _viewport.Density != WidgetViewportDensity.Compact)
            {
                text.Children.Add(CreateText(profile.Subtitle, 12, FontWeights.Normal, 0.72));
            }

            if (_viewport.ShowSecondaryStatistics)
            {
                var snapshot = _projection.Snapshot;
                text.Children.Add(CreateText(
                    string.Format(
                        FormattingCulture.Current,
                        Localize(
                            "LOCPlayAch_Showcase_ProfileStats",
                            "{0:N0} unlocked · {1:N1}% · {2:N0} completed"),
                        snapshot.TotalUnlocked,
                        snapshot.GlobalProgressionPercent,
                        snapshot.CompletedGames),
                    11,
                    FontWeights.Normal,
                    0.72));
            }

            panel.Children.Add(text);
            return panel;
        }

        private UIElement BuildScores()
        {
            var snapshot = _projection.Snapshot;
            var mode = _projection.Instance.GetOption("Mode", ShowcaseScoreMode.Dual);
            var includeCollection = mode != ShowcaseScoreMode.Prestige;
            var includePrestige = mode != ShowcaseScoreMode.Collection;
            var scoreCount = (includeCollection ? 1 : 0) + (includePrestige ? 1 : 0);
            var panel = new UniformGrid
            {
                Rows = scoreCount > 1 && _viewport.Orientation == WidgetViewportOrientation.Tall ? 2 : 1,
                Columns = scoreCount > 1 && _viewport.Orientation != WidgetViewportOrientation.Tall ? 2 : 1,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (includeCollection)
            {
                panel.Children.Add(BuildScoreCard(
                    ScoreCardType.Collection,
                    snapshot.CollectorScore,
                    snapshot.CollectorLevel,
                    snapshot.CollectorRank,
                    snapshot.CollectorLevelProgress));
            }

            if (includePrestige)
            {
                panel.Children.Add(BuildScoreCard(
                    ScoreCardType.Prestige,
                    snapshot.PrestigeScore,
                    snapshot.PrestigeLevel,
                    snapshot.PrestigeRank,
                    snapshot.PrestigeLevelProgress));
            }

            return panel;
        }

        private UIElement BuildScoreCard(
            ScoreCardType scoreType,
            int score,
            int level,
            string rank,
            double progress)
        {
            var presentation = new ScoreCardViewModel(scoreType);
            presentation.Apply(
                score,
                level,
                progress,
                rank,
                PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.UseUniformRarityBadges ?? false);
            var control = new ScoreCardControl
            {
                ScoreCard = presentation,
                Margin = new Thickness(4),
                MinWidth = 0,
                MaxWidth = 360,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            control.InfoRequested += (_, __) => ScoreInfoDialogPresenter.Show();
            return control;
        }

        private UIElement BuildPie()
        {
            var snapshot = _projection.Snapshot;
            var mode = _projection.Instance.GetOption("Mode", ShowcasePieMode.CompletedGames);
            var chart = new PieChartViewModel();
            switch (mode)
            {
                case ShowcasePieMode.Provider:
                    var games = snapshot.GameSummaries ?? new List<GameSummaryItem>();
                    var metadata = games
                        .Where(game => game != null && !string.IsNullOrWhiteSpace(game.ProviderKey))
                        .GroupBy(game => game.ProviderKey, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => (
                                group.Select(game => game.ProviderIconKey).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                                group.Select(game => game.ProviderColorHex).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "#888888"),
                            StringComparer.OrdinalIgnoreCase);
                    var providerNames = games
                        .Where(game => game != null && !string.IsNullOrWhiteSpace(game.ProviderKey))
                        .GroupBy(game => game.ProviderKey, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => group.Select(game => game.Provider).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? group.Key,
                            StringComparer.OrdinalIgnoreCase);
                    chart.SetProviderData(
                        snapshot.UnlockedByProvider,
                        snapshot.TotalByProvider,
                        snapshot.TotalLocked,
                        Localize("LOCPlayAch_Common_Locked", "Locked"),
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
                        Localize("LOCPlayAch_Rarity_Common", "Common"),
                        Localize("LOCPlayAch_Rarity_Uncommon", "Uncommon"),
                        Localize("LOCPlayAch_Rarity_Rare", "Rare"),
                        Localize("LOCPlayAch_Rarity_UltraRare", "Ultra rare"),
                        Localize("LOCPlayAch_Common_Locked", "Locked"));
                    break;
                case ShowcasePieMode.Trophy:
                    var trophyGames = snapshot.GameSummaries ?? new List<GameSummaryItem>();
                    chart.SetTrophyData(
                        trophyGames.Sum(game => game?.TrophyPlatinumCount ?? 0),
                        trophyGames.Sum(game => game?.TrophyGoldCount ?? 0),
                        trophyGames.Sum(game => game?.TrophySilverCount ?? 0),
                        trophyGames.Sum(game => game?.TrophyBronzeCount ?? 0),
                        trophyGames.Sum(game => game?.TrophyPlatinumTotal ?? 0),
                        trophyGames.Sum(game => game?.TrophyGoldTotal ?? 0),
                        trophyGames.Sum(game => game?.TrophySilverTotal ?? 0),
                        trophyGames.Sum(game => game?.TrophyBronzeTotal ?? 0),
                        Localize("LOCPlayAch_Trophy_Platinum", "Platinum"),
                        Localize("LOCPlayAch_Trophy_Gold", "Gold"),
                        Localize("LOCPlayAch_Trophy_Silver", "Silver"),
                        Localize("LOCPlayAch_Trophy_Bronze", "Bronze"),
                        Localize("LOCPlayAch_Common_Locked", "Locked"));
                    break;
                default:
                    chart.SetGameData(
                        snapshot.TotalGames,
                        snapshot.CompletedGames,
                        Localize("LOCPlayAch_Completed", "Completed"),
                        Localize("LOCPlayAch_Showcase_Incomplete", "Incomplete"));
                    break;
            }

            var control = new PieChartWithRadialIcons
            {
                PieSeries = chart.PieSeries,
                LegendItems = chart.LegendItems,
                HighlightedLabels = chart.HighlightedLabels,
                ExactUnlockedCount = chart.ExactUnlockedCount,
                ExactTotalCount = chart.ExactTotalCount,
                ShowCenterPercentage = true,
                MinHeight = 70
            };
            if (!_viewport.ShowLegend)
            {
                return control;
            }

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            root.Children.Add(control);
            var legend = BuildPieLegend(chart.LegendItems);
            Grid.SetColumn(legend, 1);
            root.Children.Add(legend);
            return root;
        }

        private UIElement BuildTimeline()
        {
            if (_viewport.Density != WidgetViewportDensity.Compact)
            {
                return BuildEstablishedTimeline();
            }

            var orderedValues = (_projection.Timeline ?? new Dictionary<DateTime, int>())
                .OrderBy(pair => pair.Key)
                .Select(pair => Math.Max(0, pair.Value))
                .ToList();
            const int desiredCount = 14;
            var values = orderedValues
                .Skip(Math.Max(0, orderedValues.Count - desiredCount))
                .ToList();
            if (values.Count == 0)
            {
                return CreateEmptyText();
            }

            var maximum = Math.Max(1, values.Max());
            var chart = new UniformGrid
            {
                Rows = 1,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            foreach (var value in values)
            {
                var column = new Grid { Margin = new Thickness(1, 0, 1, 0) };
                var bar = new Border
                {
                    Height = Math.Max(2, 80d * value / maximum),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    CornerRadius = new CornerRadius(1),
                    ToolTip = value.ToString("N0", FormattingCulture.Current)
                };
                bar.SetResourceReference(Border.BackgroundProperty, "PlayAch.Brush.Accent");
                column.Children.Add(bar);
                chart.Children.Add(column);
            }

            return chart;
        }

        private UIElement BuildEstablishedTimeline()
        {
            _timelineViewModel = _timelineViewModel ?? new TimelineViewModel();
            _timelineViewModel.TimelineRange = ShowcaseTimelineOptions.GetRange(_projection.Instance);
            _timelineViewModel.SetCounts(
                (_projection.Timeline ?? new Dictionary<DateTime, int>())
                    .ToDictionary(pair => pair.Key, pair => pair.Value));

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var chart = new ChartControl
            {
                Series = _timelineViewModel.TimelineSeries,
                LegendLocation = LiveCharts.LegendLocation.None,
                Hoverable = true,
                DisableAnimations = true
            };
            var seriesStyle = new Style(typeof(ChartColumnSeries));
            seriesStyle.Setters.Add(new Setter(
                ChartColumnSeries.FillProperty,
                new DynamicResourceExtension("PlayAch.Brush.Accent")));
            chart.Resources[typeof(ChartColumnSeries)] = seriesStyle;

            var xAxis = new ChartAxis
            {
                Labels = _timelineViewModel.TimelineLabels,
                ShowLabels = true,
                LabelsRotation = 45,
                FontSize = 10,
                Separator = new ChartSeparator { Step = 1, IsEnabled = false }
            };
            xAxis.SetResourceReference(Control.ForegroundProperty, "PlayAch.Brush.Text");
            chart.AxisX.Add(xAxis);
            var yAxis = new ChartAxis
            {
                MinValue = 0,
                LabelFormatter = _timelineViewModel.YAxisFormatter,
                FontSize = 10,
                Separator = new ChartSeparator { Opacity = 0.2 }
            };
            yAxis.SetResourceReference(Control.ForegroundProperty, "PlayAch.Brush.Text");
            yAxis.Separator.SetResourceReference(ChartSeparator.StrokeProperty, "PlayAch.Brush.Border");
            chart.AxisY.Add(yAxis);
            root.Children.Add(chart);

            if (_viewport.Density != WidgetViewportDensity.Expanded)
            {
                return root;
            }

            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            };
            foreach (var option in new[]
                     {
                         TimelineRange.OneMonth,
                         TimelineRange.ThreeMonths,
                         TimelineRange.OneYear,
                         TimelineRange.All
                     })
            {
                var range = option;
                var button = new Button
                {
                    Content = TimelineRangeName(range),
                    Margin = new Thickness(2, 0, 2, 0),
                    Padding = new Thickness(6, 1, 6, 1)
                };
                button.Click += (_, __) =>
                {
                    ShowcaseTimelineOptions.SetRange(_projection.Instance, range);
                    var plugin = PlayniteAchievementsPlugin.Instance;
                    plugin?.PersistSettingsForUi();
                    ShowcaseConfigurationEvents.RaiseChanged();
                };
                controls.Children.Add(button);
            }

            Grid.SetRow(controls, 1);
            root.Children.Add(controls);
            return root;
        }

        private static string TimelineRangeName(TimelineRange range)
        {
            switch (range)
            {
                case TimelineRange.OneMonth:
                    return Localize("LOCPlayAch_TimeRange_1M", "1M");
                case TimelineRange.ThreeMonths:
                    return Localize("LOCPlayAch_TimeRange_3M", "3M");
                case TimelineRange.OneYear:
                    return Localize("LOCPlayAch_TimeRange_1Y", "1Y");
                case TimelineRange.All:
                    return Localize("LOCPlayAch_Common_All", "All");
                default:
                    return range.ToString();
            }
        }

        private UIElement BuildPieLegend(IEnumerable<LegendItem> items)
        {
            var panel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            var limit = _viewport.Density == WidgetViewportDensity.Expanded ? 8 : 5;
            foreach (var item in (items ?? Array.Empty<LegendItem>()).Take(limit))
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var swatch = new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 0, 7, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = TryCreateBrush(item.ColorHex)
                };
                row.Children.Add(swatch);
                var label = CreateText(item.Label, 10, FontWeights.Normal, 0.74);
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                var count = CreateText(
                    item.Count.ToString("N0", FormattingCulture.Current),
                    10,
                    FontWeights.SemiBold);
                count.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(count, 2);
                row.Children.Add(count);
                panel.Children.Add(row);
            }

            return panel;
        }

        private UIElement BuildStatistics()
        {
            var items = _projection.Statistics ?? Array.Empty<ShowcaseStatistic>();
            var limit = _viewport.Density == WidgetViewportDensity.Compact
                ? 3
                : _viewport.Density == WidgetViewportDensity.Standard
                    ? 7
                    : items.Count;
            var grid = new UniformGrid
            {
                Columns = _viewport.Orientation == WidgetViewportOrientation.Tall ? 1 : 2
            };
            foreach (var item in items.Take(limit))
            {
                var panel = new StackPanel();
                panel.Children.Add(CreateText(
                    FormatStatisticDisplayValue(item),
                    17,
                    FontWeights.SemiBold));
                panel.Children.Add(CreateText(
                    Localize(item.LabelKey, item.Label),
                    10,
                    FontWeights.Normal,
                    0.68));
                grid.Children.Add(CreateCard(
                    panel,
                    new Thickness(3),
                    new Thickness(8, 6, 8, 6)));
            }

            return WrapScrollable(grid);
        }

        private UIElement BuildChartRows(IReadOnlyList<ShowcaseChartEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return CreateEmptyText();
            }

            var max = Math.Max(1, entries.Max(item => item.Value));
            var panel = new StackPanel();
            var limit = _viewport.Density == WidgetViewportDensity.Compact ? 3 : entries.Count;
            foreach (var entry in entries.Take(limit))
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = CreateText(
                    Localize(entry.LabelKey, entry.Label),
                    11,
                    FontWeights.Normal);
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                row.Children.Add(label);
                var value = CreateText(
                    entry.Value.ToString("N0", FormattingCulture.Current),
                    11,
                    FontWeights.SemiBold);
                Grid.SetColumn(value, 1);
                row.Children.Add(value);
                var progress = new ProgressBar
                {
                    Minimum = 0,
                    Maximum = max,
                    Value = entry.Value,
                    Height = 3,
                    Margin = new Thickness(0, 19, 0, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetColumnSpan(progress, 2);
                row.Children.Add(progress);
                panel.Children.Add(CreateCard(
                    row,
                    new Thickness(0, 1, 0, 5),
                    new Thickness(8, 5, 8, 5)));
            }

            return WrapScrollable(panel);
        }

        private UIElement BuildAchievements(IReadOnlyList<ShowcaseAchievementItem> achievements)
        {
            if (achievements == null || achievements.Count == 0)
            {
                return CreateEmptyText(Localize(
                    "LOCPlayAch_Showcase_NoPinnedAchievements",
                    "Pin achievements to see them here."));
            }

            var panel = new StackPanel();
            var limit = _viewport.Density == WidgetViewportDensity.Compact ? 2 : achievements.Count;
            foreach (var achievement in achievements.Take(limit))
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var image = CreateImage(achievement.IconPath, 38);
                image.Opacity = achievement.IsMissing ? 0.35 : 1;
                row.Children.Add(image);
                var text = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(CreateText(
                    achievement.IsMissing &&
                    string.IsNullOrWhiteSpace(achievement.Pin?.LastKnownAchievementName)
                        ? Localize(
                            "LOCPlayAch_Showcase_UnavailableAchievement",
                            achievement.Name)
                        : achievement.Name,
                    11,
                    FontWeights.SemiBold));
                if (_viewport.ShowSecondaryStatistics)
                {
                    text.Children.Add(CreateText(
                        achievement.IsMissing &&
                        string.IsNullOrWhiteSpace(achievement.Pin?.LastKnownGameName)
                            ? Localize(
                                "LOCPlayAch_Showcase_UnavailableGame",
                                achievement.GameName)
                            : achievement.GameName,
                        10,
                        FontWeights.Normal,
                        0.68));
                }

                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                if (achievement.Pin != null)
                {
                    var captured = achievement.Pin;
                    var menu = new ContextMenu();
                    menu.Items.Add(CreatePinOrderItem(
                        Localize("LOCPlayAch_Showcase_MoveEarlier", "Move earlier"),
                        () => ShowcasePinService.MoveAchievement(
                            PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.Showcase,
                            captured.GameId,
                            captured.ApiName,
                            -1)));
                    menu.Items.Add(CreatePinOrderItem(
                        Localize("LOCPlayAch_Showcase_MoveLater", "Move later"),
                        () => ShowcasePinService.MoveAchievement(
                            PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.Showcase,
                            captured.GameId,
                            captured.ApiName,
                            1)));
                    menu.Items.Add(new Separator());
                    var unpin = new MenuItem
                    {
                        Header = Localize(
                            "LOCPlayAch_Showcase_UnpinAchievement",
                            "Unpin from Showcase")
                    };
                    unpin.Click += (_, __) =>
                    {
                        var plugin = PlayniteAchievementsPlugin.Instance;
                        var settings = plugin?.Settings?.Persisted?.Showcase;
                        if (settings == null)
                        {
                            return;
                        }

                        ShowcasePinService.ToggleAchievement(
                            settings,
                            captured.GameId,
                            captured.ApiName,
                            captured.LastKnownGameName,
                            captured.LastKnownAchievementName);
                        plugin.PersistSettingsForUi();
                        ShowcaseConfigurationEvents.RaiseChanged();
                    };
                    menu.Items.Add(unpin);
                    row.ContextMenu = menu;
                }

                panel.Children.Add(CreateCard(
                    row,
                    new Thickness(0, 1, 0, 5),
                    new Thickness(7, 6, 7, 6)));
            }

            return WrapScrollable(panel);
        }

        private UIElement BuildGames(IReadOnlyList<GameSummaryItem> games)
        {
            if (games == null || games.Count == 0)
            {
                return CreateEmptyText(Localize(
                    "LOCPlayAch_Showcase_NoFavoriteGames",
                    "Pin games or use Playnite favorites."));
            }

            var panel = new WrapPanel();
            var size = _viewport.Density == WidgetViewportDensity.Compact ? 54 : 78;
            foreach (var game in games.Take(_viewport.Density == WidgetViewportDensity.Compact ? 3 : 12))
            {
                var tile = new StackPanel { Width = size + 10 };
                tile.Children.Add(CreateImage(game.GameCoverPath ?? game.GameLogo, size));
                if (_viewport.Density != WidgetViewportDensity.Compact)
                {
                    var name = CreateText(game.GameName, 10, FontWeights.Normal);
                    name.TextAlignment = TextAlignment.Center;
                    name.TextTrimming = TextTrimming.CharacterEllipsis;
                    tile.Children.Add(name);
                }

                if (_projection.Instance.GetOption(
                        "Source",
                        ShowcaseFavoriteGameSource.ShowcasePins) ==
                    ShowcaseFavoriteGameSource.ShowcasePins &&
                    game.PlayniteGameId.HasValue)
                {
                    var capturedGameId = game.PlayniteGameId.Value;
                    var menu = new ContextMenu();
                    menu.Items.Add(CreatePinOrderItem(
                        Localize("LOCPlayAch_Showcase_MoveEarlier", "Move earlier"),
                        () => ShowcasePinService.MoveGame(
                            PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.Showcase,
                            capturedGameId,
                            -1)));
                    menu.Items.Add(CreatePinOrderItem(
                        Localize("LOCPlayAch_Showcase_MoveLater", "Move later"),
                        () => ShowcasePinService.MoveGame(
                            PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.Showcase,
                            capturedGameId,
                            1)));
                    menu.Items.Add(new Separator());
                    var unpin = new MenuItem
                    {
                        Header = Localize(
                            "LOCPlayAch_Showcase_UnpinGame",
                            "Unpin game from Showcase")
                    };
                    unpin.Click += (_, __) =>
                    {
                        var plugin = PlayniteAchievementsPlugin.Instance;
                        var settings = plugin?.Settings?.Persisted?.Showcase;
                        if (settings == null)
                        {
                            return;
                        }

                        ShowcasePinService.ToggleGame(settings, capturedGameId);
                        plugin.PersistSettingsForUi();
                        ShowcaseConfigurationEvents.RaiseChanged();
                    };
                    menu.Items.Add(unpin);
                    tile.ContextMenu = menu;
                }

                panel.Children.Add(CreateCard(
                    tile,
                    new Thickness(3),
                    new Thickness(5)));
            }

            return WrapScrollable(panel);
        }

        private UIElement BuildMosaic(IReadOnlyList<AchievementDisplayItem> achievements)
        {
            if (achievements == null || achievements.Count == 0)
            {
                return CreateEmptyText();
            }

            var panel = new WrapPanel();
            var size = _viewport.Density == WidgetViewportDensity.Compact
                ? 34
                : _viewport.Density == WidgetViewportDensity.Expanded ? 58 : 44;
            var limit = _viewport.Density == WidgetViewportDensity.Compact ? 12 : achievements.Count;
            foreach (var item in achievements.Take(limit))
            {
                panel.Children.Add(new AchievementCompactItemControl
                {
                    DataContext = item,
                    IconSize = size,
                    Margin = new Thickness(3)
                });
            }

            return WrapScrollable(panel);
        }

        private static Border CreateCard(
            UIElement content,
            Thickness margin,
            Thickness padding)
        {
            var border = new Border
            {
                Child = content,
                Margin = margin,
                Padding = padding,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(0),
                ClipToBounds = true
            };
            border.SetResourceReference(Border.BackgroundProperty, "PlayAch.Brush.Overlay.Tint.08");
            return border;
        }

        private static Border CreateImageFrame(
            Image image,
            Thickness? margin = null)
        {
            var frame = new Border
            {
                Child = image,
                Margin = margin ?? new Thickness(0),
                Padding = new Thickness(2),
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                ClipToBounds = true
            };
            frame.SetResourceReference(Border.BackgroundProperty, "PlayAch.Brush.Overlay.Tint.08");
            frame.SetResourceReference(Border.BorderBrushProperty, "PlayAch.Brush.Border");
            return frame;
        }

        private UIElement WrapScrollable(UIElement content)
        {
            return new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }

        private TextBlock CreateEmptyText(string text = null)
        {
            return CreateText(
                text ?? Localize("LOCPlayAch_Showcase_NoData", "No data yet"),
                12,
                FontWeights.Normal,
                0.62);
        }

        private static string FormatStatisticDisplayValue(ShowcaseStatistic item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            switch (item.Key)
            {
                case "playtime":
                    var hours = Math.Max(0, item.Value) / 3600d;
                    return hours >= 1000
                        ? string.Format(
                            FormattingCulture.Current,
                            Localize("LOCPlayAch_Showcase_ThousandsHours", "{0:N1}k h"),
                            hours / 1000d)
                        : string.Format(
                            FormattingCulture.Current,
                            Localize("LOCPlayAch_Showcase_Hours", "{0:N0} h"),
                            hours);
                case "thirtyDayRate":
                    return string.Format(
                        FormattingCulture.Current,
                        Localize("LOCPlayAch_Showcase_PerDay", "{0:N1}/day"),
                        item.Value);
                case "currentStreak":
                case "longestStreak":
                    var days = (int)Math.Round(item.Value);
                    return string.Format(
                        FormattingCulture.Current,
                        Localize(
                            days == 1
                                ? "LOCPlayAch_Showcase_Day"
                                : "LOCPlayAch_Showcase_Days",
                            days == 1 ? "{0} day" : "{0} days"),
                        days.ToString("N0", FormattingCulture.Current));
                default:
                    return item.DisplayValue;
            }
        }

        private static TextBlock CreateText(
            string text,
            double fontSize,
            FontWeight weight,
            double opacity = 1)
        {
            var block = new TextBlock
            {
                Text = text ?? string.Empty,
                FontSize = fontSize,
                FontWeight = weight,
                Opacity = opacity,
                TextWrapping = TextWrapping.Wrap
            };
            block.SetResourceReference(TextBlock.ForegroundProperty, "PlayAch.Brush.Text");
            return block;
        }

        private static Image CreateImage(string path, double size)
        {
            var image = new Image
            {
                Width = size,
                Height = size,
                Stretch = Stretch.UniformToFill,
                SnapsToDevicePixels = true
            };
            AsyncImage.SetDecodePixel(image, Math.Max(64, (int)Math.Ceiling(size * 2)));
            AsyncImage.SetUri(image, path);

            return image;
        }

        private static Brush TryCreateBrush(string color)
        {
            try
            {
                var brush = new BrushConverter().ConvertFromString(color) as Brush;
                if (brush?.CanFreeze == true)
                {
                    brush.Freeze();
                }

                return brush ?? Brushes.Gray;
            }
            catch
            {
                return Brushes.Gray;
            }
        }

        private static MenuItem CreatePinOrderItem(string header, Func<bool> move)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, __) =>
            {
                if (move?.Invoke() != true)
                {
                    return;
                }

                PlayniteAchievementsPlugin.Instance?.PersistSettingsForUi();
                ShowcaseConfigurationEvents.RaiseChanged();
            };
            return item;
        }

        private static string Localize(string key, string fallback)
        {
            var value = ResourceProvider.GetString(key);
            return string.IsNullOrWhiteSpace(value) ||
                   string.Equals(value, key, StringComparison.Ordinal) ||
                   (value.StartsWith("<!", StringComparison.Ordinal) &&
                    value.EndsWith("!>", StringComparison.Ordinal))
                ? fallback
                : value;
        }

        private static string Humanize(ShowcaseWidgetKind kind)
        {
            switch (kind)
            {
                case ShowcaseWidgetKind.NativePoints:
                    return "Native Points";
                case ShowcaseWidgetKind.PinnedAchievements:
                    return "Pinned Achievements";
                case ShowcaseWidgetKind.FavoriteGames:
                    return "Favorite Games";
                case ShowcaseWidgetKind.IconMosaic:
                    return "Icon Mosaic";
                case ShowcaseWidgetKind.ScreenshotSlideshow:
                    return "Screenshot Slideshow";
                case ShowcaseWidgetKind.Statistics:
                    return "Overall Statistics";
                default:
                    return kind.ToString();
            }
        }

        private static string GetWidgetGlyph(ShowcaseWidgetKind kind)
        {
            switch (kind)
            {
                case ShowcaseWidgetKind.Profile:
                    return "\uE77B";
                case ShowcaseWidgetKind.Scores:
                    return "\uE8E5";
                case ShowcaseWidgetKind.Pie:
                    return "\uE9D2";
                case ShowcaseWidgetKind.Timeline:
                    return "\uE9D9";
                case ShowcaseWidgetKind.Statistics:
                    return "\uE9D5";
                case ShowcaseWidgetKind.NativePoints:
                    return "\uE8C7";
                case ShowcaseWidgetKind.PinnedAchievements:
                    return "\uE7C1";
                case ShowcaseWidgetKind.FavoriteGames:
                    return "\uE734";
                case ShowcaseWidgetKind.IconMosaic:
                    return "\uE80A";
                case ShowcaseWidgetKind.ScreenshotSlideshow:
                    return "\uEB9F";
                default:
                    return "\uE946";
            }
        }
    }
}

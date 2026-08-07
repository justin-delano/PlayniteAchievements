using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;
using PlayniteAchievements.ViewModels.Showcase.Widgets;
using PlayniteAchievements.Views.Controls;
using PlayniteAchievements.Views.Dialogs;
using PlayniteAchievements.Views.Helpers;
using ChartAxis = LiveCharts.Wpf.Axis;
using ChartColumnSeries = LiveCharts.Wpf.ColumnSeries;
using ChartControl = LiveCharts.Wpf.CartesianChart;
using ChartSeparator = LiveCharts.Wpf.Separator;
using static PlayniteAchievements.Views.Showcase.ShowcaseUiText;

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
        private ShowcaseWidgetViewModelBase _bodyViewModel;

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
                : Localize(ShowcaseWidgetCatalog.Get(_projection.Instance.Kind).NameKey);
            GlyphText.Text = GetWidgetGlyph(_projection.Instance.Kind);
            RootBorder.ToolTip = TitleText.Text;
        }

        private void RebuildBody()
        {
            HeaderBorder.Visibility = _viewport.Density == WidgetViewportDensity.Expanded
                ? Visibility.Visible
                : Visibility.Collapsed;
            BodyHost.Margin = _viewport.Density == WidgetViewportDensity.Compact
                ? new Thickness(6)
                : _viewport.Density == WidgetViewportDensity.Expanded
                    ? new Thickness(10)
                    : new Thickness(8);
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
                    BodyHost.Content = UpdateBodyViewModel<StatisticsWidgetViewModel>();
                    break;
                case ShowcaseWidgetKind.NativePoints:
                    BodyHost.Content = _projection.ChartEntries?.Count > 0
                        ? (object)UpdateBodyViewModel<NativePointsWidgetViewModel>()
                        : CreateEmptyText();
                    break;
                case ShowcaseWidgetKind.PinnedAchievements:
                    BodyHost.Content = _projection.Achievements?.Count > 0
                        ? (object)UpdateBodyViewModel<PinnedAchievementsWidgetViewModel>()
                        : CreateEmptyText(Localize("LOCPlayAch_Showcase_NoPinnedAchievements"));
                    break;
                case ShowcaseWidgetKind.FavoriteGames:
                    BodyHost.Content = _projection.Games?.Count > 0
                        ? (object)UpdateBodyViewModel<FavoriteGamesWidgetViewModel>()
                        : CreateEmptyText(Localize("LOCPlayAch_Showcase_NoFavoriteGames"));
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

        // Reuses (or lazily creates) the per-kind body view model for this control and feeds it the
        // current projection and viewport. Implicit templates in ShowcaseWidgetTemplates.xaml render
        // the returned view model. A control instance renders a single widget kind for its lifetime,
        // so the view model type never changes once created.
        private ShowcaseWidgetViewModelBase UpdateBodyViewModel<T>()
            where T : ShowcaseWidgetViewModelBase, new()
        {
            if (!(_bodyViewModel is T typed))
            {
                typed = new T();
                _bodyViewModel = typed;
            }

            typed.Update(_projection, _viewport);
            return typed;
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
                var avatarSize = _viewport.Density == WidgetViewportDensity.Compact ? 42 : 72;
                var avatar = CreateImage(profile.AvatarPath, avatarSize);
                var avatarFrame = CreateImageFrame(avatar);
                avatarFrame.Margin = new Thickness(0, 0, 12, 0);
                avatarFrame.CornerRadius = new CornerRadius((avatarSize + 4) / 2);
                avatarFrame.SetResourceReference(Border.BorderBrushProperty, "PlayAch.Brush.Accent");
                panel.Children.Add(avatarFrame);
            }

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            text.Children.Add(CreateText(
                string.IsNullOrWhiteSpace(profile.DisplayName)
                    ? Localize("LOCPlayAch_Showcase_Profile_DefaultName")
                    : profile.DisplayName,
                18,
                FontWeights.SemiBold));
            if (!string.IsNullOrWhiteSpace(profile.Subtitle) &&
                _viewport.Density != WidgetViewportDensity.Compact)
            {
                text.Children.Add(CreateText(profile.Subtitle, 12, FontWeights.Normal, 0.72));
            }

            if (_viewport.Density == WidgetViewportDensity.Expanded)
            {
                var snapshot = _projection.Snapshot;
                text.Children.Add(CreateText(
                    string.Format(
                        FormattingCulture.Current,
                        Localize("LOCPlayAch_Showcase_ProfileStats"),
                        snapshot.TotalUnlocked,
                        snapshot.GlobalProgressionPercent,
                        snapshot.CompletedGames),
                    11,
                    FontWeights.Normal,
                    0.72));
            }

            if (_viewport.Density == WidgetViewportDensity.Expanded)
            {
                var currentStreak = _projection.Statistics?.FirstOrDefault(item =>
                    string.Equals(item?.Key, "currentStreak", StringComparison.Ordinal));
                var longestStreak = _projection.Statistics?.FirstOrDefault(item =>
                    string.Equals(item?.Key, "longestStreak", StringComparison.Ordinal));
                if (currentStreak != null && longestStreak != null)
                {
                    text.Children.Add(CreateText(
                        string.Format(
                            FormattingCulture.Current,
                            Localize("LOCPlayAch_Showcase_ProfileStreaks"),
                            ShowcaseStatisticFormatter.Format(currentStreak),
                            ShowcaseStatisticFormatter.Format(longestStreak)),
                        11,
                        FontWeights.Normal,
                        0.72));
                }
            }

            panel.Children.Add(text);
            return panel;
        }

        private UIElement BuildScores()
        {
            var snapshot = _projection.Snapshot;
            var mode = ShowcaseWidgetOptions.GetScoreMode(_projection.Instance);
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
                IsFeatured = _viewport.Density != WidgetViewportDensity.Compact,
                Margin = new Thickness(4),
                MinWidth = 0,
                MaxWidth = _viewport.Density == WidgetViewportDensity.Expanded ? 440 : 360,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            control.InfoRequested += (_, __) => ScoreInfoDialogPresenter.Show();
            return control;
        }

        private UIElement BuildPie()
        {
            var snapshot = _projection.Snapshot;
            var mode = ShowcaseWidgetOptions.GetPieMode(_projection.Instance);
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
                        Localize("LOCPlayAch_Trophy_Platinum"),
                        Localize("LOCPlayAch_Trophy_Gold"),
                        Localize("LOCPlayAch_Trophy_Silver"),
                        Localize("LOCPlayAch_Trophy_Bronze"),
                        Localize("LOCPlayAch_Common_Locked"));
                    break;
                default:
                    chart.SetGameData(
                        snapshot.TotalGames,
                        snapshot.CompletedGames,
                        Localize("LOCPlayAch_Completed"),
                        Localize("LOCPlayAch_Showcase_Incomplete"));
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
                    ShowcaseConfigurationCommit.Commit();
                };
                controls.Children.Add(button);
            }

            Grid.SetRow(controls, 1);
            root.Children.Add(controls);
            return root;
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

        private UIElement BuildMosaic(IReadOnlyList<AchievementDisplayItem> achievements)
        {
            if (achievements == null || achievements.Count == 0)
            {
                return CreateEmptyText();
            }

            var panel = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6)
            };
            var size = _viewport.Density == WidgetViewportDensity.Compact
                ? 32
                : _viewport.Density == WidgetViewportDensity.Expanded ? 54 : 42;
            var limit = _viewport.Density == WidgetViewportDensity.Compact ? 12 : achievements.Count;
            var appearance = PlayniteAchievementsPlugin.Instance?.Settings?.Persisted;
            foreach (var item in achievements.Take(limit))
            {
                panel.Children.Add(new AchievementCompactItemControl
                {
                    DataContext = item,
                    IconSize = size,
                    ShowRarityGlow = appearance?.ModernCompactListShowRarityGlow ?? true,
                    AnimateRarityGlows = appearance?.AnimateRarityGlows ?? true,
                    // Match the full achievement-grid glow; the surrounding margin keeps its
                    // larger halo visible without changing the shared compact-list default.
                    UseLargeRarityGlow = true,
                    Margin = new Thickness(6)
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
                text ?? Localize("LOCPlayAch_Showcase_NoData"),
                12,
                FontWeights.Normal,
                0.62);
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

        private static Image CreateImage(string path, double size) =>
            CreateImage(path, size, size, Stretch.UniformToFill);

        private static Image CreateImage(
            string path,
            double width,
            double height,
            Stretch stretch)
        {
            var image = new Image
            {
                Width = width,
                Height = height,
                Stretch = stretch,
                SnapsToDevicePixels = true
            };
            AsyncImage.SetDecodePixel(
                image,
                Math.Max(64, (int)Math.Ceiling(Math.Max(width, height) * 2)));
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

        private static string GetWidgetGlyph(ShowcaseWidgetKind kind) =>
            ShowcaseWidgetCatalog.Get(kind).GlyphKey;
    }
}

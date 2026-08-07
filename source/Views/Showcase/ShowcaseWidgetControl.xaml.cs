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
                    BodyHost.Content = UpdateBodyViewModel<ProfileWidgetViewModel>();
                    break;
                case ShowcaseWidgetKind.Scores:
                    BodyHost.Content = UpdateBodyViewModel<ScoresWidgetViewModel>();
                    break;
                case ShowcaseWidgetKind.Pie:
                    BodyHost.Content = UpdateBodyViewModel<PieWidgetViewModel>();
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
                    BodyHost.Content = _projection.MosaicAchievements?.Count > 0
                        ? (object)UpdateBodyViewModel<IconMosaicWidgetViewModel>()
                        : CreateEmptyText();
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

        private static string GetWidgetGlyph(ShowcaseWidgetKind kind) =>
            ShowcaseWidgetCatalog.Get(kind).GlyphKey;
    }
}

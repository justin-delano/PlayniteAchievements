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
            // The header only appears when the user gave the widget a custom title;
            // widgets are otherwise chrome-free at every density (large StartPage-hosted
            // widgets used to auto-show the kind name at expanded density).
            var custom = _projection?.Instance?.CustomTitle?.Trim();
            if (string.IsNullOrWhiteSpace(custom))
            {
                TitleText.Text = string.Empty;
                GlyphText.Text = string.Empty;
                HeaderBorder.Visibility = Visibility.Collapsed;
                return;
            }

            TitleText.Text = custom;
            GlyphText.Text = GetWidgetGlyph(_projection.Instance.Kind);
            HeaderBorder.Visibility = Visibility.Visible;
        }

        private void RebuildBody()
        {
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
                    BodyHost.Content = UpdateBodyViewModel<TimelineWidgetViewModel>();
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
                    BodyHost.Content = _projection.AchievementRows?.Count > 0
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
                case ShowcaseWidgetKind.RecentAchievements:
                    BodyHost.Content = _projection.AchievementRows?.Count > 0
                        ? (object)UpdateBodyViewModel<RecentAchievementsWidgetViewModel>()
                        : CreateEmptyText();
                    break;
                case ShowcaseWidgetKind.GameSummaries:
                    BodyHost.Content = _projection.Games?.Count > 0
                        ? (object)UpdateBodyViewModel<GameSummariesWidgetViewModel>()
                        : CreateEmptyText();
                    break;
                case ShowcaseWidgetKind.GameMosaic:
                    BodyHost.Content = _projection.Games?.Count > 0
                        ? (object)UpdateBodyViewModel<GameMosaicWidgetViewModel>()
                        : CreateEmptyText();
                    break;
                case ShowcaseWidgetKind.ActivityCalendar:
                    BodyHost.Content = UpdateBodyViewModel<ActivityCalendarWidgetViewModel>();
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

        private static string GetWidgetGlyph(ShowcaseWidgetKind kind) =>
            ShowcaseWidgetCatalog.Get(kind).GlyphKey;
    }
}

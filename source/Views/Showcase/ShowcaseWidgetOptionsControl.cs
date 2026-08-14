using System;
using System.Windows;
using System.Windows.Controls;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Showcase;
using static PlayniteAchievements.Views.Showcase.ShowcaseUiText;

namespace PlayniteAchievements.Views.Showcase
{
    /// <summary>
    /// Shared option editor for Showcase and StartPage widget instances.
    /// Host-specific save and cancel behavior stays with the owning surface.
    /// </summary>
    internal sealed class ShowcaseWidgetOptionsControl : UserControl
    {
        private readonly ShowcaseWidgetInstanceSettings _settings;
        private readonly Action _persist;
        private readonly bool _publishChanges;

        public ShowcaseWidgetOptionsControl(
            ShowcaseWidgetInstanceSettings settings,
            Action persist = null,
            bool publishChanges = true,
            Thickness? margin = null,
            bool loadStyles = true)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _persist = persist;
            _publishChanges = publishChanges;
            if (loadStyles)
            {
                Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/PlayniteAchievements;component/Resources/PlayAchImplicitControlStyles.xaml",
                        UriKind.Absolute)
                });
            }
            Content = Build(margin ?? new Thickness(16));
            FormattingCulture.Apply(this);
        }

        public static bool HasOptions(ShowcaseWidgetKind kind)
        {
            switch (kind)
            {
                case ShowcaseWidgetKind.Scores:
                case ShowcaseWidgetKind.Pie:
                case ShowcaseWidgetKind.Timeline:
                case ShowcaseWidgetKind.NativePoints:
                case ShowcaseWidgetKind.FavoriteGames:
                case ShowcaseWidgetKind.IconMosaic:
                case ShowcaseWidgetKind.ScreenshotSlideshow:
                case ShowcaseWidgetKind.RecentAchievements:
                case ShowcaseWidgetKind.GameSummaries:
                case ShowcaseWidgetKind.GameMosaic:
                case ShowcaseWidgetKind.ActivityCalendar:
                    return true;
                default:
                    return false;
            }
        }

        private UIElement Build(Thickness margin)
        {
            var panel = new StackPanel { Margin = margin };
            switch (_settings.Kind)
            {
                case ShowcaseWidgetKind.Scores:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_ScoreCards"),
                        new[] { ShowcaseScoreMode.Dual, ShowcaseScoreMode.Collection, ShowcaseScoreMode.Prestige },
                        ShowcaseWidgetOptions.GetScoreMode(_settings),
                        value => ShowcaseWidgetOptions.SetScoreMode(_settings, value),
                        ScoreModeName);
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Range"),
                        new[]
                        {
                            TimelineRange.OneMonth,
                            TimelineRange.ThreeMonths,
                            TimelineRange.OneYear,
                            TimelineRange.All
                        },
                        ShowcaseTimelineOptions.GetRange(_settings),
                        value => ShowcaseTimelineOptions.SetRange(_settings, value),
                        TimelineRangeName);
                    break;
                case ShowcaseWidgetKind.Pie:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Mode"),
                        new[]
                        {
                            ShowcasePieMode.CompletedGames,
                            ShowcasePieMode.Provider,
                            ShowcasePieMode.Rarity,
                            ShowcasePieMode.Trophy
                        },
                        ShowcaseWidgetOptions.GetPieMode(_settings),
                        value => ShowcaseWidgetOptions.SetPieMode(_settings, value),
                        PieModeName);
                    break;
                case ShowcaseWidgetKind.Timeline:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Range"),
                        new[]
                        {
                            TimelineRange.OneMonth,
                            TimelineRange.ThreeMonths,
                            TimelineRange.OneYear,
                            TimelineRange.All
                        },
                        ShowcaseTimelineOptions.GetRange(_settings),
                        value => ShowcaseTimelineOptions.SetRange(_settings, value),
                        TimelineRangeName);
                    break;
                case ShowcaseWidgetKind.NativePoints:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_GroupBy"),
                        new[] { ShowcasePointsGrouping.Provider, ShowcasePointsGrouping.Game },
                        ShowcaseWidgetOptions.GetPointsGrouping(_settings),
                        value => ShowcaseWidgetOptions.SetPointsGrouping(_settings, value),
                        PointsGroupingName);
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_TopN"),
                        new[] { 5, 8, 10, 15, 25 },
                        ShowcaseWidgetOptions.GetTopN(_settings),
                        value => ShowcaseWidgetOptions.SetTopN(_settings, value),
                        value => value.ToString("N0", FormattingCulture.Current));
                    break;
                case ShowcaseWidgetKind.FavoriteGames:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Source"),
                        new[]
                        {
                            ShowcaseFavoriteGameSource.ShowcasePins,
                            ShowcaseFavoriteGameSource.PlayniteFavorites
                        },
                        ShowcaseWidgetOptions.GetFavoriteSource(_settings),
                        value => ShowcaseWidgetOptions.SetFavoriteSource(_settings, value),
                        FavoriteSourceName);
                    break;
                case ShowcaseWidgetKind.IconMosaic:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Source"),
                        new[] { ShowcaseMosaicSource.Recent, ShowcaseMosaicSource.Rarest, ShowcaseMosaicSource.Pinned },
                        ShowcaseWidgetOptions.GetMosaicSource(_settings),
                        value => ShowcaseWidgetOptions.SetMosaicSource(_settings, value),
                        MosaicSourceName);
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_ItemCount"),
                        new[] { 12, 24, 36, 48, 64 },
                        ShowcaseWidgetOptions.GetMosaicCount(_settings),
                        value => ShowcaseWidgetOptions.SetMosaicCount(_settings, value),
                        value => value.ToString("N0", FormattingCulture.Current));
                    break;
                case ShowcaseWidgetKind.ScreenshotSlideshow:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Variant"),
                        new[]
                        {
                            ShowcaseScreenshotVariant.All,
                            ShowcaseScreenshotVariant.Clean,
                            ShowcaseScreenshotVariant.Notification,
                            ShowcaseScreenshotVariant.Framed
                        },
                        ShowcaseWidgetOptions.GetScreenshotVariant(_settings),
                        value => ShowcaseWidgetOptions.SetScreenshotVariant(_settings, value),
                        ScreenshotVariantName);
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Interval"),
                        new[] { 3, 5, 8, 15, 30 },
                        ShowcaseWidgetOptions.GetSlideshowIntervalSeconds(_settings),
                        value => ShowcaseWidgetOptions.SetSlideshowIntervalSeconds(_settings, value),
                        value => string.Format(
                            FormattingCulture.Current,
                            Localize("LOCPlayAch_Showcase_Seconds"),
                            value));
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_FitMode"),
                        new[] { ShowcaseImageFitMode.Fit, ShowcaseImageFitMode.Fill },
                        ShowcaseWidgetOptions.GetImageFitMode(_settings),
                        value => ShowcaseWidgetOptions.SetImageFitMode(_settings, value),
                        FitModeName);
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Shuffle"),
                        new[] { true, false },
                        ShowcaseWidgetOptions.GetShuffle(_settings),
                        value => ShowcaseWidgetOptions.SetShuffle(_settings, value),
                        value => value
                            ? Localize("LOCPlayAch_Settings_Override_On")
                            : Localize("LOCPlayAch_Settings_Override_Off"));
                    break;
                case ShowcaseWidgetKind.RecentAchievements:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_ItemCount"),
                        new[] { 5, 10, 15, 25, 50, 100 },
                        ShowcaseWidgetOptions.GetRecentCount(_settings),
                        value => ShowcaseWidgetOptions.SetRecentCount(_settings, value),
                        value => value.ToString("N0", FormattingCulture.Current));
                    break;
                case ShowcaseWidgetKind.GameSummaries:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Settings_SortBy"),
                        new[]
                        {
                            ShowcaseGameListSort.LastUnlock,
                            ShowcaseGameListSort.Completion,
                            ShowcaseGameListSort.Name,
                            ShowcaseGameListSort.Playtime
                        },
                        ShowcaseWidgetOptions.GetGameListSort(_settings),
                        value => ShowcaseWidgetOptions.SetGameListSort(_settings, value),
                        GameListSortName);
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_ItemCount"),
                        new[] { 10, 25, 50, 100, 200 },
                        ShowcaseWidgetOptions.GetGameListCount(_settings),
                        value => ShowcaseWidgetOptions.SetGameListCount(_settings, value),
                        value => value.ToString("N0", FormattingCulture.Current));
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_HideCompleted"),
                        new[] { false, true },
                        ShowcaseWidgetOptions.GetHideCompleted(_settings),
                        value => ShowcaseWidgetOptions.SetHideCompleted(_settings, value),
                        value => value
                            ? Localize("LOCPlayAch_Settings_Override_On")
                            : Localize("LOCPlayAch_Settings_Override_Off"));
                    break;
                case ShowcaseWidgetKind.ActivityCalendar:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Range"),
                        new[]
                        {
                            TimelineRange.OneMonth,
                            TimelineRange.ThreeMonths,
                            TimelineRange.OneYear,
                            TimelineRange.All
                        },
                        ShowcaseTimelineOptions.GetRange(_settings),
                        value => ShowcaseTimelineOptions.SetRange(_settings, value),
                        TimelineRangeName);
                    break;
                case ShowcaseWidgetKind.GameMosaic:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Source"),
                        new[]
                        {
                            ShowcaseGameMosaicSource.Completed,
                            ShowcaseGameMosaicSource.All,
                            ShowcaseGameMosaicSource.Pinned,
                            ShowcaseGameMosaicSource.PlayniteFavorites
                        },
                        ShowcaseWidgetOptions.GetGameMosaicSource(_settings),
                        value => ShowcaseWidgetOptions.SetGameMosaicSource(_settings, value),
                        GameMosaicSourceName);
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_ItemCount"),
                        new[] { 12, 24, 36, 48, 64 },
                        ShowcaseWidgetOptions.GetGameMosaicCount(_settings),
                        value => ShowcaseWidgetOptions.SetGameMosaicCount(_settings, value),
                        value => value.ToString("N0", FormattingCulture.Current));
                    break;
            }

            return panel;
        }

        private void AddChoice<T>(
            Panel panel,
            string label,
            T[] values,
            T selected,
            Action<T> apply,
            Func<T, string> display)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            var labelBlock = new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 10, 0),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "PlayAch.Brush.Text");
            row.Children.Add(labelBlock);

            var combo = new ComboBox { MinHeight = 30 };
            Choice<T> selectedChoice = null;
            foreach (var value in values)
            {
                var choice = new Choice<T> { Value = value, Label = display(value) };
                combo.Items.Add(choice);
                if (Equals(value, selected))
                {
                    selectedChoice = choice;
                }
            }

            if (selectedChoice == null)
            {
                selectedChoice = new Choice<T>
                {
                    Value = selected,
                    Label = display(selected)
                };
                combo.Items.Insert(0, selectedChoice);
            }

            combo.DisplayMemberPath = nameof(Choice<T>.Label);
            combo.SelectedItem = selectedChoice;
            combo.SelectionChanged += (_, __) =>
            {
                if (!(combo.SelectedItem is Choice<T> choice))
                {
                    return;
                }

                apply(choice.Value);
                _persist?.Invoke();
                if (_publishChanges)
                {
                    ShowcaseConfigurationEvents.RaiseChanged();
                }
            };
            Grid.SetColumn(combo, 1);
            row.Children.Add(combo);
            panel.Children.Add(row);
        }

        private sealed class Choice<T>
        {
            public T Value { get; set; }

            public string Label { get; set; }
        }
    }
}

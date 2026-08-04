using System;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Showcase;

namespace PlayniteAchievements.Views.StartPage
{
    public sealed class StartPageShowcaseWidgetSettingsControl : UserControl
    {
        private readonly ShowcaseWidgetInstanceSettings _settings;
        private readonly Action _persist;

        public StartPageShowcaseWidgetSettingsControl(
            ShowcaseWidgetInstanceSettings settings,
            Action persist)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _persist = persist;
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/PlayniteAchievements;component/Resources/PlayAchImplicitControlStyles.xaml",
                    UriKind.Absolute)
            });
            Content = Build();
            FormattingCulture.Apply(this);
        }

        private UIElement Build()
        {
            var panel = new StackPanel { Margin = new Thickness(16) };
            switch (_settings.Kind)
            {
                case ShowcaseWidgetKind.Timeline:
                    AddEnumChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Range", "Range"),
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
                    AddEnumChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_GroupBy", "Group by"),
                        new[]
                        {
                            ShowcasePointsGrouping.Provider,
                            ShowcasePointsGrouping.Game
                        },
                        _settings.GetOption("Grouping", ShowcasePointsGrouping.Provider),
                        value => _settings.SetOption("Grouping", value),
                        value => Localize(
                            $"LOCPlayAch_Showcase_PointsGrouping_{value}",
                            value.ToString()));
                    AddEnumChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_TopN", "Top entries"),
                        new[] { 5, 8, 10, 15, 25 },
                        _settings.GetOption("TopN", 8),
                        value => _settings.SetOption("TopN", value),
                        value => value.ToString("N0", FormattingCulture.Current));
                    break;
                case ShowcaseWidgetKind.FavoriteGames:
                    AddEnumChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Source", "Source"),
                        new[]
                        {
                            ShowcaseFavoriteGameSource.ShowcasePins,
                            ShowcaseFavoriteGameSource.PlayniteFavorites
                        },
                        _settings.GetOption(
                            "Source",
                            ShowcaseFavoriteGameSource.ShowcasePins),
                        value => _settings.SetOption("Source", value),
                        value => value == ShowcaseFavoriteGameSource.PlayniteFavorites
                            ? Localize(
                                "LOCPlayAch_Showcase_FavoriteSource_PlayniteFavorites",
                                "Playnite favorites")
                            : Localize(
                                "LOCPlayAch_Showcase_FavoriteSource_ShowcasePins",
                                "Showcase pins"));
                    break;
                case ShowcaseWidgetKind.IconMosaic:
                    AddEnumChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Source", "Source"),
                        new[]
                        {
                            ShowcaseMosaicSource.Recent,
                            ShowcaseMosaicSource.Rarest,
                            ShowcaseMosaicSource.Pinned
                        },
                        _settings.GetOption("Source", ShowcaseMosaicSource.Recent),
                        value => _settings.SetOption("Source", value),
                        value => Localize(
                            $"LOCPlayAch_Showcase_MosaicSource_{value}",
                            value.ToString()));
                    AddEnumChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_ItemCount", "Item count"),
                        new[] { 12, 24, 36, 48, 64 },
                        _settings.GetOption("Count", 24),
                        value => _settings.SetOption("Count", value),
                        value => value.ToString("N0", FormattingCulture.Current));
                    break;
                case ShowcaseWidgetKind.ScreenshotSlideshow:
                    AddEnumChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Variant", "Variant"),
                        new[]
                        {
                            ShowcaseScreenshotVariant.All,
                            ShowcaseScreenshotVariant.Clean,
                            ShowcaseScreenshotVariant.Notification,
                            ShowcaseScreenshotVariant.Framed
                        },
                        _settings.GetOption("Variant", ShowcaseScreenshotVariant.All),
                        value => _settings.SetOption("Variant", value),
                        value => Localize(
                            $"LOCPlayAch_Showcase_ScreenshotVariant_{value}",
                            value.ToString()));
                    AddEnumChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Interval", "Interval"),
                        new[] { 3, 5, 8, 15, 30 },
                        _settings.GetOption("IntervalSeconds", 8),
                        value => _settings.SetOption("IntervalSeconds", value),
                        value => string.Format(
                            FormattingCulture.Current,
                            Localize("LOCPlayAch_Showcase_Seconds", "{0} seconds"),
                            value));
                    AddEnumChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_FitMode", "Fit"),
                        new[]
                        {
                            ShowcaseImageFitMode.Fit,
                            ShowcaseImageFitMode.Fill
                        },
                        _settings.GetOption("FitMode", ShowcaseImageFitMode.Fill),
                        value => _settings.SetOption("FitMode", value),
                        value => Localize(
                            $"LOCPlayAch_Showcase_ImageFit_{value}",
                            value.ToString()));
                    AddEnumChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Shuffle", "Shuffle"),
                        new[] { true, false },
                        _settings.GetOption("Shuffle", true),
                        value => _settings.SetOption("Shuffle", value),
                        value => value
                            ? Localize("LOCPlayAch_Settings_Override_On", "On")
                            : Localize("LOCPlayAch_Settings_Override_Off", "Off"));
                    break;
            }

            return panel;
        }

        private void AddEnumChoice<T>(
            Panel panel,
            string label,
            T[] values,
            T selected,
            Action<T> apply,
            Func<T, string> display)
        {
            var labelBlock = new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 10, 0, 4),
                FontWeight = FontWeights.SemiBold
            };
            labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "PlayAch.Brush.Text");
            panel.Children.Add(labelBlock);
            var combo = new ComboBox { MinHeight = 30 };
            foreach (var value in values)
            {
                combo.Items.Add(new Choice<T> { Value = value, Label = display(value) });
            }

            combo.DisplayMemberPath = nameof(Choice<T>.Label);
            combo.SelectedItem = FindChoice(combo, selected);
            combo.SelectionChanged += (_, __) =>
            {
                if (!(combo.SelectedItem is Choice<T> choice))
                {
                    return;
                }

                apply(choice.Value);
                _persist?.Invoke();
                ShowcaseConfigurationEvents.RaiseChanged();
            };
            panel.Children.Add(combo);
        }

        private static object FindChoice<T>(ItemsControl combo, T value)
        {
            foreach (var item in combo.Items)
            {
                if (item is Choice<T> choice &&
                    Equals(choice.Value, value))
                {
                    return choice;
                }
            }

            return combo.Items.Count > 0 ? combo.Items[0] : null;
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

        private sealed class Choice<T>
        {
            public T Value { get; set; }

            public string Label { get; set; }
        }
    }
}

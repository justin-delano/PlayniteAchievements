using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.Views.Settings.Controls;
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
        private System.ComponentModel.INotifyPropertyChanged _gridOptionsRecord;
        private System.Windows.Threading.DispatcherTimer _gridOptionsPersistTimer;

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
                case ShowcaseWidgetKind.PinnedAchievements:
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
                case ShowcaseWidgetKind.PinnedAchievements:
                    AddPinCollectionChoice(panel, achievementCollection: true);
                    break;
                case ShowcaseWidgetKind.Scores:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_ScoreCards"),
                        new[] { ShowcaseScoreMode.Dual, ShowcaseScoreMode.Collection, ShowcaseScoreMode.Prestige },
                        ShowcaseWidgetOptions.GetScoreMode(_settings),
                        value => ShowcaseWidgetOptions.SetScoreMode(_settings, value),
                        ScoreModeName);
                    AddRangeChoice(panel);

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
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Settings_ShowOverviewPiePercentages"),
                        new[] { true, false },
                        ShowcaseWidgetOptions.GetPieShowCenterPercentage(_settings),
                        value => ShowcaseWidgetOptions.SetPieShowCenterPercentage(_settings, value),
                        OnOffLabel);
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_ShowLegend"),
                        new[] { true, false },
                        ShowcaseWidgetOptions.GetPieShowLegend(_settings),
                        value => ShowcaseWidgetOptions.SetPieShowLegend(_settings, value),
                        OnOffLabel);
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Settings_OverviewPieSmallSliceMode"),
                        new[]
                        {
                            OverviewPieSmallSliceMode.Round,
                            OverviewPieSmallSliceMode.Exact,
                            OverviewPieSmallSliceMode.Hide
                        },
                        ShowcaseWidgetOptions.GetPieSmallSliceMode(_settings),
                        value => ShowcaseWidgetOptions.SetPieSmallSliceMode(_settings, value),
                        SmallSliceModeName);
                    break;
                case ShowcaseWidgetKind.Timeline:
                    AddRangeChoice(panel);

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
                        CountLabel);
                    break;
                case ShowcaseWidgetKind.FavoriteGames:
                    FrameworkElement favoriteCollectionRow = null;
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Source"),
                        new[]
                        {
                            ShowcaseFavoriteGameSource.ShowcasePins,
                            ShowcaseFavoriteGameSource.PlayniteFavorites
                        },
                        ShowcaseWidgetOptions.GetFavoriteSource(_settings),
                        value =>
                        {
                            ShowcaseWidgetOptions.SetFavoriteSource(_settings, value);
                            if (favoriteCollectionRow != null)
                            {
                                favoriteCollectionRow.Visibility = value ==
                                    ShowcaseFavoriteGameSource.ShowcasePins
                                    ? Visibility.Visible
                                    : Visibility.Collapsed;
                            }
                        },
                        FavoriteSourceName);
                    favoriteCollectionRow = AddPinCollectionChoice(panel, achievementCollection: false);
                    favoriteCollectionRow.Visibility = ShowcaseWidgetOptions.GetFavoriteSource(_settings) ==
                        ShowcaseFavoriteGameSource.ShowcasePins
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    break;
                case ShowcaseWidgetKind.IconMosaic:
                    FrameworkElement achievementMosaicCollectionRow = null;
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Source"),
                        new[] { ShowcaseMosaicSource.Recent, ShowcaseMosaicSource.Rarest, ShowcaseMosaicSource.Pinned },
                        ShowcaseWidgetOptions.GetMosaicSource(_settings),
                        value =>
                        {
                            ShowcaseWidgetOptions.SetMosaicSource(_settings, value);
                            if (achievementMosaicCollectionRow != null)
                            {
                                achievementMosaicCollectionRow.Visibility = value == ShowcaseMosaicSource.Pinned
                                    ? Visibility.Visible
                                    : Visibility.Collapsed;
                            }
                        },
                        MosaicSourceName);
                    achievementMosaicCollectionRow = AddPinCollectionChoice(panel, achievementCollection: true);
                    achievementMosaicCollectionRow.Visibility = ShowcaseWidgetOptions.GetMosaicSource(_settings) ==
                        ShowcaseMosaicSource.Pinned
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_ItemCount"),
                        new[] { 12, 24, 36, 48, 64 },
                        ShowcaseWidgetOptions.GetMosaicCount(_settings),
                        value => ShowcaseWidgetOptions.SetMosaicCount(_settings, value),
                        CountLabel);
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Settings_ToastShowRarityGlow"),
                        new[] { true, false },
                        ShowcaseWidgetOptions.GetMosaicShowRarityGlow(_settings),
                        value => ShowcaseWidgetOptions.SetMosaicShowRarityGlow(_settings, value),
                        OnOffLabel);
                    break;
                case ShowcaseWidgetKind.ScreenshotSlideshow:
                    FrameworkElement slideshowGameCollectionRow = null;
                    FrameworkElement slideshowAchievementCollectionRow = null;
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Source"),
                        new[]
                        {
                            ShowcaseSlideshowSource.All,
                            ShowcaseSlideshowSource.GameCollection,
                            ShowcaseSlideshowSource.AchievementCollection
                        },
                        ShowcaseWidgetOptions.GetSlideshowSource(_settings),
                        value =>
                        {
                            ShowcaseWidgetOptions.SetSlideshowSource(_settings, value);
                            if (slideshowGameCollectionRow != null)
                            {
                                slideshowGameCollectionRow.Visibility =
                                    value == ShowcaseSlideshowSource.GameCollection
                                        ? Visibility.Visible
                                        : Visibility.Collapsed;
                            }

                            if (slideshowAchievementCollectionRow != null)
                            {
                                slideshowAchievementCollectionRow.Visibility =
                                    value == ShowcaseSlideshowSource.AchievementCollection
                                        ? Visibility.Visible
                                        : Visibility.Collapsed;
                            }
                        },
                        SlideshowSourceName);
                    slideshowGameCollectionRow = AddPinCollectionChoice(panel, achievementCollection: false);
                    slideshowGameCollectionRow.Visibility =
                        ShowcaseWidgetOptions.GetSlideshowSource(_settings) ==
                        ShowcaseSlideshowSource.GameCollection
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                    slideshowAchievementCollectionRow = AddPinCollectionChoice(panel, achievementCollection: true);
                    slideshowAchievementCollectionRow.Visibility =
                        ShowcaseWidgetOptions.GetSlideshowSource(_settings) ==
                        ShowcaseSlideshowSource.AchievementCollection
                            ? Visibility.Visible
                            : Visibility.Collapsed;
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
                        OnOffLabel);
                    break;
                case ShowcaseWidgetKind.GameSummaries:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Filter_ActivitySelectorPlaceholder"),
                        new[]
                        {
                            GameActivityScope.All,
                            GameActivityScope.Played,
                            GameActivityScope.Unplayed
                        },
                        ShowcaseWidgetOptions.GetGameActivityScope(_settings),
                        value => ShowcaseWidgetOptions.SetGameActivityScope(_settings, value),
                        ActivityScopeName);
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_HideCompleted"),
                        new[] { false, true },
                        ShowcaseWidgetOptions.GetHideCompleted(_settings),
                        value => ShowcaseWidgetOptions.SetHideCompleted(_settings, value),
                        OnOffLabel);
                    break;
                case ShowcaseWidgetKind.ActivityCalendar:
                    AddRangeChoice(panel);

                    break;
                case ShowcaseWidgetKind.GameMosaic:
                    FrameworkElement gameMosaicCollectionRow = null;
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
                        value =>
                        {
                            ShowcaseWidgetOptions.SetGameMosaicSource(_settings, value);
                            if (gameMosaicCollectionRow != null)
                            {
                                gameMosaicCollectionRow.Visibility = value == ShowcaseGameMosaicSource.Pinned
                                    ? Visibility.Visible
                                    : Visibility.Collapsed;
                            }
                        },
                        GameMosaicSourceName);
                    gameMosaicCollectionRow = AddPinCollectionChoice(panel, achievementCollection: false);
                    gameMosaicCollectionRow.Visibility = ShowcaseWidgetOptions.GetGameMosaicSource(_settings) ==
                        ShowcaseGameMosaicSource.Pinned
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_ItemCount"),
                        new[] { 12, 24, 36, 48, 64 },
                        ShowcaseWidgetOptions.GetGameMosaicCount(_settings),
                        value => ShowcaseWidgetOptions.SetGameMosaicCount(_settings, value),
                        CountLabel);
                    break;
            }

            AppendGridOptionsEditor(panel);
            return panel;
        }

        /// <summary>
        /// Appends the shared grid display-options editor for the grid widget kinds. The editor
        /// binds the widget's LIVE catalog record (not the dialog's widget clone), so grid
        /// display edits are instant-apply, independent of the host's persist callback and of
        /// the dialog's Save/Cancel, which govern only the title and the widget's own option
        /// bag. Live grids react to the record directly (bindings plus the grid view models'
        /// record subscriptions), so edits only need persisting - debounced, because a full
        /// settings write per checkbox toggle makes the editor visibly laggy.
        /// </summary>
        private void AppendGridOptionsEditor(Panel panel)
        {
            var catalog = PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.GridOptions;
            var surfaceKey = ShowcaseGridSurfaces.ResolveWidgetSurface(_settings.Kind, _settings.InstanceId);
            if (catalog == null || surfaceKey == null)
            {
                return;
            }

            var editor = new GridOptionsEditor
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            object options;
            if (ShowcaseGridSurfaces.IsAchievementSurface(surfaceKey))
            {
                // The sort combo's Default (None) keeps the projection order: pin order for
                // pinned grids, unlock recency for recent grids.
                options = catalog.GetAchievement(surfaceKey);
            }
            else
            {
                options = catalog.GetGameSummaries(surfaceKey);
                // Pinned games additionally offer the order-preserving PinOrder choice, since
                // their source order is user-controlled pin order.
                editor.ShowGameSortPinOrderChoice = _settings.Kind == ShowcaseWidgetKind.FavoriteGames;
            }

            editor.Options = options;
            _gridOptionsRecord = options as System.ComponentModel.INotifyPropertyChanged;
            if (_gridOptionsRecord != null)
            {
                Loaded += OnLoadedAttachGridOptions;
                Unloaded += OnUnloadedDetachGridOptions;
            }

            panel.Children.Add(editor);
        }

        private void OnLoadedAttachGridOptions(object sender, RoutedEventArgs e)
        {
            _gridOptionsRecord.PropertyChanged -= OnGridOptionsRecordChanged;
            _gridOptionsRecord.PropertyChanged += OnGridOptionsRecordChanged;
        }

        private void OnUnloadedDetachGridOptions(object sender, RoutedEventArgs e)
        {
            _gridOptionsRecord.PropertyChanged -= OnGridOptionsRecordChanged;
            FlushPendingGridOptionsPersist();
        }

        private void OnGridOptionsRecordChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_gridOptionsPersistTimer == null)
            {
                _gridOptionsPersistTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(600)
                };
                _gridOptionsPersistTimer.Tick += (_, __) => FlushPendingGridOptionsPersist();
            }

            // Restart the window on every edit so a burst of toggles produces one write.
            _gridOptionsPersistTimer.Stop();
            _gridOptionsPersistTimer.Start();
        }

        private void FlushPendingGridOptionsPersist()
        {
            if (_gridOptionsPersistTimer == null || !_gridOptionsPersistTimer.IsEnabled)
            {
                return;
            }

            _gridOptionsPersistTimer.Stop();
            PlayniteAchievementsPlugin.Instance?.PersistSettingsForUi();
        }

        private static readonly TimelineRange[] RangeChoices =
        {
            TimelineRange.OneMonth,
            TimelineRange.ThreeMonths,
            TimelineRange.OneYear,
            TimelineRange.All
        };

        /// <summary>The shared time-range picker used by every range-windowed widget.</summary>
        private void AddRangeChoice(Panel panel)
        {
            AddChoice(
                panel,
                Localize("LOCPlayAch_Showcase_Range"),
                RangeChoices,
                ShowcaseTimelineOptions.GetRange(_settings),
                value => ShowcaseTimelineOptions.SetRange(_settings, value),
                TimelineRangeName);
        }

        private FrameworkElement AddPinCollectionChoice(Panel panel, bool achievementCollection)
        {
            var showcase = PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.Showcase;
            if (achievementCollection)
            {
                var selected = ShowcasePinService.ResolveAchievementCollection(
                    showcase,
                    ShowcaseWidgetOptions.GetPinCollectionId(_settings));
                var collections = (showcase?.AchievementPinCollections ??
                    new System.Collections.Generic.List<PinnedAchievementCollection>())
                    .Where(collection => collection != null)
                    .ToArray();
                return AddChoice(
                    panel,
                    Localize("LOCPlayAch_Showcase_PinCollection"),
                    collections,
                    selected,
                    value => ShowcaseWidgetOptions.SetPinCollectionId(_settings, value?.CollectionId),
                    value => value?.Name ?? string.Empty);
            }

            var selectedGameCollection = ShowcasePinService.ResolveGameCollection(
                showcase,
                ShowcaseWidgetOptions.GetPinCollectionId(_settings));
            var gameCollections = (showcase?.GamePinCollections ??
                new System.Collections.Generic.List<PinnedGameCollection>())
                .Where(collection => collection != null)
                .ToArray();
            return AddChoice(
                panel,
                Localize("LOCPlayAch_Showcase_PinCollection"),
                gameCollections,
                selectedGameCollection,
                value => ShowcaseWidgetOptions.SetPinCollectionId(_settings, value?.CollectionId),
                value => value?.Name ?? string.Empty);
        }

        private static string CountLabel(int value) => value.ToString("N0", FormattingCulture.Current);

        private static string OnOffLabel(bool value) => value
            ? Localize("LOCPlayAch_Settings_Override_On")
            : Localize("LOCPlayAch_Settings_Override_Off");

        private static string ActivityScopeName(GameActivityScope value)
        {
            switch (value)
            {
                case GameActivityScope.Played:
                    return Localize("LOCPlayAch_Filter_Played");
                case GameActivityScope.Unplayed:
                    return Localize("LOCPlayAch_Filter_Unplayed");
                default:
                    return Localize("LOCPlayAch_Common_All");
            }
        }

        private static string SmallSliceModeName(OverviewPieSmallSliceMode value)
        {
            switch (value)
            {
                case OverviewPieSmallSliceMode.Exact:
                    return Localize("LOCPlayAch_Settings_OverviewPieSmallSliceMode_Exact");
                case OverviewPieSmallSliceMode.Hide:
                    return Localize("LOCPlayAch_Common_Hide");
                default:
                    return Localize("LOCPlayAch_Settings_OverviewPieSmallSliceMode_Round");
            }
        }

        private Grid AddChoice<T>(
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
            return row;
        }

        private sealed class Choice<T>
        {
            public T Value { get; set; }

            public string Label { get; set; }
        }
    }
}

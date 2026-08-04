using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.Showcase
{
    public sealed class ShowcaseWidgetSettingsDialog : UserControl
    {
        private const string ImagePatterns = "*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp";

        private readonly ShowcaseWidgetInstanceSettings _sourceWidget;
        private readonly ShowcaseWidgetInstanceSettings _workingWidget;
        private readonly ShowcaseSettings _layout;
        private readonly ShowcaseProfileSettings _workingProfile;
        private TextBox _titleBox;
        private TextBox _profileNameBox;
        private TextBox _profileSubtitleBox;
        private TextBox _avatarBox;
        private TextBox _backgroundBox;

        private ShowcaseWidgetSettingsDialog(
            ShowcaseWidgetInstanceSettings widget,
            ShowcaseSettings layout)
        {
            _sourceWidget = widget ?? throw new ArgumentNullException(nameof(widget));
            _workingWidget = widget.Clone();
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _workingProfile = (layout.Profile ?? new ShowcaseProfileSettings()).Clone();
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/PlayniteAchievements;component/Resources/PlayAchImplicitControlStyles.xaml",
                    UriKind.Absolute)
            });
            Width = 430;
            Height = GetEditorHeight(widget.Kind);
            Content = BuildContent();
            FormattingCulture.Apply(this);
        }

        public bool Saved { get; private set; }

        public static bool Show(
            ShowcaseWidgetInstanceSettings widget,
            ShowcaseSettings layout)
        {
            if (widget == null || layout == null)
            {
                return false;
            }

            var editor = new ShowcaseWidgetSettingsDialog(widget, layout);
            var title = string.Format(
                FormattingCulture.Current,
                Localize("LOCPlayAch_Showcase_WidgetSettingsTitle", "{0} settings"),
                GetWidgetName(widget.Kind));
            var height = GetEditorHeight(widget.Kind);
            var window = PlayniteUiProvider.CreateExtensionWindow(
                title,
                editor,
                new WindowOptions
                {
                    Width = 430,
                    Height = height + 25,
                    CanBeResizable = false,
                    ShowCloseButton = true,
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false
                });
            window.ShowDialog();
            return editor.Saved;
        }

        private UIElement BuildContent()
        {
            var root = new Grid
            {
                Margin = new Thickness(16),
                Background = Brushes.Transparent
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var panel = new StackPanel();
            scroll.Content = panel;
            root.Children.Add(scroll);

            panel.Children.Add(CreateHeading(GetWidgetName(_workingWidget.Kind)));
            _titleBox = AddTextBox(
                panel,
                Localize("LOCPlayAch_Showcase_CustomTitle", "Custom title"),
                _workingWidget.CustomTitle,
                Localize("LOCPlayAch_Showcase_CustomTitleHint", "Leave blank to use the widget name."));

            switch (_workingWidget.Kind)
            {
                case ShowcaseWidgetKind.Profile:
                    BuildProfileSettings(panel);
                    break;
                case ShowcaseWidgetKind.Scores:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Mode", "Mode"),
                        new[] { ShowcaseScoreMode.Dual, ShowcaseScoreMode.Collection, ShowcaseScoreMode.Prestige },
                        _workingWidget.GetOption("Mode", ShowcaseScoreMode.Dual),
                        value => _workingWidget.SetOption("Mode", value),
                        ScoreModeName);
                    break;
                case ShowcaseWidgetKind.Pie:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Mode", "Mode"),
                        new[]
                        {
                            ShowcasePieMode.CompletedGames,
                            ShowcasePieMode.Provider,
                            ShowcasePieMode.Rarity,
                            ShowcasePieMode.Trophy
                        },
                        _workingWidget.GetOption("Mode", ShowcasePieMode.CompletedGames),
                        value => _workingWidget.SetOption("Mode", value),
                        PieModeName);
                    break;
                case ShowcaseWidgetKind.Timeline:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Range", "Range"),
                        new[]
                        {
                            TimelineRange.OneMonth,
                            TimelineRange.ThreeMonths,
                            TimelineRange.OneYear,
                            TimelineRange.All
                        },
                        ShowcaseTimelineOptions.GetRange(_workingWidget),
                        value => ShowcaseTimelineOptions.SetRange(_workingWidget, value),
                        TimelineRangeName);
                    break;
                case ShowcaseWidgetKind.NativePoints:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_GroupBy", "Group by"),
                        new[] { ShowcasePointsGrouping.Provider, ShowcasePointsGrouping.Game },
                        _workingWidget.GetOption("Grouping", ShowcasePointsGrouping.Provider),
                        value => _workingWidget.SetOption("Grouping", value),
                        PointsGroupingName);
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_TopN", "Top entries"),
                        new[] { 5, 8, 10, 15, 25 },
                        _workingWidget.GetOption("TopN", 8),
                        value => _workingWidget.SetOption("TopN", value),
                        value => value.ToString("N0", FormattingCulture.Current));
                    break;
                case ShowcaseWidgetKind.FavoriteGames:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Source", "Source"),
                        new[]
                        {
                            ShowcaseFavoriteGameSource.ShowcasePins,
                            ShowcaseFavoriteGameSource.PlayniteFavorites
                        },
                        _workingWidget.GetOption("Source", ShowcaseFavoriteGameSource.ShowcasePins),
                        value => _workingWidget.SetOption("Source", value),
                        FavoriteSourceName);
                    break;
                case ShowcaseWidgetKind.IconMosaic:
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_Source", "Source"),
                        new[] { ShowcaseMosaicSource.Recent, ShowcaseMosaicSource.Rarest, ShowcaseMosaicSource.Pinned },
                        _workingWidget.GetOption("Source", ShowcaseMosaicSource.Recent),
                        value => _workingWidget.SetOption("Source", value),
                        MosaicSourceName);
                    AddChoice(
                        panel,
                        Localize("LOCPlayAch_Showcase_ItemCount", "Item count"),
                        new[] { 12, 24, 36, 48, 64 },
                        _workingWidget.GetOption("Count", 24),
                        value => _workingWidget.SetOption("Count", value),
                        value => value.ToString("N0", FormattingCulture.Current));
                    break;
                case ShowcaseWidgetKind.ScreenshotSlideshow:
                    BuildScreenshotSettings(panel);
                    break;
                default:
                    panel.Children.Add(CreateHint(Localize(
                        "LOCPlayAch_Showcase_NoAdditionalSettings",
                        "This widget has no additional settings.")));
                    break;
            }

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var cancel = new Button
            {
                Content = Localize("LOCPlayAch_Button_Cancel", "Cancel"),
                MinWidth = 82,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancel.Click += (_, __) => Window.GetWindow(this)?.Close();
            var save = new Button
            {
                Content = Localize("LOCPlayAch_Button_Save", "Save"),
                MinWidth = 82,
                IsDefault = true
            };
            save.Click += Save_Click;
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);
            return root;
        }

        private void BuildProfileSettings(Panel panel)
        {
            _profileNameBox = AddTextBox(
                panel,
                Localize("LOCPlayAch_Showcase_ProfileName", "Display name"),
                _workingProfile.DisplayName);
            _profileSubtitleBox = AddTextBox(
                panel,
                Localize("LOCPlayAch_Showcase_ProfileSubtitle", "Subtitle"),
                _workingProfile.Subtitle);
            _avatarBox = AddImagePicker(
                panel,
                Localize("LOCPlayAch_Showcase_ProfileAvatar", "Avatar"),
                _workingProfile.AvatarPath);
            _backgroundBox = AddImagePicker(
                panel,
                Localize("LOCPlayAch_Showcase_ProfileBackground", "Background"),
                _workingProfile.BackgroundPath);
        }

        private void BuildScreenshotSettings(Panel panel)
        {
            AddChoice(
                panel,
                Localize("LOCPlayAch_Showcase_Variant", "Capture variant"),
                new[]
                {
                    ShowcaseScreenshotVariant.All,
                    ShowcaseScreenshotVariant.Clean,
                    ShowcaseScreenshotVariant.Notification,
                    ShowcaseScreenshotVariant.Framed
                },
                _workingWidget.GetOption("Variant", ShowcaseScreenshotVariant.All),
                value => _workingWidget.SetOption("Variant", value),
                ScreenshotVariantName);
            AddChoice(
                panel,
                Localize("LOCPlayAch_Showcase_Interval", "Interval"),
                new[] { 3, 5, 8, 15, 30 },
                _workingWidget.GetOption("IntervalSeconds", 8),
                value => _workingWidget.SetOption("IntervalSeconds", value),
                value => string.Format(
                    FormattingCulture.Current,
                    Localize("LOCPlayAch_Showcase_Seconds", "{0} seconds"),
                    value));
            AddChoice(
                panel,
                Localize("LOCPlayAch_Showcase_FitMode", "Fit"),
                new[] { ShowcaseImageFitMode.Fit, ShowcaseImageFitMode.Fill },
                _workingWidget.GetOption("FitMode", ShowcaseImageFitMode.Fill),
                value => _workingWidget.SetOption("FitMode", value),
                FitModeName);
            AddChoice(
                panel,
                Localize("LOCPlayAch_Showcase_Shuffle", "Shuffle"),
                new[] { true, false },
                _workingWidget.GetOption("Shuffle", true),
                value => _workingWidget.SetOption("Shuffle", value),
                value => value
                    ? Localize("LOCPlayAch_Settings_Override_On", "On")
                    : Localize("LOCPlayAch_Settings_Override_Off", "Off"));
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _sourceWidget.CustomTitle = _titleBox.Text?.Trim();
            _sourceWidget.Options = new Dictionary<string, string>(
                _workingWidget.Options ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
            if (_sourceWidget.Kind == ShowcaseWidgetKind.Profile)
            {
                var profile = _layout.Profile ?? (_layout.Profile = new ShowcaseProfileSettings());
                profile.DisplayName = _profileNameBox.Text?.Trim();
                profile.Subtitle = _profileSubtitleBox.Text?.Trim();
                profile.AvatarPath = ResolveManagedImage(
                    _avatarBox.Text,
                    _workingProfile.AvatarPath,
                    "avatar");
                profile.BackgroundPath = ResolveManagedImage(
                    _backgroundBox.Text,
                    _workingProfile.BackgroundPath,
                    "background");
            }

            Saved = true;
            Window.GetWindow(this)?.Close();
        }

        private string ResolveManagedImage(string selectedPath, string originalPath, string slot)
        {
            var path = selectedPath?.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (string.Equals(path, originalPath, StringComparison.OrdinalIgnoreCase))
            {
                return originalPath;
            }

            var plugin = PlayniteAchievementsPlugin.Instance;
            var imported = ManagedShowcaseImageService.Import(
                path,
                plugin?.GetPluginUserDataPath(),
                slot);
            if (!string.IsNullOrWhiteSpace(imported))
            {
                plugin?.ImageService?.EvictByUriSegment(imported);
            }

            return imported ?? originalPath;
        }

        private static TextBox AddTextBox(
            Panel panel,
            string label,
            string value,
            string hint = null)
        {
            panel.Children.Add(CreateLabel(label));
            var box = new TextBox
            {
                Text = value ?? string.Empty,
                MinHeight = 30,
                Padding = new Thickness(7, 4, 7, 4)
            };
            panel.Children.Add(box);
            if (!string.IsNullOrWhiteSpace(hint))
            {
                panel.Children.Add(CreateHint(hint));
            }

            return box;
        }

        private static TextBox AddImagePicker(Panel panel, string label, string value)
        {
            panel.Children.Add(CreateLabel(label));
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var box = new TextBox
            {
                Text = value ?? string.Empty,
                IsReadOnly = true,
                MinHeight = 30,
                Padding = new Thickness(7, 4, 7, 4)
            };
            row.Children.Add(box);
            var browse = new Button
            {
                Content = Localize("LOCPlayAch_Button_Browse", "Browse…"),
                MinWidth = 82,
                Margin = new Thickness(8, 0, 0, 0)
            };
            browse.Click += (_, __) =>
            {
                var dialog = new OpenFileDialog
                {
                    Filter = $"{Localize("LOCPlayAch_Showcase_ImageFiles", "Image files")} ({ImagePatterns})|{ImagePatterns}|{Localize("LOCPlayAch_Showcase_AllFiles", "All files")} (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };
                if (dialog.ShowDialog() == true)
                {
                    box.Text = dialog.FileName;
                }
            };
            Grid.SetColumn(browse, 1);
            row.Children.Add(browse);
            panel.Children.Add(row);
            return box;
        }

        private static void AddChoice<T>(
            Panel panel,
            string label,
            IEnumerable<T> values,
            T selected,
            Action<T> apply,
            Func<T, string> display)
        {
            panel.Children.Add(CreateLabel(label));
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

            combo.DisplayMemberPath = nameof(Choice<T>.Label);
            combo.SelectedItem = selectedChoice ?? (combo.Items.Count > 0 ? combo.Items[0] : null);
            combo.SelectionChanged += (_, __) =>
            {
                if (combo.SelectedItem is Choice<T> choice)
                {
                    apply(choice.Value);
                }
            };
            panel.Children.Add(combo);
        }

        private static TextBlock CreateHeading(string text)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            block.SetResourceReference(TextBlock.ForegroundProperty, "PlayAch.Brush.Text");
            return block;
        }

        private static TextBlock CreateLabel(string text)
        {
            var block = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 4)
            };
            block.SetResourceReference(TextBlock.ForegroundProperty, "PlayAch.Brush.Text");
            return block;
        }

        private static TextBlock CreateHint(string text)
        {
            var block = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            block.SetResourceReference(TextBlock.ForegroundProperty, "PlayAch.Brush.Text.Secondary");
            return block;
        }

        private static string GetWidgetName(ShowcaseWidgetKind kind)
        {
            var definition = ShowcaseWidgetCatalog.Get(kind);
            return Localize(definition.NameKey, kind.ToString());
        }

        private static string ScoreModeName(ShowcaseScoreMode value) => Localize(
            $"LOCPlayAch_Showcase_ScoreMode_{value}",
            value.ToString());

        private static string PieModeName(ShowcasePieMode value) => Localize(
            $"LOCPlayAch_Showcase_PieMode_{value}",
            value.ToString());

        private static string PointsGroupingName(ShowcasePointsGrouping value) => Localize(
            $"LOCPlayAch_Showcase_PointsGrouping_{value}",
            value.ToString());

        private static string FavoriteSourceName(ShowcaseFavoriteGameSource value) => Localize(
            $"LOCPlayAch_Showcase_FavoriteSource_{value}",
            value.ToString());

        private static string MosaicSourceName(ShowcaseMosaicSource value) => Localize(
            $"LOCPlayAch_Showcase_MosaicSource_{value}",
            value.ToString());

        private static string ScreenshotVariantName(ShowcaseScreenshotVariant value) => Localize(
            $"LOCPlayAch_Showcase_ScreenshotVariant_{value}",
            value.ToString());

        private static string FitModeName(ShowcaseImageFitMode value) => Localize(
            $"LOCPlayAch_Showcase_ImageFit_{value}",
            value.ToString());

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

        private static double GetEditorHeight(ShowcaseWidgetKind kind)
        {
            switch (kind)
            {
                case ShowcaseWidgetKind.Profile:
                    return 445;
                case ShowcaseWidgetKind.ScreenshotSlideshow:
                    return 410;
                case ShowcaseWidgetKind.NativePoints:
                case ShowcaseWidgetKind.IconMosaic:
                    return 315;
                default:
                    return 255;
            }
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

        private sealed class Choice<T>
        {
            public T Value { get; set; }

            public string Label { get; set; }
        }
    }
}

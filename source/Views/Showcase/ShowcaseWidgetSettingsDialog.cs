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
using static PlayniteAchievements.Views.Showcase.ShowcaseUiText;

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
            Width = 400;
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
                Localize("LOCPlayAch_Showcase_WidgetSettingsTitle"),
                GetWidgetName(widget.Kind));
            var height = GetEditorHeight(widget.Kind);
            var window = PlayniteUiProvider.CreateExtensionWindow(
                title,
                editor,
                new WindowOptions
                {
                    Width = 400,
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
                Margin = new Thickness(12),
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

            _titleBox = AddTextBox(
                panel,
                Localize("LOCPlayAch_Showcase_CustomTitle"),
                _workingWidget.CustomTitle);

            if (_workingWidget.Kind == ShowcaseWidgetKind.Profile)
            {
                BuildProfileSettings(panel);
            }
            else if (ShowcaseWidgetOptionsControl.HasOptions(_workingWidget.Kind))
            {
                panel.Children.Add(new ShowcaseWidgetOptionsControl(
                    _workingWidget,
                    publishChanges: false,
                    margin: new Thickness(0),
                    loadStyles: false));
            }

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var cancel = new Button
            {
                Content = Localize("LOCPlayAch_Button_Cancel"),
                MinWidth = 82,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancel.Click += (_, __) => Window.GetWindow(this)?.Close();
            var save = new Button
            {
                Content = Localize("LOCPlayAch_Button_Save"),
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
            var providerHint = new TextBlock
            {
                Text = Localize("LOCPlayAch_Showcase_ProfileProviderHint"),
                FontStyle = FontStyles.Italic,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            providerHint.SetResourceReference(TextBlock.ForegroundProperty, "PlayAch.Brush.Text");
            panel.Children.Add(providerHint);

            _profileNameBox = AddTextBox(
                panel,
                Localize("LOCPlayAch_Showcase_ProfileName"),
                _workingProfile.DisplayName);
            _profileSubtitleBox = AddTextBox(
                panel,
                Localize("LOCPlayAch_Showcase_ProfileSubtitle"),
                _workingProfile.Subtitle);
            _avatarBox = AddImagePicker(
                panel,
                Localize("LOCPlayAch_Showcase_ProfileAvatar"),
                _workingProfile.AvatarPath);
            _backgroundBox = AddImagePicker(
                panel,
                Localize("LOCPlayAch_Showcase_ProfileBackground"),
                _workingProfile.BackgroundPath);
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
            string value)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var labelBlock = CreateLabel(label);
            labelBlock.Margin = new Thickness(0, 0, 10, 0);
            labelBlock.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(labelBlock);
            var box = new TextBox
            {
                Text = value ?? string.Empty,
                MinHeight = 30,
                Padding = new Thickness(7, 4, 7, 4)
            };
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            panel.Children.Add(row);

            return box;
        }

        private static TextBox AddImagePicker(Panel panel, string label, string value)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var labelBlock = CreateLabel(label);
            labelBlock.Margin = new Thickness(0, 0, 10, 0);
            labelBlock.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(labelBlock);
            var box = new TextBox
            {
                Text = value ?? string.Empty,
                IsReadOnly = true,
                MinHeight = 30,
                Padding = new Thickness(7, 4, 7, 4)
            };
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            var browse = new Button
            {
                Content = Localize("LOCPlayAch_Button_Browse"),
                MinWidth = 82,
                Margin = new Thickness(8, 0, 0, 0)
            };
            browse.Click += (_, __) =>
            {
                var dialog = new OpenFileDialog
                {
                    Filter = $"{Localize("LOCPlayAch_Showcase_ImageFiles")} ({ImagePatterns})|{ImagePatterns}|{Localize("LOCPlayAch_Showcase_AllFiles")} (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };
                if (dialog.ShowDialog() == true)
                {
                    box.Text = dialog.FileName;
                }
            };
            Grid.SetColumn(browse, 2);
            row.Children.Add(browse);
            var clear = new Button
            {
                Content = Localize("LOCPlayAch_Button_Clear"),
                MinWidth = 72,
                Margin = new Thickness(8, 0, 0, 0)
            };
            clear.Click += (_, __) => box.Text = string.Empty;
            Grid.SetColumn(clear, 3);
            row.Children.Add(clear);
            panel.Children.Add(row);
            return box;
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

        private static double GetEditorHeight(ShowcaseWidgetKind kind)
        {
            switch (kind)
            {
                case ShowcaseWidgetKind.Profile:
                    return 300;
                case ShowcaseWidgetKind.ScreenshotSlideshow:
                case ShowcaseWidgetKind.GameSummaries:
                    return 250;
                case ShowcaseWidgetKind.NativePoints:
                case ShowcaseWidgetKind.IconMosaic:
                case ShowcaseWidgetKind.Scores:
                case ShowcaseWidgetKind.GameMosaic:
                    return 210;
                default:
                    return 175;
            }
        }

    }
}

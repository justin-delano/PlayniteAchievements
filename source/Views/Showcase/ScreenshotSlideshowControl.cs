using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Captures;
using PlayniteAchievements.Views.Dialogs;
using PlayniteAchievements.Views.Helpers;
using static PlayniteAchievements.Views.Showcase.ShowcaseUiText;

namespace PlayniteAchievements.Views.Showcase
{
    public sealed class ScreenshotSlideshowControl : UserControl, IDisposable
    {
        private static readonly string PreviousGlyph = char.ConvertFromUtf32(0xE76B);
        private static readonly string NextGlyph = char.ConvertFromUtf32(0xE76C);
        private static readonly string PlayGlyph = char.ConvertFromUtf32(0xE768);
        private static readonly string PauseGlyph = char.ConvertFromUtf32(0xE769);
        private static readonly string FullscreenGlyph = char.ConvertFromUtf32(0xE740);

        private readonly ShowcaseWidgetInstanceSettings _settings;
        private readonly Image _image;
        private readonly TextBlock _status;
        private readonly TextBlock _caption;
        private readonly TextBlock _position;
        private readonly Button _previous;
        private readonly Button _next;
        private readonly Button _pause;
        private readonly Button _fullscreen;
        private readonly Border _captionBar;
        private readonly Border _transport;
        private readonly DispatcherTimer _timer;
        private readonly Random _random = new Random();
        private CaptureLibraryService _captureLibrary;
        private IReadOnlyList<CaptureItem> _items = Array.Empty<CaptureItem>();
        private int _index;
        private int _reloadVersion;
        private bool _paused;
        private bool _showChrome;

        public ScreenshotSlideshowControl(ShowcaseWidgetInstanceSettings settings)
        {
            _settings = settings ?? ShowcaseWidgetSettingsFactory.CreateDefault(
                ShowcaseWidgetKind.ScreenshotSlideshow);
            _image = new Image
            {
                Margin = new Thickness(8),
                Stretch = Stretch.Uniform
            };
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            _status = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(18),
                Opacity = 0.68
            };
            _status.SetResourceReference(TextBlock.ForegroundProperty, "PlayAch.Brush.Text");
            _caption = CreateTextBlock(FontWeights.SemiBold);
            _caption.TextTrimming = TextTrimming.CharacterEllipsis;
            _position = CreateTextBlock(FontWeights.Normal);
            _position.Opacity = 0.65;

            _previous = CreateGlyphButton(
                PreviousGlyph,
                "PlayAch.Capture.NavButtonStyle",
                "LOCPlayAch_Showcase_PreviousScreenshot",
                "Previous screenshot",
                () => Move(-1));
            _previous.Margin = new Thickness(0, 0, 8, 0);
            _next = CreateGlyphButton(
                NextGlyph,
                "PlayAch.Capture.NavButtonStyle",
                "LOCPlayAch_Showcase_NextScreenshot",
                "Next screenshot",
                () => Move(1));
            _next.Margin = new Thickness(8, 0, 0, 0);
            _pause = CreateGlyphButton(
                PauseGlyph,
                "PlayAch.Capture.GlyphButtonStyle",
                "LOCPlayAch_Showcase_PauseSlideshow",
                "Pause slideshow",
                TogglePause);
            _fullscreen = CreateGlyphButton(
                FullscreenGlyph,
                "PlayAch.Capture.GlyphButtonStyle",
                "LOCPlayAch_Captures_Fullscreen",
                "Fullscreen",
                OpenFullscreen);

            _transport = BuildTransport();
            _captionBar = BuildCaptionBar();
            _timer = new DispatcherTimer();
            _timer.Tick += Timer_Tick;
            Content = Build();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += OnSizeChanged;
        }

        public void Dispose()
        {
            _reloadVersion++;
            _timer.Stop();
            if (_captureLibrary != null)
            {
                _captureLibrary.Changed -= CaptureLibrary_Changed;
                _captureLibrary = null;
            }
        }

        private UIElement Build()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var mediaRow = new Grid();
            mediaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mediaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mediaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mediaRow.Children.Add(_previous);

            var mediaLayers = new Grid();
            mediaLayers.Children.Add(_image);
            mediaLayers.Children.Add(_status);
            mediaLayers.Children.Add(_transport);
            var mediaFrame = new Border
            {
                Child = mediaLayers,
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                ClipToBounds = true,
                Cursor = Cursors.Hand
            };
            mediaFrame.SetResourceReference(Border.BorderBrushProperty, "PlayAch.Brush.Border");
            mediaFrame.MouseLeftButtonUp += (_, args) =>
            {
                if (args.ClickCount > 1)
                {
                    OpenFullscreen();
                    args.Handled = true;
                }
            };
            Grid.SetColumn(mediaFrame, 1);
            mediaRow.Children.Add(mediaFrame);
            Grid.SetColumn(_next, 2);
            mediaRow.Children.Add(_next);
            root.Children.Add(mediaRow);

            Grid.SetRow(_captionBar, 1);
            root.Children.Add(_captionBar);
            return root;
        }

        private Border BuildTransport()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            panel.Children.Add(_pause);
            panel.Children.Add(_fullscreen);
            return new Border
            {
                Child = panel,
                Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(12, 0, 12, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom
            };
        }

        private Border BuildCaptionBar()
        {
            var captionGrid = new Grid();
            captionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            captionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            captionGrid.Children.Add(_caption);
            Grid.SetColumn(_position, 1);
            _position.Margin = new Thickness(10, 0, 0, 0);
            captionGrid.Children.Add(_position);
            return new Border
            {
                Child = captionGrid,
                Padding = new Thickness(2, 7, 2, 0)
            };
        }

        private static TextBlock CreateTextBlock(FontWeight weight)
        {
            var text = new TextBlock
            {
                FontWeight = weight,
                VerticalAlignment = VerticalAlignment.Center
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "PlayAch.Brush.Text");
            return text;
        }

        private static Button CreateGlyphButton(
            string glyph,
            string styleKey,
            string localizationKey,
            string fallback,
            Action action)
        {
            var label = Localize(localizationKey, fallback);
            var button = new Button
            {
                Content = glyph,
                ToolTip = label
            };
            button.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
            AutomationProperties.SetName(button, label);
            button.Click += (_, __) => action();
            return button;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_captureLibrary != null)
            {
                _captureLibrary.Changed -= CaptureLibrary_Changed;
            }

            _captureLibrary = PlayniteAchievementsPlugin.Instance?.CaptureLibraryService;
            if (_captureLibrary != null)
            {
                _captureLibrary.Changed += CaptureLibrary_Changed;
            }

            _timer.Interval = TimeSpan.FromSeconds(Math.Max(
                2,
                ShowcaseWidgetOptions.GetSlideshowIntervalSeconds(_settings)));
            if (!_paused)
            {
                _timer.Start();
            }

            UpdateResponsiveChrome(ActualWidth, ActualHeight);
            _ = ReloadAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => Dispose();

        private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
            UpdateResponsiveChrome(e.NewSize.Width, e.NewSize.Height);

        private void UpdateResponsiveChrome(double width, double height)
        {
            _showChrome = width >= 260 && height >= 160;
            UpdateChromeVisibility();
        }

        private void UpdateChromeVisibility()
        {
            var canNavigate = _showChrome && _items.Count > 1;
            _previous.Visibility = canNavigate ? Visibility.Visible : Visibility.Collapsed;
            _next.Visibility = canNavigate ? Visibility.Visible : Visibility.Collapsed;
            _captionBar.Visibility = _showChrome ? Visibility.Visible : Visibility.Collapsed;
            _transport.Visibility = _showChrome && _items.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            _pause.IsEnabled = _items.Count > 1;
            _fullscreen.IsEnabled = _items.Count > 0;
        }

        private void CaptureLibrary_Changed(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => _ = ReloadAsync()));
        }

        private async Task ReloadAsync()
        {
            var captureLibrary = _captureLibrary;
            var reloadVersion = ++_reloadVersion;
            var selectedVariant = ShowcaseWidgetOptions.GetScreenshotVariant(_settings);
            CaptureVariant? captureVariant = null;
            switch (selectedVariant)
            {
                case ShowcaseScreenshotVariant.Clean:
                    captureVariant = CaptureVariant.Clean;
                    break;
                case ShowcaseScreenshotVariant.Notification:
                    captureVariant = CaptureVariant.Notification;
                    break;
                case ShowcaseScreenshotVariant.Framed:
                    captureVariant = CaptureVariant.Framed;
                    break;
            }

            _status.Text = Localize(
                "LOCPlayAch_Showcase_LoadingScreenshots",
                "Loading screenshots…");
            _status.Visibility = Visibility.Visible;

            IReadOnlyList<CaptureItem> items;
            try
            {
                items = captureLibrary == null
                    ? (IReadOnlyList<CaptureItem>)Array.Empty<CaptureItem>()
                    : await Task.Run(() => captureLibrary.GetScreenshots(captureVariant).ToList());
            }
            catch
            {
                items = Array.Empty<CaptureItem>();
            }

            if (reloadVersion != _reloadVersion ||
                !ReferenceEquals(_captureLibrary, captureLibrary) ||
                !IsLoaded)
            {
                return;
            }

            var currentPath = Current?.FilePath;
            _items = items;
            _index = ResolveIndex(currentPath);
            ShowCurrent();
        }

        private int ResolveIndex(string previousPath)
        {
            if (_items.Count == 0)
            {
                return 0;
            }

            if (!string.IsNullOrWhiteSpace(previousPath))
            {
                for (var index = 0; index < _items.Count; index++)
                {
                    if (string.Equals(
                            _items[index].FilePath,
                            previousPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return index;
                    }
                }
            }

            return Math.Max(0, Math.Min(_index, _items.Count - 1));
        }

        private CaptureItem Current =>
            _index >= 0 && _index < _items.Count ? _items[_index] : null;

        private void Timer_Tick(object sender, EventArgs e) => Move(1);

        private void TogglePause()
        {
            _paused = !_paused;
            var label = _paused
                ? Localize("LOCPlayAch_Showcase_ResumeSlideshow", "Resume slideshow")
                : Localize("LOCPlayAch_Showcase_PauseSlideshow", "Pause slideshow");
            _pause.Content = _paused ? PlayGlyph : PauseGlyph;
            _pause.ToolTip = label;
            AutomationProperties.SetName(_pause, label);
            if (_paused)
            {
                _timer.Stop();
            }
            else
            {
                _timer.Start();
            }
        }

        private void Move(int direction)
        {
            if (_items.Count < 2)
            {
                return;
            }

            if (ShowcaseWidgetOptions.GetShuffle(_settings))
            {
                var next = _index;
                while (next == _index)
                {
                    next = _random.Next(_items.Count);
                }

                _index = next;
            }
            else
            {
                _index = (_index + Math.Sign(direction) + _items.Count) % _items.Count;
            }

            ShowCurrent();
        }

        private void OpenFullscreen()
        {
            var current = Current;
            if (current != null)
            {
                FullscreenMediaViewerPresenter.Show(this, current.FilePath, false);
            }
        }

        private void ShowCurrent()
        {
            _image.Stretch = ShowcaseWidgetOptions.GetImageFitMode(_settings) ==
                ShowcaseImageFitMode.Fill
                ? Stretch.UniformToFill
                : Stretch.Uniform;
            var current = Current;
            if (current == null)
            {
                _image.Source = null;
                _caption.Text = string.Empty;
                _position.Text = string.Empty;
                _status.Text = Localize(
                    "LOCPlayAch_Showcase_NoScreenshots",
                    "No achievement screenshots found");
                _status.Visibility = Visibility.Visible;
                UpdateChromeVisibility();
                return;
            }

            var renderedEdge = Math.Max(ActualWidth, ActualHeight);
            var decodePixel = (int)Math.Max(
                720,
                Math.Min(3840, Math.Ceiling(renderedEdge * 1.5)));
            AsyncImage.SetDecodePixel(_image, decodePixel);
            AsyncImage.SetUri(_image, current.FilePath);
            _caption.Text = (current.AchievementStem ?? string.Empty).Replace('_', ' ');
            _position.Text = $"{_index + 1} / {_items.Count}";
            _status.Visibility = Visibility.Collapsed;
            UpdateChromeVisibility();
        }
    }
}

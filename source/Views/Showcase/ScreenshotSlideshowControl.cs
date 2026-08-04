using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Captures;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.Showcase
{
    public sealed class ScreenshotSlideshowControl : UserControl, IDisposable
    {
        private readonly ShowcaseWidgetInstanceSettings _settings;
        private readonly Image _image;
        private readonly TextBlock _status;
        private readonly Button _pause;
        private readonly StackPanel _controls;
        private readonly DispatcherTimer _timer;
        private readonly Random _random = new Random();
        private CaptureLibraryService _captureLibrary;
        private IReadOnlyList<string> _paths = Array.Empty<string>();
        private int _index;
        private bool _paused;

        public ScreenshotSlideshowControl(ShowcaseWidgetInstanceSettings settings)
        {
            _settings = settings ?? new ShowcaseWidgetInstanceSettings
            {
                Kind = ShowcaseWidgetKind.ScreenshotSlideshow
            };
            _image = new Image();
            _status = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.7
            };
            _status.SetResourceReference(TextBlock.ForegroundProperty, "PlayAch.Brush.Text");
            _pause = CreateButton(
                "Ⅱ",
                Localize("LOCPlayAch_Showcase_PauseSlideshow", "Pause slideshow"),
                TogglePause);
            _timer = new DispatcherTimer();
            _timer.Tick += Timer_Tick;
            _controls = BuildControls();
            Content = Build();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += OnSizeChanged;
        }

        public void Dispose()
        {
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
            root.Children.Add(_image);
            root.Children.Add(_status);
            root.Children.Add(_controls);
            return root;
        }

        private StackPanel BuildControls()
        {
            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 4)
            };
            controls.Children.Add(CreateButton(
                "‹",
                Localize("LOCPlayAch_Showcase_PreviousScreenshot", "Previous screenshot"),
                () => Move(-1)));
            controls.Children.Add(_pause);
            controls.Children.Add(CreateButton(
                "›",
                Localize("LOCPlayAch_Showcase_NextScreenshot", "Next screenshot"),
                () => Move(1)));
            return controls;
        }

        private Button CreateButton(string content, string tooltip, Action action)
        {
            var button = new Button
            {
                Content = content,
                ToolTip = tooltip,
                MinWidth = 28,
                Margin = new Thickness(2),
                Padding = new Thickness(4, 1, 4, 1)
            };
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

            Reload();
            _timer.Interval = TimeSpan.FromSeconds(Math.Max(
                2,
                Math.Min(300, _settings.GetOption("IntervalSeconds", 8))));
            if (!_paused)
            {
                _timer.Start();
            }

            UpdateResponsiveControls(ActualWidth, ActualHeight);
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
            UpdateResponsiveControls(e.NewSize.Width, e.NewSize.Height);

        private void UpdateResponsiveControls(double width, double height)
        {
            _controls.Visibility = width < 260 || height < 160
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_captureLibrary != null)
            {
                _captureLibrary.Changed -= CaptureLibrary_Changed;
            }

            Dispose();
        }

        private void CaptureLibrary_Changed(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(Reload));
        }

        private void Reload()
        {
            var selectedVariant = _settings.GetOption(
                "Variant",
                ShowcaseScreenshotVariant.All);
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

            _paths = _captureLibrary == null
                ? (IReadOnlyList<string>)Array.Empty<string>()
                : _captureLibrary
                    .GetScreenshots(captureVariant)
                    .Select(item => item.FilePath)
                    .ToList();
            _index = Math.Max(0, Math.Min(_index, _paths.Count - 1));
            ShowCurrent();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            Move(1);
        }

        private void TogglePause()
        {
            _paused = !_paused;
            _pause.Content = _paused ? "▶" : "Ⅱ";
            _pause.ToolTip = _paused
                ? Localize("LOCPlayAch_Showcase_ResumeSlideshow", "Resume slideshow")
                : Localize("LOCPlayAch_Showcase_PauseSlideshow", "Pause slideshow");
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
            if (_paths.Count == 0)
            {
                return;
            }

            if (_settings.GetOption("Shuffle", true) && _paths.Count > 1)
            {
                var next = _index;
                while (next == _index)
                {
                    next = _random.Next(_paths.Count);
                }

                _index = next;
            }
            else
            {
                _index = (_index + Math.Sign(direction) + _paths.Count) % _paths.Count;
            }

            ShowCurrent();
        }

        private void ShowCurrent()
        {
            _image.Stretch = _settings.GetOption(
                    "FitMode",
                    ShowcaseImageFitMode.Fill) == ShowcaseImageFitMode.Fill
                ? Stretch.UniformToFill
                : Stretch.Uniform;
            if (_paths.Count == 0)
            {
                _image.Source = null;
                _status.Text = Localize(
                    "LOCPlayAch_Showcase_NoScreenshots",
                    "No achievement screenshots found");
                _status.Visibility = Visibility.Visible;
                return;
            }

            AsyncImage.SetDecodePixel(_image, 0);
            AsyncImage.SetUri(_image, _paths[_index]);
            _status.Visibility = Visibility.Collapsed;
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
    }
}

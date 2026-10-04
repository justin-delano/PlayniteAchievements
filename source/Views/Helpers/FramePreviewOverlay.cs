using System;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// Shows a screenshot frame full-monitor over Playnite so it can be checked at real scale.
    /// Reproduces the compositor's 1080-DIP virtual canvas exactly (Viewbox Fill onto the
    /// monitor), so what is shown matches what gets stamped onto saved images. Dismissed by
    /// click, Escape, or a 10s auto-close timer. Shared by the settings frame fire-test and any
    /// other surface that previews a frame template.
    /// </summary>
    internal static class FramePreviewOverlay
    {
        /// <summary>
        /// Opens the overlay on the monitor of Playnite's current window (or
        /// <paramref name="fallbackReference"/> when Playnite reports none) rendering
        /// <paramref name="viewModel"/> through <paramref name="frameTemplate"/>. Returns the shown
        /// window so the caller can track and close it, or null when nothing was shown (no
        /// template, or no monitor could be resolved). UI thread only.
        /// </summary>
        public static Window Show(
            IPlayniteAPI playniteApi,
            Window fallbackReference,
            DataTemplate frameTemplate,
            AchievementToastViewModel viewModel)
        {
            if (frameTemplate == null)
            {
                return null;
            }

            var window = PlayniteUiProvider.CreateBorderlessTopmostWindow(
                playniteApi,
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"));
            window.SizeToContent = SizeToContent.Manual;
            window.ShowActivated = true;
            window.Focusable = true;

            var reference = playniteApi?.Dialogs?.GetCurrentAppWindow() ?? fallbackReference;
            var monitorPixels = PlayniteUiProvider.PlaceOnWindowMonitor(window, reference);
            if (monitorPixels == null)
            {
                return null;
            }

            var (canvasWidth, canvasHeight, _) = ScreenshotFrameCompositor.ComputeCanvas(
                monitorPixels.Value.Width,
                monitorPixels.Value.Height);
            var canvas = new Grid
            {
                Width = canvasWidth,
                Height = canvasHeight,
                // Almost-transparent so the live screen shows through while the window still
                // receives the dismissing click (fully transparent pixels are not hit-testable).
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(1, 0, 0, 0)),
            };
            canvas.Children.Add(new ContentControl
            {
                Content = viewModel,
                ContentTemplate = frameTemplate,
            });
            window.Content = new Viewbox
            {
                Stretch = System.Windows.Media.Stretch.Fill,
                Child = canvas,
            };

            var autoClose = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10),
            };
            autoClose.Tick += (s, args) => window.Close();
            window.PreviewMouseDown += (s, args) => window.Close();
            window.PreviewKeyDown += (s, args) =>
            {
                if (args.Key == System.Windows.Input.Key.Escape)
                {
                    args.Handled = true;
                    window.Close();
                }
            };
            window.Closed += (s, args) => autoClose.Stop();

            window.Show();
            window.Focus();
            autoClose.Start();
            return window;
        }
    }
}

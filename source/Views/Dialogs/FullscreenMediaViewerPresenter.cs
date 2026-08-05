using System;
using System.Windows;
using System.Windows.Input;

namespace PlayniteAchievements.Views.Dialogs
{
    /// <summary>Hosts the shared capture lightbox for gallery-style surfaces.</summary>
    internal static class FullscreenMediaViewerPresenter
    {
        public static void Show(FrameworkElement owner, string path, bool isVideo)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var content = new FullscreenMediaViewer(path, isVideo);
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = System.Windows.Media.Brushes.Black,
                ShowInTaskbar = false,
                Owner = Window.GetWindow(owner),
                Content = content
            };
            content.RequestClose += (_, __) => window.Close();
            window.Loaded += (_, __) => window.WindowState = WindowState.Maximized;
            window.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    window.Close();
                    args.Handled = true;
                }
            };
            window.ShowDialog();
        }
    }
}

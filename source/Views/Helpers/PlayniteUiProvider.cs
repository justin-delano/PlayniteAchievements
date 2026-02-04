using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Playnite.SDK;

namespace PlayniteAchievements.Views.Helpers
{
    public static class PlayniteUiProvider
    {
        public static void RestoreMainView()
        {
            API.Instance.MainView.SwitchToLibraryView();
        }

        public static void HandleEsc(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (sender is Window window)
                {
                    e.Handled = true;
                    window.Close();
                }
            }
        }

        public static Window CreateExtensionWindow(string Title, UserControl ViewExtension, WindowOptions windowOptions = null)
        {
            if (windowOptions == null)
            {
                windowOptions = new WindowOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false,
                    ShowCloseButton = true
                };
            }

            Window windowExtension = API.Instance.Dialogs.CreateWindow(windowOptions);

            windowExtension.Title = Title;
            windowExtension.ShowInTaskbar = false;
            windowExtension.ResizeMode = windowOptions.CanBeResizable ? ResizeMode.CanResize : ResizeMode.NoResize;
            windowExtension.Owner = API.Instance.Dialogs.GetCurrentAppWindow();
            windowExtension.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            windowExtension.Content = ViewExtension;

            if (!double.IsNaN(ViewExtension.Height) && !double.IsNaN(ViewExtension.Width))
            {
                windowExtension.Height = ViewExtension.Height + 25;
                windowExtension.Width = ViewExtension.Width;
            }
            else if (!double.IsNaN(ViewExtension.MinHeight) && !double.IsNaN(ViewExtension.MinWidth) && ViewExtension.MinHeight > 0 && ViewExtension.MinWidth > 0)
            {
                windowExtension.Height = ViewExtension.MinHeight + 25;
                windowExtension.Width = ViewExtension.MinWidth;
            }
            else if (windowOptions.Width != 0 && windowOptions.Height != 0)
            {
                windowExtension.Width = windowOptions.Width;
                windowExtension.Height = windowOptions.Height;
            }
            else
            {
                windowExtension.SizeToContent = SizeToContent.WidthAndHeight;
            }

            windowExtension.PreviewKeyDown += new KeyEventHandler(HandleEsc);

            return windowExtension;
        }
    }

    public class WindowOptions : WindowCreationOptions
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public bool CanBeResizable { get; set; } = false;
    }
}
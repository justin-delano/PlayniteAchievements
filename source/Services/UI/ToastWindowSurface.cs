using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace PlayniteAchievements.Services.UI
{
    /// <summary>
    /// Makes a revealed toast window composite through DWM instead of as a per-pixel layered window,
    /// and supplies the two things a layered window had for free: hiding (cloaking replaces
    /// <c>Window.Opacity</c>, which needs <c>AllowsTransparency</c>) and click-through (a window region
    /// limits hit-testing to the card while it rests).
    ///
    /// A layered window copies its whole surface on every changed frame, which grows with device
    /// pixels; at a large card on a scaled display it sustained half the refresh rate with no other
    /// load. DWM composites a non-layered window's surface directly and held the full rate
    /// (tools/capture-harness/README.md, "At the user's device size, the layered surface is the
    /// ceiling and a DWM window lifts it").
    ///
    /// All members are wrapped so they can never throw into the toast pipeline.
    /// </summary>
    internal static class ToastWindowSurface
    {
        private const int GwlExStyle = -20;
        private const int WsExNoActivate = 0x08000000;
        private const int DwmwaCloak = 13;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwaBorderColor = 34;
        private const int DwmwcpDoNotRound = 1;
        private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left, Right, Top, Bottom;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr handle);

        /// <summary>
        /// Switches <paramref name="window"/> to DWM composition. Must run before the window's handle
        /// exists, since <c>AllowsTransparency</c> cannot change afterwards.
        /// </summary>
        public static void UseDwmComposition(Window window)
        {
            if (window == null)
            {
                return;
            }

            try
            {
                window.AllowsTransparency = false;
                window.Background = Brushes.Transparent;

                // The chrome extends DWM's frame across the whole client area, and re-applies it on
                // the messages that reset it (composition changes, activation); a zero glass frame
                // would undo the explicit extension below.
                var chrome = WindowChrome.GetWindowChrome(window);
                if (chrome != null)
                {
                    chrome.GlassFrameThickness = WindowChrome.GlassFrameCompleteThickness;
                }

                window.SourceInitialized += OnSourceInitialized;
            }
            catch
            {
            }
        }

        private static void OnSourceInitialized(object sender, EventArgs e)
        {
            try
            {
                var window = (Window)sender;
                window.SourceInitialized -= OnSourceInitialized;
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                var source = HwndSource.FromHwnd(hwnd);
                if (source?.CompositionTarget != null)
                {
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;
                }

                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);

                // A click on the card must not take focus from the game.
                SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate);

                // Windows 11 rounds and outlines top-level windows; either would cut into the card's
                // glow at the window edge. Both attributes fail harmlessly on Windows 10.
                var corner = DwmwcpDoNotRound;
                DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
                var border = DwmwaColorNone;
                DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(int));
            }
            catch
            {
            }
        }

        /// <summary>
        /// Hides or shows the window on screen without hiding it from WPF, which keeps rendering it,
        /// so the first composed frames of a cloaked window still pay the card's one-time costs.
        /// Needs the window's handle.
        /// </summary>
        public static void SetCloaked(Window window, bool cloaked)
        {
            try
            {
                var hwnd = window == null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                var value = cloaked ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DwmwaCloak, ref value, sizeof(int));
            }
            catch
            {
            }
        }

        /// <summary>
        /// Limits the window to <paramref name="element"/>'s bounds, grown by
        /// <paramref name="allowanceDip"/> for effects drawn past them, so clicks anywhere else in the
        /// window reach what is underneath. Null <paramref name="element"/> restores the whole window.
        /// </summary>
        public static void SetHitRegion(Window window, FrameworkElement element, double allowanceDip)
        {
            try
            {
                var hwnd = window == null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                if (element == null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
                {
                    SetWindowRgn(hwnd, IntPtr.Zero, true);
                    return;
                }

                var source = PresentationSource.FromVisual(window);
                var toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
                var bounds = element.TransformToAncestor(window)
                    .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                bounds.Inflate(allowanceDip, allowanceDip);
                bounds.Intersect(new Rect(0, 0, window.ActualWidth, window.ActualHeight));
                if (bounds.IsEmpty)
                {
                    SetWindowRgn(hwnd, IntPtr.Zero, true);
                    return;
                }

                var topLeft = toDevice.Transform(bounds.TopLeft);
                var bottomRight = toDevice.Transform(bounds.BottomRight);
                var region = CreateRectRgn(
                    (int)Math.Floor(topLeft.X), (int)Math.Floor(topLeft.Y),
                    (int)Math.Ceiling(bottomRight.X), (int)Math.Ceiling(bottomRight.Y));
                if (region == IntPtr.Zero)
                {
                    return;
                }

                // The system owns the region once SetWindowRgn succeeds; only a failed call leaves it ours.
                if (SetWindowRgn(hwnd, region, true) == 0)
                {
                    DeleteObject(region);
                }
            }
            catch
            {
            }
        }
    }
}

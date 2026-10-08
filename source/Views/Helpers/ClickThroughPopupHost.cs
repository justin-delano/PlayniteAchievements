using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// Makes the popup window that hosts a visual transparent to mouse input. LiveCharts shows
    /// DataTooltip in a Popup, which is its own HWND: IsHitTestVisible only stops WPF hit-testing
    /// inside it, so a tooltip under the cursor still takes the mouse from the chart, the hovered
    /// slice or bar raises MouseLeave, and the tooltip closes and reopens in a loop.
    /// WS_EX_TRANSPARENT on the layered popup window passes mouse messages to the window beneath.
    /// </summary>
    public static class ClickThroughPopupHost
    {
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TRANSPARENT = 0x00000020;
        private const long WS_EX_LAYERED = 0x00080000;
        private const long WS_EX_NOACTIVATE = 0x08000000;

        /// <summary>
        /// Applies the click-through style to every window that hosts <paramref name="element"/>.
        /// WPF destroys a popup's HWND on close and creates a new one on open, so this follows
        /// source changes rather than styling a single window.
        /// </summary>
        public static void Attach(UIElement element)
        {
            if (element == null)
            {
                return;
            }

            PresentationSource.AddSourceChangedHandler(element, OnSourceChanged);
        }

        private static void OnSourceChanged(object sender, SourceChangedEventArgs e)
        {
            // Only popup roots: a Window host would make the whole application window click-through.
            if (!(e.NewSource is HwndSource source)
                || source.Handle == IntPtr.Zero
                || source.RootVisual is Window)
            {
                return;
            }

            var style = GetWindowLong(source.Handle, GWL_EXSTYLE).ToInt64();
            var clickThrough = style | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE;
            if (clickThrough != style)
            {
                SetWindowLong(source.Handle, GWL_EXSTYLE, new IntPtr(clickThrough));
            }
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        // The Ptr entry points do not exist in 32-bit user32, so the pointer size decides which to call.
        private static IntPtr GetWindowLong(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex)
                : new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        private static void SetWindowLong(IntPtr hWnd, int nIndex, IntPtr value)
        {
            if (IntPtr.Size == 8)
            {
                SetWindowLongPtr64(hWnd, nIndex, value);
            }
            else
            {
                SetWindowLong32(hWnd, nIndex, unchecked((int)value.ToInt64()));
            }
        }
    }
}

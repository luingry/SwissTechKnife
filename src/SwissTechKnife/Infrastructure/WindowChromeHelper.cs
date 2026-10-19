using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SwissTechKnife.Infrastructure
{
    /// <summary>
    /// Native window polish for the custom chrome: dark DWM frame, Win11 rounded corners, a border tinted
    /// to the canvas, and Snap Layouts on the custom maximize button (WM_NCHITTEST → HTMAXBUTTON).
    /// </summary>
    internal static class WindowChromeHelper
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWCP_ROUND = 2;

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCMOUSELEAVE = 0x02A2;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCLBUTTONUP = 0x00A2;
        private const int WM_NCMOUSEMOVE = 0x00A0;
        private const int HTMAXBUTTON = 9;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void ApplyFrame(IntPtr hwnd, byte r, byte g, byte b)
        {
            int dark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int corners = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
            int colorRef = r | (g << 8) | (b << 16);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
        }

        /// <summary>Routes the maximize button through the non-client area so Windows 11 shows Snap Layouts.</summary>
        public static void EnableSnapLayouts(Window window, FrameworkElement maximizeButton, Action<bool> setHover, Action click)
        {
            var source = (HwndSource)PresentationSource.FromVisual(window);
            bool pressed = false;
            source?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                switch (msg)
                {
                    case WM_NCHITTEST:
                        if (IsOver(maximizeButton, lParam))
                        {
                            setHover(true);
                            handled = true;
                            return new IntPtr(HTMAXBUTTON);
                        }
                        setHover(false);
                        break;
                    case WM_NCMOUSELEAVE:
                        setHover(false);
                        pressed = false;
                        break;
                    case WM_NCMOUSEMOVE:
                        if (wParam.ToInt32() != HTMAXBUTTON)
                        {
                            setHover(false);
                        }
                        break;
                    case WM_NCLBUTTONDOWN:
                        if (wParam.ToInt32() == HTMAXBUTTON)
                        {
                            pressed = true;
                            handled = true;
                        }
                        break;
                    case WM_NCLBUTTONUP:
                        if (wParam.ToInt32() == HTMAXBUTTON && pressed)
                        {
                            pressed = false;
                            handled = true;
                            click();
                        }
                        break;
                }
                return IntPtr.Zero;
            });
        }

        private static bool IsOver(FrameworkElement element, IntPtr lParam)
        {
            if (!element.IsVisible || element.ActualWidth <= 0)
            {
                return false;
            }
            long value = lParam.ToInt64();
            var screen = new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
            try
            {
                Point local = element.PointFromScreen(screen);
                return local.X >= 0 && local.Y >= 0 && local.X < element.ActualWidth && local.Y < element.ActualHeight;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}

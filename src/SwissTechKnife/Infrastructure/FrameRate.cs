using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;
using SwissTechKnife.Core;

namespace SwissTechKnife.Infrastructure
{
    /// <summary>
    /// WPF animates at 60 fps by default. On a display that is not 60 Hz (74, 120, 144…) frames then land on
    /// uneven vblanks (13 ms, 27 ms, 13 ms…), which reads as stutter. Match the animation rate to the display.
    /// Must run before any animation is created.
    /// </summary>
    internal static class FrameRate
    {
        private const int VREFRESH = 116;

        public static void MatchDisplay()
        {
            int rate = 60;
            IntPtr dc = GetDC(IntPtr.Zero);
            try
            {
                int refresh = GetDeviceCaps(dc, VREFRESH);
                if (refresh > 1)
                {
                    rate = Math.Max(60, Math.Min(240, refresh));
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }
            Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata(rate));
            Log.Info("Frames", "Animation frame rate " + rate + " fps");
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr dc, int index);
    }
}

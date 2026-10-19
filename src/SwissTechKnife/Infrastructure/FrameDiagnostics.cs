using System;
using System.Diagnostics;
using System.Windows.Media;
using SwissTechKnife.Core;

namespace SwissTechKnife.Infrastructure
{
    /// <summary>
    /// Diagnostics (STK_FRAMEDIAG=1): logs UI-thread frame gaps over 25 ms, i.e. visible animation hitches.
    /// Off by default; hooking CompositionTarget.Rendering keeps WPF rendering every frame.
    /// </summary>
    internal static class FrameDiagnostics
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static double _last;
        private static TranslateTransform _knob;
        private static ScaleTransform _scale;
        private static double _trackStart;

        private static void Track(object sender, System.Windows.RoutedEventArgs e)
        {
            var box = (System.Windows.Controls.CheckBox)sender;
            _knob = box.Template?.FindName("KnobMove", box) as TranslateTransform;
            _scale = box.Template?.FindName("KnobScale", box) as ScaleTransform;
            _trackStart = Clock.Elapsed.TotalMilliseconds;
            Log.Info("Frames", "toggle " + box.IsChecked);
        }

        public static void StartIfEnabled()
        {
            if (Environment.GetEnvironmentVariable("STK_FRAMEDIAG") != "1")
            {
                return;
            }
            Log.Info("Frames", "Frame diagnostics on; render tier " + (System.Windows.Media.RenderCapability.Tier >> 16));
            // Trace the knob of the switch that was just clicked, frame by frame.
            System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Controls.CheckBox), System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, new System.Windows.RoutedEventHandler(Track));
            System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Controls.CheckBox), System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, new System.Windows.RoutedEventHandler(Track));
            CompositionTarget.Rendering += (s, e) =>
            {
                double now = Clock.Elapsed.TotalMilliseconds;
                if (_knob != null && now - _trackStart < 450)
                {
                    Log.Info("Frames", $"knob t={now - _trackStart:0} x={_knob.X:0.0} scale={_scale?.ScaleX:0.00}");
                }
                double gap = now - _last;
                if (_last > 0 && gap > 25)
                {
                    Log.Warn("Frames", $"Frame gap {gap:0} ms at {now:0}");
                }
                _last = now;
            };
        }
    }
}

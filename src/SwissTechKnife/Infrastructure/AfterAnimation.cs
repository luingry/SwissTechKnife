using System;
using System.Windows.Threading;

namespace SwissTechKnife.Infrastructure
{
    /// <summary>
    /// Runs work after a control's state animation has finished. WPF animates on the UI thread, so disk writes,
    /// list refreshes or module start/stop at the moment a switch flips make the knob stutter.
    /// </summary>
    internal static class AfterAnimation
    {
        /// <summary>Slightly longer than the switch transition (220 ms).</summary>
        private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(260);

        public static void Run(Action action)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Delay };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                action();
            };
            timer.Start();
        }
    }
}

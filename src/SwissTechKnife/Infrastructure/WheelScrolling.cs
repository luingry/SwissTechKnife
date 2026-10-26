using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SwissTechKnife.Infrastructure
{
    /// <summary>
    /// WPF's ScrollViewer scrolls a fixed step for every wheel message and ignores MouseWheelEventArgs.Delta.
    /// Anything that sends fractional deltas (smooth-scrolling tools such as SmoothMice, high-resolution wheels,
    /// precision touchpads) therefore scrolls the full step per message: dozens of times too far and in jumps.
    /// This class scales the scroll with the delta (120 = one notch) in every ScrollViewer. Whole notches keep
    /// WPF's own behaviour.
    /// </summary>
    internal static class WheelScrolling
    {
        private const int WheelDelta = 120;
        // WPF (net48) scrolls this many device-independent pixels per notch in pixel mode, whatever WheelScrollLines is.
        private const double PixelsPerNotch = 48;
        private const int FallbackLines = 3;

        private static readonly ConditionalWeakTable<ScrollViewer, State> States = new ConditionalWeakTable<ScrollViewer, State>();
        private static bool _installed;

        public static void Install()
        {
            if (_installed)
            {
                return;
            }
            _installed = true;
            // Class handlers of the derived type run before ScrollViewer.OnMouseWheel, so marking the event handled
            // replaces WPF's fixed step. Bubbling reaches the innermost ScrollViewer first, as WPF does.
            EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.MouseWheelEvent, new MouseWheelEventHandler(OnMouseWheel));
        }

        private static void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var viewer = (ScrollViewer)sender;
            if (e.Handled || e.Delta == 0 || e.Delta % WheelDelta == 0)
            {
                return;
            }
            if (viewer.ScrollableHeight <= 0 || viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled)
            {
                return;
            }

            double notches = -e.Delta / (double)WheelDelta;
            State state = GetState(viewer);

            if (viewer.CanContentScroll)
            {
                int wheelLines = SystemParameters.WheelScrollLines;
                if (wheelLines < 0)
                {
                    // "One screen per notch": no meaningful fraction, keep WPF's page scrolling.
                    return;
                }
                ScrollByLines(viewer, state, notches * (wheelLines == 0 ? FallbackLines : wheelLines));
            }
            else
            {
                ScrollByPixels(viewer, state, notches * PixelsPerNotch);
            }
            e.Handled = true;
        }

        private static State GetState(ScrollViewer viewer)
        {
            State state;
            if (!States.TryGetValue(viewer, out state))
            {
                state = new State();
                States.Add(viewer, state);
                viewer.ScrollChanged += OnScrollChanged;
            }
            return state;
        }

        // Layout applied the requested offset (raised right after layout, before the next input message), so
        // VerticalOffset is current again and any older request is stale, e.g. after the Home key.
        private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            State state;
            if (States.TryGetValue((ScrollViewer)sender, out state))
            {
                state.PendingOffset = double.NaN;
            }
        }

        // Pixel offsets: follow the delta exactly. Several messages can arrive before the next layout pass, so
        // accumulate on the offset that was requested last instead of the (stale) VerticalOffset.
        private static void ScrollByPixels(ScrollViewer viewer, State state, double pixels)
        {
            double current = double.IsNaN(state.PendingOffset) ? viewer.VerticalOffset : state.PendingOffset;
            double target = Math.Max(0, Math.Min(viewer.ScrollableHeight, current + pixels));
            viewer.ScrollToVerticalOffset(target);
            state.PendingOffset = target;
        }
        // Logical (item) scrolling cannot move by a fraction of an item: carry the remainder to the next message.
        private static void ScrollByLines(ScrollViewer viewer, State state, double lines)
        {
            if (state.Remainder != 0 && Math.Sign(state.Remainder) != Math.Sign(lines))
            {
                state.Remainder = 0;
            }
            state.Remainder += lines;
            int whole = (int)state.Remainder;
            state.Remainder -= whole;
            for (int i = 0; i < Math.Abs(whole); i++)
            {
                if (whole > 0)
                {
                    viewer.LineDown();
                }
                else
                {
                    viewer.LineUp();
                }
            }
        }

        private sealed class State
        {
            public double PendingOffset = double.NaN;
            public double Remainder;
        }
    }
}

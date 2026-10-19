using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SwissTechKnife.Sdk
{
    /// <summary>
    /// Host for <c>Segmented.Item</c> radio buttons. Draws one shared indicator behind the checked item and
    /// slides it to the newly checked item instead of jumping. Resizes snap without animation.
    /// <code>
    /// &lt;Border Style="{StaticResource Segmented.Container}"&gt;
    ///     &lt;ui:SegmentedPanel&gt;
    ///         &lt;RadioButton Style="{StaticResource Segmented.Item}" GroupName="View" Content="A" IsChecked="True" /&gt;
    ///         &lt;RadioButton Style="{StaticResource Segmented.Item}" GroupName="View" Content="B" /&gt;
    ///     &lt;/ui:SegmentedPanel&gt;
    /// &lt;/Border&gt;
    /// </code>
    /// </summary>
    public sealed class SegmentedPanel : StackPanel
    {
        public static readonly DependencyProperty IndicatorBrushProperty = DependencyProperty.Register(
            nameof(IndicatorBrush), typeof(Brush), typeof(SegmentedPanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IndicatorRadiusProperty = DependencyProperty.Register(
            nameof(IndicatorRadius), typeof(double), typeof(SegmentedPanel), new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly DependencyProperty IndicatorXProperty = DependencyProperty.Register(
            "IndicatorX", typeof(double), typeof(SegmentedPanel), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly DependencyProperty IndicatorWidthProperty = DependencyProperty.Register(
            "IndicatorWidth", typeof(double), typeof(SegmentedPanel), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Duration SlideDuration = new Duration(TimeSpan.FromMilliseconds(320));
        private static readonly IEasingFunction SlideEase = CreateEase();
        private bool _placed;
        private bool _sliding;

        public SegmentedPanel()
        {
            Orientation = Orientation.Horizontal;
            AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((s, e) => Slide()));
            AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((s, e) => InvalidateVisual()));
        }

        public Brush IndicatorBrush
        {
            get => (Brush)GetValue(IndicatorBrushProperty);
            set => SetValue(IndicatorBrushProperty, value);
        }

        public double IndicatorRadius
        {
            get => (double)GetValue(IndicatorRadiusProperty);
            set => SetValue(IndicatorRadiusProperty, value);
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            Size size = base.ArrangeOverride(arrangeSize);
            // Layout changes (first show, resize, label change) snap; only a selection change slides.
            if (!_sliding && TryGetTarget(out double x, out double width))
            {
                Snap(x, width);
                _placed = true;
            }
            return size;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double width = (double)GetValue(IndicatorWidthProperty);
            if (IndicatorBrush == null || width <= 0 || CheckedChild() == null)
            {
                return;
            }
            double radius = Math.Min(IndicatorRadius, ActualHeight / 2);
            dc.DrawRoundedRectangle(IndicatorBrush, null, new Rect((double)GetValue(IndicatorXProperty), 0, width, ActualHeight), radius, radius);
        }

        private void Slide()
        {
            if (!TryGetTarget(out double x, out double width))
            {
                return;
            }
            if (!_placed || !IsVisible)
            {
                Snap(x, width);
                _placed = true;
                return;
            }
            _sliding = true;
            var move = new DoubleAnimation(x, SlideDuration) { EasingFunction = SlideEase };
            var grow = new DoubleAnimation(width, SlideDuration) { EasingFunction = SlideEase };
            move.Completed += (s, e) =>
            {
                _sliding = false;
                InvalidateArrange();
            };
            BeginAnimation(IndicatorXProperty, move);
            BeginAnimation(IndicatorWidthProperty, grow);
        }

        private void Snap(double x, double width)
        {
            BeginAnimation(IndicatorXProperty, null);
            BeginAnimation(IndicatorWidthProperty, null);
            SetValue(IndicatorXProperty, x);
            SetValue(IndicatorWidthProperty, width);
        }

        private bool TryGetTarget(out double x, out double width)
        {
            x = 0;
            width = 0;
            UIElement item = CheckedChild();
            if (item == null || !(item is FrameworkElement element) || element.ActualWidth <= 0)
            {
                return false;
            }
            x = element.TranslatePoint(new Point(0, 0), this).X;
            width = element.ActualWidth;
            return true;
        }

        private UIElement CheckedChild()
        {
            foreach (UIElement child in InternalChildren)
            {
                if (child is ToggleButton toggle && toggle.IsChecked == true && toggle.Visibility == Visibility.Visible)
                {
                    return child;
                }
            }
            return null;
        }

        private static IEasingFunction CreateEase()
        {
            // In-out keeps the travel visible; an aggressive ease-out covers most of the distance in the first frames.
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            ease.Freeze();
            return ease;
        }
    }
}

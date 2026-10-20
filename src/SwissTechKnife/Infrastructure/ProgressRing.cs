using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SwissTechKnife.Infrastructure
{
    /// <summary>
    /// Circular progress drawn around an icon: a soft track plus an arc for <see cref="Value"/> (0–1), or a spinning
    /// quarter arc while <see cref="IsIndeterminate"/>. Value changes ease in; the spin is a render-transform rotation.
    /// </summary>
    internal sealed class ProgressRing : FrameworkElement
    {
        private static readonly Duration ValueDuration = new Duration(TimeSpan.FromMilliseconds(200));
        private static readonly IEasingFunction Ease = CreateEase();
        private readonly RotateTransform _spin = new RotateTransform();

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(ProgressRing), new PropertyMetadata(0.0, OnValueChanged));

        public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
            nameof(IsIndeterminate), typeof(bool), typeof(ProgressRing), new PropertyMetadata(false, OnIndeterminateChanged));

        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            nameof(Stroke), typeof(Brush), typeof(ProgressRing), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
            nameof(Track), typeof(Brush), typeof(ProgressRing), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
            nameof(Thickness), typeof(double), typeof(ProgressRing), new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>The drawn fraction; animated toward <see cref="Value"/>.</summary>
        private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
            "Shown", typeof(double), typeof(ProgressRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public ProgressRing()
        {
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = _spin;
            IsHitTestVisible = false;
            SnapsToDevicePixels = false;
            IsVisibleChanged += (s, e) => UpdateSpin();
        }

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public bool IsIndeterminate
        {
            get => (bool)GetValue(IsIndeterminateProperty);
            set => SetValue(IsIndeterminateProperty, value);
        }

        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public Brush Track
        {
            get => (Brush)GetValue(TrackProperty);
            set => SetValue(TrackProperty, value);
        }

        public double Thickness
        {
            get => (double)GetValue(ThicknessProperty);
            set => SetValue(ThicknessProperty, value);
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ring = (ProgressRing)d;
            double to = Math.Max(0, Math.Min(1, (double)e.NewValue));
            // Going back (a new attempt) jumps; going forward eases.
            if (to < (double)ring.GetValue(ShownProperty))
            {
                ring.BeginAnimation(ShownProperty, null);
                ring.SetValue(ShownProperty, to);
                return;
            }
            ring.BeginAnimation(ShownProperty, new DoubleAnimation(to, ValueDuration) { EasingFunction = Ease }, HandoffBehavior.SnapshotAndReplace);
        }

        private static void OnIndeterminateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ring = (ProgressRing)d;
            ring.UpdateSpin();
            ring.InvalidateVisual();
        }

        private void UpdateSpin()
        {
            if (IsIndeterminate && IsVisible)
            {
                _spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.9)))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                });
            }
            else
            {
                // Stopped while hidden too: no frames are spent on a ring nobody sees.
                _spin.BeginAnimation(RotateTransform.AngleProperty, null);
                _spin.Angle = 0;
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double thickness = Thickness;
            double radius = (Math.Min(ActualWidth, ActualHeight) - thickness) / 2;
            if (radius <= 0)
            {
                return;
            }
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            if (Track != null)
            {
                dc.DrawEllipse(null, CreatePen(Track, thickness), center, radius, radius);
            }
            double fraction = IsIndeterminate ? 0.25 : (double)GetValue(ShownProperty);
            if (Stroke == null || fraction <= 0)
            {
                return;
            }
            if (fraction >= 0.999)
            {
                dc.DrawEllipse(null, CreatePen(Stroke, thickness), center, radius, radius);
                return;
            }
            // Clockwise from 12 o'clock.
            double angle = fraction * 2 * Math.PI;
            var start = new Point(center.X, center.Y - radius);
            var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();
            dc.DrawGeometry(null, CreatePen(Stroke, thickness), geometry);
        }

        private static Pen CreatePen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            return pen;
        }

        private static IEasingFunction CreateEase()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ease.Freeze();
            return ease;
        }
    }
}

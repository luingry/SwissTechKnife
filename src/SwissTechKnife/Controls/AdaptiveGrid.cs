using System;
using System.Windows;
using System.Windows.Controls;

namespace SwissTechKnife.Controls
{
    /// <summary>
    /// Responsive card grid: as many equal columns as fit <see cref="MinColumnWidth"/>, rows as tall as
    /// their tallest card. Cheaper than nested WrapPanels and keeps cards aligned edge to edge.
    /// </summary>
    public sealed class AdaptiveGrid : Panel
    {
        public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
            nameof(MinColumnWidth), typeof(double), typeof(AdaptiveGrid), new FrameworkPropertyMetadata(280.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
            nameof(Gap), typeof(double), typeof(AdaptiveGrid), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double MinColumnWidth
        {
            get => (double)GetValue(MinColumnWidthProperty);
            set => SetValue(MinColumnWidthProperty, value);
        }

        public double Gap
        {
            get => (double)GetValue(GapProperty);
            set => SetValue(GapProperty, value);
        }

        private int Columns(double width)
        {
            if (double.IsInfinity(width) || width <= 0)
            {
                return 1;
            }
            return Math.Max(1, (int)Math.Floor((width + Gap) / (MinColumnWidth + Gap)));
        }

        private double ColumnWidth(double width, int columns) => Math.Max(0, (width - Gap * (columns - 1)) / columns);

        protected override Size MeasureOverride(Size available)
        {
            double width = double.IsInfinity(available.Width) ? MinColumnWidth : available.Width;
            int columns = Columns(width);
            double columnWidth = ColumnWidth(width, columns);
            double height = 0;
            double rowHeight = 0;
            int index = 0;
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(columnWidth, double.PositiveInfinity));
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
                if (++index % columns == 0)
                {
                    height += rowHeight + Gap;
                    rowHeight = 0;
                }
            }
            height += rowHeight;
            if (index > 0 && index % columns == 0)
            {
                height -= Gap;
            }
            return new Size(width, Math.Max(0, height));
        }

        protected override Size ArrangeOverride(Size final)
        {
            int columns = Columns(final.Width);
            double columnWidth = ColumnWidth(final.Width, columns);
            var visible = new System.Collections.Generic.List<UIElement>(InternalChildren.Count);
            foreach (UIElement child in InternalChildren)
            {
                if (child.Visibility != Visibility.Collapsed)
                {
                    visible.Add(child);
                }
            }
            double y = 0;
            for (int start = 0; start < visible.Count; start += columns)
            {
                double rowHeight = 0;
                for (int i = start; i < Math.Min(start + columns, visible.Count); i++)
                {
                    rowHeight = Math.Max(rowHeight, visible[i].DesiredSize.Height);
                }
                for (int i = start; i < Math.Min(start + columns, visible.Count); i++)
                {
                    double x = (i - start) * (columnWidth + Gap);
                    visible[i].Arrange(new Rect(x, y, columnWidth, rowHeight));
                }
                y += rowHeight + Gap;
            }
            return final;
        }
    }
}

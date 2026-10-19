using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SwissTechKnife.Infrastructure
{
    /// <summary>bool → Visibility. ConverterParameter "invert" flips it.</summary>
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;
            if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
            {
                flag = !flag;
            }
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>null/empty → Collapsed. ConverterParameter "invert" flips it.</summary>
    public sealed class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool present = value != null && !(value is string s && s.Length == 0);
            if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
            {
                present = !present;
            }
            return present ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Progress 0..1 → "45%".</summary>
    public sealed class PercentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is double d ? Math.Round(d * 100).ToString(culture) + "%" : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

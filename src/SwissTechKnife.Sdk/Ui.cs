using System.Windows;
using System.Windows.Media;

namespace SwissTechKnife.Sdk
{
    /// <summary>
    /// Attached properties read by the design-system control templates.
    /// XAML: <c>xmlns:ui="clr-namespace:SwissTechKnife.Sdk;assembly=SwissTechKnife.Sdk"</c>, then
    /// <c>&lt;Button ui:Ui.Icon="&amp;#xE896;" Content="Instalar" Style="{StaticResource Button.Primary}" /&gt;</c>.
    /// </summary>
    public static class Ui
    {
        /// <summary>Glyph from the icon font (Segoe Fluent Icons) shown before the content.</summary>
        public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
            "Icon", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static string GetIcon(DependencyObject element) => (string)element.GetValue(IconProperty);

        public static void SetIcon(DependencyObject element, string value) => element.SetValue(IconProperty, value);

        public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
            "CornerRadius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(new CornerRadius(10)));

        public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);

        public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);

        /// <summary>Layer faded in on hover, above the background.</summary>
        public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.RegisterAttached(
            "HoverBackground", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));

        public static Brush GetHoverBackground(DependencyObject element) => (Brush)element.GetValue(HoverBackgroundProperty);

        public static void SetHoverBackground(DependencyObject element, Brush value) => element.SetValue(HoverBackgroundProperty, value);

        /// <summary>Layer faded in while pressed, above the hover layer.</summary>
        public static readonly DependencyProperty PressedBackgroundProperty = DependencyProperty.RegisterAttached(
            "PressedBackground", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));

        public static Brush GetPressedBackground(DependencyObject element) => (Brush)element.GetValue(PressedBackgroundProperty);

        public static void SetPressedBackground(DependencyObject element, Brush value) => element.SetValue(PressedBackgroundProperty, value);

        /// <summary>Hint text shown by inputs while they are empty.</summary>
        public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
            "Placeholder", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));

        public static string GetPlaceholder(DependencyObject element) => (string)element.GetValue(PlaceholderProperty);

        public static void SetPlaceholder(DependencyObject element, string value) => element.SetValue(PlaceholderProperty, value);
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SwissTechKnife.ViewModels;

namespace SwissTechKnife.Views
{
    /// <summary>Confirmation before SwissTechKnife updates itself: release notes, then Cancelar / Atualizar.</summary>
    public partial class HostUpdateDialog : UserControl
    {
        private IInputElement _returnFocus;
        private int _generation;

        public HostUpdateDialog()
        {
            InitializeComponent();
        }

        public void Open()
        {
            _generation++;
            _returnFocus = Keyboard.FocusedElement;
            Visibility = Visibility.Visible;
            Animate(show: true, onDone: null);
            Dispatcher.BeginInvoke(new Action(() => CancelButton.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }

        public void Close()
        {
            int generation = ++_generation;
            Animate(show: false, onDone: () =>
            {
                if (generation == _generation)
                {
                    Visibility = Visibility.Collapsed;
                }
            });
            if (_returnFocus is UIElement element && element.IsVisible)
            {
                element.Focus();
            }
        }

        private void Animate(bool show, Action onDone)
        {
            var ease = (IEasingFunction)FindResource(show ? "Ease.Emphasized" : "Ease.Out");
            Duration duration = show ? (Duration)FindResource("Motion.Slow") : (Duration)FindResource("Motion.Normal");
            var fade = new DoubleAnimation(show ? 1 : 0, duration) { EasingFunction = ease };
            if (onDone != null)
            {
                fade.Completed += (s, e) => onDone();
            }
            Scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, duration));
            Card.BeginAnimation(OpacityProperty, fade);
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(show ? 1 : 0.97, duration) { EasingFunction = ease });
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(show ? 1 : 0.97, duration) { EasingFunction = ease });
            CardMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(show ? 0 : 6, duration) { EasingFunction = ease });
        }

        private void OnScrimClick(object sender, MouseButtonEventArgs e) => (DataContext as HostUpdateViewModel)?.CancelCommand.Execute(null);
    }
}

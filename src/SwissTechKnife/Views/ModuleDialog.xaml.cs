using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SwissTechKnife.ViewModels;

namespace SwissTechKnife.Views
{
    /// <summary>
    /// Module details popup. The card keeps showing the last module while it fades out, so closing never
    /// flashes an empty dialog.
    /// </summary>
    public partial class ModuleDialog : UserControl
    {
        private IInputElement _returnFocus;
        private int _generation;

        public ModuleDialog()
        {
            InitializeComponent();
        }

        private ShellViewModel Shell => (ShellViewModel)Window.GetWindow(this)?.DataContext;

        public void Open(ModuleItem item)
        {
            _generation++;
            Card.DataContext = item;
            _returnFocus = Keyboard.FocusedElement;
            Visibility = Visibility.Visible;
            Animate(show: true, onDone: null);
            Dispatcher.BeginInvoke(new Action(() => CloseButton.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }

        public void Close()
        {
            int generation = ++_generation;
            Animate(show: false, onDone: () =>
            {
                if (generation == _generation)
                {
                    Visibility = Visibility.Collapsed;
                    Card.DataContext = null;
                }
            });
            if (_returnFocus is UIElement element && element.IsVisible)
            {
                element.Focus();
            }
        }

        /// <summary>Tab switch: the new tab content fades and rises in.</summary>
        public void AnimateTab()
        {
            var ease = (IEasingFunction)FindResource("Ease.Emphasized");
            var duration = (Duration)FindResource("Motion.Normal");
            TabBody.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            TabMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, duration) { EasingFunction = ease });
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

        /// <summary>Update confirmation popup over the details card.</summary>
        public void OpenConfirm(ModuleItem item)
        {
            ConfirmCard.DataContext = item;
            ConfirmLayer.Visibility = Visibility.Visible;
            AnimateConfirm(show: true, onDone: null);
            Dispatcher.BeginInvoke(new Action(() => ConfirmCancel.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }

        public void CloseConfirm()
        {
            int generation = ++_confirmGeneration;
            AnimateConfirm(show: false, onDone: () =>
            {
                if (generation == _confirmGeneration)
                {
                    ConfirmLayer.Visibility = Visibility.Collapsed;
                    ConfirmCard.DataContext = null;
                }
            });
        }

        private int _confirmGeneration;

        private void AnimateConfirm(bool show, Action onDone)
        {
            if (show)
            {
                _confirmGeneration++;
            }
            var ease = (IEasingFunction)FindResource(show ? "Ease.Emphasized" : "Ease.Out");
            Duration duration = show ? (Duration)FindResource("Motion.Slow") : (Duration)FindResource("Motion.Normal");
            var fade = new DoubleAnimation(show ? 1 : 0, duration) { EasingFunction = ease };
            if (onDone != null)
            {
                fade.Completed += (s, e) => onDone();
            }
            ConfirmScrim.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, duration));
            ConfirmCard.BeginAnimation(OpacityProperty, fade);
            ConfirmScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(show ? 1 : 0.97, duration) { EasingFunction = ease });
            ConfirmScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(show ? 1 : 0.97, duration) { EasingFunction = ease });
            ConfirmMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(show ? 0 : 6, duration) { EasingFunction = ease });
        }

        private void OnConfirmScrimClick(object sender, MouseButtonEventArgs e) => Shell?.CancelUpdateCommand.Execute(null);

        private void OnScrimClick(object sender, MouseButtonEventArgs e) => Dismiss();

        private void OnCloseClick(object sender, RoutedEventArgs e) => Dismiss();

        private void Dismiss()
        {
            ShellViewModel shell = Shell;
            if (shell != null)
            {
                shell.SelectedModule = null;
            }
        }
    }
}

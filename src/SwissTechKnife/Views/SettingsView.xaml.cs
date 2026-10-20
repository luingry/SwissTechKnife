using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SwissTechKnife.ViewModels;

namespace SwissTechKnife.Views
{
    public partial class SettingsView : UserControl
    {
        private HostUpdateViewModel _hostUpdate;
        private int _noticeGeneration;
        private bool _noticeShown;

        public SettingsView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) => Attach((e.NewValue as SettingsViewModel)?.HostUpdate);
        }

        private void Attach(HostUpdateViewModel hostUpdate)
        {
            if (_hostUpdate != null)
            {
                _hostUpdate.PropertyChanged -= OnHostUpdateChanged;
            }
            _hostUpdate = hostUpdate;
            if (_hostUpdate != null)
            {
                _hostUpdate.PropertyChanged += OnHostUpdateChanged;
                AnimateNotice(_hostUpdate.ShowNotice);
            }
        }

        private void OnHostUpdateChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(HostUpdateViewModel.ShowNotice))
            {
                AnimateNotice(_hostUpdate.ShowNotice);
            }
        }

        /// <summary>The update notice fades and rises in, and fades out before collapsing.</summary>
        private void AnimateNotice(bool show)
        {
            if (show == _noticeShown)
            {
                return;
            }
            _noticeShown = show;
            int generation = ++_noticeGeneration;
            var ease = (IEasingFunction)FindResource(show ? "Ease.Emphasized" : "Ease.Out");
            var duration = (Duration)FindResource(show ? "Motion.Slow" : "Motion.Normal");
            var fade = new DoubleAnimation(show ? 1 : 0, duration) { EasingFunction = ease };
            if (show)
            {
                HostUpdateNotice.Visibility = Visibility.Visible;
            }
            else
            {
                fade.Completed += (s, e) =>
                {
                    if (generation == _noticeGeneration)
                    {
                        HostUpdateNotice.Visibility = Visibility.Collapsed;
                    }
                };
            }
            HostUpdateNotice.BeginAnimation(OpacityProperty, fade);
            HostUpdateMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(show ? 0 : 6, duration) { EasingFunction = ease });
        }
    }
}

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SwissTechKnife.Core;
using SwissTechKnife.Infrastructure;
using SwissTechKnife.Sdk;
using SwissTechKnife.ViewModels;

namespace SwissTechKnife.Views
{
    public partial class MainWindow : Window
    {
        private readonly ShellViewModel _shell;
        private readonly SettingsStore _settings;
        private bool _maximizeHot;
        private bool _exiting;

        public MainWindow(ShellViewModel shell, SettingsStore settings)
        {
            InitializeComponent();
            _shell = shell;
            _settings = settings;
            DataContext = shell;
            RestorePlacement();
            shell.PropertyChanged += OnShellPropertyChanged;
            shell.Settings.HostUpdate.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(HostUpdateViewModel.IsConfirmOpen))
                {
                    if (shell.Settings.HostUpdate.IsConfirmOpen)
                    {
                        HostDialog.Open();
                    }
                    else
                    {
                        HostDialog.Close();
                    }
                }
            };
            SourceInitialized += OnSourceInitialized;
            StateChanged += (s, e) => UpdateMaximizedState();
            Closing += OnClosing;
            PreviewKeyDown += OnPreviewKeyDown;
            UpdateMaximizedState();
        }

        /// <summary>Real exit (tray "Sair"), bypassing hide-to-tray.</summary>
        /// <summary>
        /// Asked on close: returns true to hide in the notification area instead of exiting (the setting is on,
        /// or a background module needs the process). Set by the app; null means exit.
        /// </summary>
        public Func<bool> HideOnClose { get; set; }

        public void ExitApplication()
        {
            _exiting = true;
            Close();
        }

        public void BringToFront()
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Show();
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            Color canvas = (Color)FindResource("Color.Canvas");
            WindowChromeHelper.ApplyFrame(hwnd, canvas.R, canvas.G, canvas.B);
            WindowChromeHelper.EnableSnapLayouts(this, MaximizeButton, SetMaximizeHot, ToggleMaximize);
        }

        private void SetMaximizeHot(bool hot)
        {
            if (_maximizeHot == hot)
            {
                return;
            }
            _maximizeHot = hot;
            VisualStateManager.GoToState(MaximizeButton, hot ? "MouseOver" : "Normal", useTransitions: true);
        }

        private void OnShellPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ShellViewModel.IsStoreActive):
                case nameof(ShellViewModel.IsSettingsActive):
                case nameof(ShellViewModel.CurrentPage):
                    AnimatePageIn();
                    break;
                case nameof(ShellViewModel.DialogTab):
                    if (_shell.IsDialogOpen)
                    {
                        Dialog.AnimateTab();
                    }
                    break;
                case nameof(ShellViewModel.IsUpdateConfirmOpen):
                    if (_shell.IsUpdateConfirmOpen)
                    {
                        Dialog.OpenConfirm(_shell.PendingUpdate);
                    }
                    else
                    {
                        Dialog.CloseConfirm();
                    }
                    break;
                case nameof(ShellViewModel.IsDialogOpen):
                    if (_shell.IsDialogOpen)
                    {
                        Dialog.Open(_shell.SelectedModule);
                    }
                    else
                    {
                        Dialog.Close();
                    }
                    break;
            }
        }

        /// <summary>Page change: quick fade + rise, cheap enough to run on every navigation.</summary>
        private void AnimatePageIn()
        {
            var ease = (IEasingFunction)FindResource("Ease.Emphasized");
            var duration = (Duration)FindResource("Motion.Slow");
            PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            PageMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, duration) { EasingFunction = ease });
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _shell.Settings.HostUpdate.IsConfirmOpen)
            {
                _shell.Settings.HostUpdate.IsConfirmOpen = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _shell.IsDialogOpen)
            {
                _shell.CloseTopOverlay();
                e.Handled = true;
            }
        }

        private void UpdateMaximizedState()
        {
            bool maximized = WindowState == WindowState.Maximized;
            // A maximized WindowChrome window overhangs the monitor by the resize frame; pad it back in.
            Thickness frame = SystemParameters.WindowResizeBorderThickness;
            RootGrid.Margin = maximized ? new Thickness(frame.Left + 4, frame.Top + 4, frame.Right + 4, frame.Bottom + 4) : new Thickness(0);
            Ui.SetIcon(MaximizeButton, maximized ? "" : "");
            MaximizeButton.ToolTip = Strings.Get(maximized ? "Restore" : "Maximize");
        }

        private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnMaximize(object sender, RoutedEventArgs e) => ToggleMaximize();

        private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private void RestorePlacement()
        {
            WindowPlacement placement = _settings.Current.Window;
            if (placement == null || placement.Width < MinWidth || placement.Height < MinHeight)
            {
                return;
            }
            var bounds = new Rect(placement.Left, placement.Top, placement.Width, placement.Height);
            var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (!desktop.IntersectsWith(bounds))
            {
                return;
            }
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            if (placement.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (!_exiting && HideOnClose != null && HideOnClose())
            {
                // Keep running in the notification area; modules keep working.
                e.Cancel = true;
                Hide();
                return;
            }
            Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _settings.Current.Window = new WindowPlacement
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Maximized = WindowState == WindowState.Maximized,
            };
        }
    }
}

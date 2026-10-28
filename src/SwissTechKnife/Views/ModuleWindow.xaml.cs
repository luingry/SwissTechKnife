using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SwissTechKnife.Core;
using SwissTechKnife.Infrastructure;
using SwissTechKnife.Sdk;
using SwissTechKnife.ViewModels;

namespace SwissTechKnife.Views
{
    /// <summary>
    /// A module's page in a window of its own, without the shell around it. The page element is the same one
    /// the shell shows (the host keeps one per module), so it lives in one place at a time: see <see cref="Detach"/>.
    /// </summary>
    public partial class ModuleWindow : Window
    {
        private bool _maximizeHot;

        public ModuleWindow(ModuleItem item, FrameworkElement page)
        {
            InitializeComponent();
            Item = item;
            DataContext = item;
            Page.Child = page;
            SourceInitialized += OnSourceInitialized;
            StateChanged += (s, e) => UpdateMaximizedState();
            Loaded += (s, e) => AnimatePageIn();
            Closed += (s, e) => Detach();
            UpdateMaximizedState();
        }

        public ModuleItem Item { get; }

        public FrameworkElement PageElement => Page.Child as FrameworkElement;

        /// <summary>Gives the page back (to the shell); the window then has nothing left to show.</summary>
        public void Detach()
        {
            if (Page.Child is FrameworkElement page)
            {
                Page.Child = null;
                try
                {
                    Lifecycle(page)?.OnViewHidden();
                }
                catch (Exception ex)
                {
                    Log.Error("ModuleWindow", "Module view lifecycle callback failed", ex);
                }
            }
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

        /// <summary>The page itself, or its view model, may opt into show/hide callbacks.</summary>
        internal static IModuleViewLifecycle Lifecycle(FrameworkElement page) => page as IModuleViewLifecycle ?? page?.DataContext as IModuleViewLifecycle;

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

        private void AnimatePageIn()
        {
            var ease = (IEasingFunction)FindResource("Ease.Emphasized");
            var duration = (Duration)FindResource("Motion.Slow");
            PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            PageMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, duration) { EasingFunction = ease });
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
    }
}

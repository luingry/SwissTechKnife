using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using SwissTechKnife.Sdk;
using Forms = System.Windows.Forms;

namespace SwissTechKnife.Infrastructure
{
    /// <summary>
    /// Notification-area icon, created only while "manter na área de notificação" is on.
    /// The shell's NotifyIcon is native, but its menu is the design-system WPF ContextMenu.
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly Forms.NotifyIcon _icon;
        private readonly ContextMenu _menu;
        private readonly Action _open;

        public TrayIcon(Action open, Action exit)
        {
            _open = open;
            _menu = new ContextMenu();
            _menu.Items.Add(CreateItem("Abrir SwissTechKnife", "", open));
            _menu.Items.Add(new Separator());
            _menu.Items.Add(CreateItem("Sair", "", exit));

            _icon = new Forms.NotifyIcon
            {
                Text = "SwissTechKnife",
                Icon = LoadIcon(),
                Visible = true,
            };
            _icon.MouseClick += OnMouseClick;
        }

        /// <summary>One-time native notification from the icon (e.g. why the app is still running).</summary>
        public void ShowHint(string message)
        {
            _icon.BalloonTipTitle = "SwissTechKnife";
            _icon.BalloonTipText = message;
            _icon.BalloonTipIcon = Forms.ToolTipIcon.None;
            _icon.ShowBalloonTip(4000);
        }

        public void Dispose()
        {
            _menu.IsOpen = false;
            _icon.Visible = false;
            _icon.Icon?.Dispose();
            _icon.Dispose();
        }

        private void OnMouseClick(object sender, Forms.MouseEventArgs e)
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                _open();
            }
            else if (e.Button == Forms.MouseButtons.Right)
            {
                _menu.Placement = PlacementMode.MousePoint;
                _menu.IsOpen = true;
                // Without foreground activation the menu would not close when clicking elsewhere.
                if (PresentationSource.FromVisual(_menu) is HwndSource source)
                {
                    SetForegroundWindow(source.Handle);
                }
            }
        }

        private static MenuItem CreateItem(string header, string glyph, Action action)
        {
            var item = new MenuItem { Header = header };
            Ui.SetIcon(item, glyph);
            item.Click += (s, e) => action();
            return item;
        }

        private static System.Drawing.Icon LoadIcon()
        {
            var uri = new Uri("pack://application:,,,/SwissTechKnife;component/Assets/swisstechknife.ico");
            using (var stream = Application.GetResourceStream(uri).Stream)
            {
                return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);
    }
}

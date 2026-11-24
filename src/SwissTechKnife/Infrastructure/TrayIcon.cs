using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using SwissTechKnife.Sdk;
using Forms = System.Windows.Forms;

namespace SwissTechKnife.Infrastructure
{
    /// <summary>
    /// Notification-area icon, shown for the whole life of the process (created after first paint).
    /// The shell's NotifyIcon is native, but its menu is the design-system WPF ContextMenu, built each time it opens
    /// so it can carry the modules' entries when their own icons are hidden.
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly Forms.NotifyIcon _icon;
        private readonly ContextMenu _menu = new ContextMenu();
        private readonly Action _open;
        private readonly Action _exit;
        private readonly Func<IReadOnlyList<Control>> _moduleItems;

        public TrayIcon(Action open, Action exit, Func<IReadOnlyList<Control>> moduleItems)
        {
            _open = open;
            _exit = exit;
            _moduleItems = moduleItems;

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

        /// <summary>The module list changed: an open menu is rebuilt in place (e.g. a module moved between active and inactive).</summary>
        public void RefreshMenu()
        {
            if (_menu.IsOpen)
            {
                Populate();
            }
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
                Populate();
                ShowMenu(_menu);
            }
        }

        private void Populate()
        {
            _menu.Items.Clear();
            _menu.Items.Add(CreateItem("TrayOpen", "", _open));
            IReadOnlyList<Control> modules = _moduleItems?.Invoke();
            if (modules != null && modules.Count > 0)
            {
                _menu.Items.Add(new Separator());
                foreach (Control item in modules)
                {
                    _menu.Items.Add(item);
                }
            }
            _menu.Items.Add(new Separator());
            _menu.Items.Add(CreateItem("TrayExit", "", _exit));
        }

        /// <summary>Opens a design-system menu at the cursor for a notification-area icon.</summary>
        internal static void ShowMenu(ContextMenu menu)
        {
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            // Without foreground activation the menu would not close when clicking elsewhere.
            if (PresentationSource.FromVisual(menu) is HwndSource source)
            {
                SetForegroundWindow(source.Handle);
            }
        }

        /// <summary>The header is bound, so the open menu follows a language change.</summary>
        internal static MenuItem CreateItem(string key, string glyph, Action action)
        {
            var item = new MenuItem();
            item.SetBinding(HeaderedItemsControl.HeaderProperty, new Binding("[" + key + "]") { Source = LocalizedStrings.Instance, Mode = BindingMode.OneWay });
            Ui.SetIcon(item, glyph);
            if (action != null)
            {
                item.Click += (s, e) => action();
            }
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

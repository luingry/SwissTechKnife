using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SwissTechKnife.Core;
using SwissTechKnife.Infrastructure;
using SwissTechKnife.Sdk;
using SwissTechKnife.ViewModels;
using Forms = System.Windows.Forms;

namespace SwissTechKnife.Modules
{
    /// <summary>
    /// Notification-area icons of enabled modules whose "Show icon in the notification area" switch is on.
    /// Each icon opens the module on left click (double click opens its <see cref="IModuleWindow"/>, if it declares one); its menu holds "Open &lt;module&gt;" plus the module's own
    /// <see cref="IModuleTrayMenu"/> items, never Exit: the module's enable switch decides whether it runs.
    /// When the host setting hides module icons, none is shown and <see cref="CreateMenuSection"/> lists every installed
    /// module for the host icon's menu instead.
    /// </summary>
    internal sealed class ModuleTrayIcons : IDisposable
    {
        private const string Source = "ModuleTray";
        private readonly ModuleManager _manager;
        private readonly Action<string> _openModule;
        private readonly Action<string> _showDetails;
        private readonly Func<bool> _iconsHidden;
        private readonly Dictionary<string, Entry> _icons = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public ModuleTrayIcons(ModuleManager manager, Action<string> openModule, Action<string> showDetails, Func<bool> iconsHidden)
        {
            _manager = manager;
            _openModule = openModule;
            _showDetails = showDetails;
            _iconsHidden = iconsHidden;
            _manager.Changed += OnChanged;
            Sync();
        }

        /// <summary>Raised after the module list changed, so an open host menu can rebuild its module section.</summary>
        public event EventHandler MenuChanged;

        /// <summary>
        /// True while at least one enabled module asks for its icon: closing the window must keep the host alive.
        /// With module icons hidden, its entry lives in the host icon's menu, which needs the host just the same.
        /// </summary>
        public bool Any => _manager.Items.Any(i => i.IsEnabled && i.ShowTrayIcon);

        /// <summary>The host setting changed: shows or removes the icons accordingly.</summary>
        public void Refresh()
        {
            Sync();
            MenuChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Entries for the host icon's menu while module icons are hidden: installed modules grouped into active and
        /// inactive, each with its icon, a click that opens it and a switch that enables or disables it. Empty otherwise.
        /// </summary>
        public IReadOnlyList<Control> CreateMenuSection()
        {
            var items = new List<Control>();
            if (!_iconsHidden())
            {
                return items;
            }
            List<ModuleItem> installed = _manager.Items.Where(i => i.IsInstalled).ToList();
            AddGroup(items, "TrayActiveModules", installed.Where(i => i.IsEnabled));
            AddGroup(items, "TrayInactiveModules", installed.Where(i => !i.IsEnabled));
            return items;
        }

        private void AddGroup(List<Control> items, string captionKey, IEnumerable<ModuleItem> modules)
        {
            bool first = true;
            foreach (ModuleItem module in modules)
            {
                if (first)
                {
                    MenuItem caption = TrayIcon.CreateItem(captionKey, null, null);
                    caption.Style = (Style)Application.Current.FindResource("TrayMenu.Group");
                    items.Add(caption);
                    first = false;
                }
                items.Add(CreateModuleRow(module));
            }
        }

        private MenuItem CreateModuleRow(ModuleItem module)
        {
            var row = new MenuItem
            {
                Header = module.Name ?? module.Id,
                DataContext = module,
                Tag = Strings.Format("TrayToggleModule", module.Name ?? module.Id),
                Style = (Style)Application.Current.FindResource("TrayMenu.Module"),
            };
            System.Windows.Automation.AutomationProperties.SetName(row, module.Name ?? module.Id);
            row.Click += (s, e) =>
            {
                if (module.IsEnabled)
                {
                    ShowWindow(module);
                }
                else
                {
                    _showDetails(module.Id);
                }
            };
            return row;
        }

        public void Dispose()
        {
            _manager.Changed -= OnChanged;
            foreach (Entry entry in _icons.Values)
            {
                entry.Dispose();
            }
            _icons.Clear();
        }

        private void OnChanged(object sender, EventArgs e) => Refresh();

        private void Sync()
        {
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            if (!_iconsHidden())
            {
                wanted.UnionWith(_manager.Items.Where(i => i.IsEnabled && i.ShowTrayIcon).Select(i => i.Id));
            }
            foreach (string id in _icons.Keys.Where(id => !wanted.Contains(id)).ToList())
            {
                _icons[id].Dispose();
                _icons.Remove(id);
            }
            foreach (string id in wanted.Where(id => !_icons.ContainsKey(id)))
            {
                try
                {
                    _icons[id] = new Entry(_manager.Find(id), this);
                }
                catch (Exception ex)
                {
                    Log.Error(Source, "Could not show the icon of " + id, ex);
                }
            }
        }

        private void ShowMenu(ModuleItem item)
        {
            var menu = new ContextMenu();
            var open = new MenuItem { Header = Strings.Format("TrayOpenModule", item.Name) };
            Ui.SetIcon(open, "");
            open.Click += (s, e) => _openModule(item.Id);
            menu.Items.Add(open);

            // Initialize is cheap by contract, so an on-demand module is created to ask for its commands.
            if (_manager.TryActivate(item) && _manager.Runtime.Instance(item.Id) is IModuleTrayMenu provider)
            {
                try
                {
                    List<Control> extra = provider.CreateTrayMenuItems()?.Where(c => c != null).ToList() ?? new List<Control>();
                    if (extra.Count > 0)
                    {
                        menu.Items.Add(new Separator());
                        extra.ForEach(c => menu.Items.Add(c));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(Source, "CreateTrayMenuItems failed for " + item.Id, ex);
                }
            }
            TrayIcon.ShowMenu(menu);
        }

        /// <summary>Opens the module's dedicated window; falls back to its page in the host when it has none.</summary>
        private void ShowWindow(ModuleItem item)
        {
            if (_manager.TryActivate(item) && _manager.Runtime.Instance(item.Id) is IModuleWindow window)
            {
                try
                {
                    window.ShowWindow();
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error(Source, "ShowWindow failed for " + item.Id, ex);
                }
            }
            _openModule(item.Id);
        }

        /// <summary>One module's icon; follows the module's name and icon image as they change.</summary>
        private sealed class Entry : IDisposable
        {
            private readonly ModuleItem _item;
            private readonly ModuleTrayIcons _owner;
            private readonly Forms.NotifyIcon _icon;
            private object _renderedFrom;
            private DispatcherTimer _singleClick;

            public Entry(ModuleItem item, ModuleTrayIcons owner)
            {
                _item = item;
                _owner = owner;
                _icon = new Forms.NotifyIcon();
                Refresh();
                _icon.Visible = true;
                _icon.MouseClick += OnMouseClick;
                _icon.MouseDoubleClick += OnMouseDoubleClick;
                _item.PropertyChanged += OnItemChanged;
            }

            public void Dispose()
            {
                _singleClick?.Stop();
                _item.PropertyChanged -= OnItemChanged;
                _icon.Visible = false;
                _icon.Icon?.Dispose();
                _icon.Dispose();
            }

            private void OnItemChanged(object sender, PropertyChangedEventArgs e)
            {
                if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ModuleItem.IconImage))
                {
                    Refresh();
                }
            }

            private void Refresh()
            {
                // Tooltips are limited to 63 characters.
                string name = _item.Name ?? _item.Id;
                _icon.Text = name.Length > 63 ? name.Substring(0, 63) : name;

                object source = (object)_item.IconImage ?? _item.IconGeometry;
                if (ReferenceEquals(source, _renderedFrom) && _icon.Icon != null)
                {
                    return;
                }
                _renderedFrom = source;
                System.Drawing.Icon previous = _icon.Icon;
                _icon.Icon = Render(_item);
                previous?.Dispose();
            }

            private void OnMouseClick(object sender, Forms.MouseEventArgs e)
            {
                if (e.Button == Forms.MouseButtons.Left)
                {
                    if (_item.HasWindow)
                    {
                        // Wait out the double-click time, so a double click opens only the module's window.
                        if (_singleClick == null)
                        {
                            _singleClick = CreateSingleClickTimer();
                        }
                        _singleClick.Stop();
                        _singleClick.Start();
                    }
                    else
                    {
                        _owner._openModule(_item.Id);
                    }
                }
                else if (e.Button == Forms.MouseButtons.Right)
                {
                    _owner.ShowMenu(_item);
                }
            }

            private void OnMouseDoubleClick(object sender, Forms.MouseEventArgs e)
            {
                if (e.Button == Forms.MouseButtons.Left && _item.HasWindow)
                {
                    _singleClick?.Stop();
                    _owner.ShowWindow(_item);
                }
            }

            private DispatcherTimer CreateSingleClickTimer()
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    _owner._openModule(_item.Id);
                };
                return timer;
            }
        }

        /// <summary>Draws the module's tile (the same look as in the app) at the shell's small icon size.</summary>
        private static System.Drawing.Icon Render(ModuleItem item)
        {
            int size = Forms.SystemInformation.SmallIconSize.Width;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                double radius = size * 0.27;
                dc.DrawRoundedRectangle(item.TileBrush, null, new Rect(0, 0, size, size), radius, radius);
                if (item.IconImage != null)
                {
                    double inset = size * 0.16;
                    dc.DrawImage(item.IconImage, new Rect(inset, inset, size - 2 * inset, size - 2 * inset));
                }
                else
                {
                    double inset = size * 0.2;
                    double scale = (size - 2 * inset) / 24.0;
                    var pen = new Pen(item.AccentBrush, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    pen.Freeze();
                    dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, inset, inset));
                    dc.DrawGeometry(null, pen, item.IconGeometry);
                    dc.Pop();
                }
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                stream.Position = 0;
                using (var image = new System.Drawing.Bitmap(stream))
                {
                    IntPtr handle = image.GetHicon();
                    try
                    {
                        // Clone copies the handle, so the icon owns (and later frees) its own copy.
                        using (System.Drawing.Icon borrowed = System.Drawing.Icon.FromHandle(handle))
                        {
                            return (System.Drawing.Icon)borrowed.Clone();
                        }
                    }
                    finally
                    {
                        DestroyIcon(handle);
                    }
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}

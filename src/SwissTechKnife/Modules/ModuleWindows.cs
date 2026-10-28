using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using SwissTechKnife.Core;
using SwissTechKnife.ViewModels;
using SwissTechKnife.Views;

namespace SwissTechKnife.Modules
{
    /// <summary>
    /// Module pages opened in windows of their own. The host keeps one page element per module, so the page moves
    /// between the shell and its window: opening the window takes it from the shell, opening it in the shell closes the window.
    /// </summary>
    internal sealed class ModuleWindows : IDisposable
    {
        private const string Source = "ModuleWindows";
        private readonly ModuleManager _manager;
        private readonly ShellViewModel _shell;
        private readonly Dictionary<string, ModuleWindow> _windows = new Dictionary<string, ModuleWindow>(StringComparer.Ordinal);

        public ModuleWindows(ModuleManager manager, ShellViewModel shell)
        {
            _manager = manager;
            _shell = shell;
            _shell.PageClaimed = OnPageClaimed;
            _manager.Changed += OnChanged;
        }

        /// <summary>Brings the module's window forward if it is open.</summary>
        public bool BringToFront(string id)
        {
            if (!_windows.TryGetValue(id, out ModuleWindow open))
            {
                return false;
            }
            open.BringToFront();
            return true;
        }

        /// <summary>Shows the module's window (creating it if needed); false when the page could not be opened.</summary>
        public bool Show(ModuleItem item)
        {
            if (item == null || !item.IsEnabled)
            {
                return false;
            }
            if (_windows.TryGetValue(item.Id, out ModuleWindow open))
            {
                open.BringToFront();
                return true;
            }
            if (!_manager.TryActivate(item))
            {
                return false;
            }

            FrameworkElement page;
            try
            {
                page = _manager.Runtime.GetView(item.Id);
                item.PageError = null;
            }
            catch (Exception ex)
            {
                Log.Error(Source, "CreateView failed for " + item.Id, ex);
                return false;
            }

            _shell.ReleasePage(page);
            if (VisualTreeHelper.GetParent(page) is UIElement presenter)
            {
                // The shell's presenter drops its old content on the next layout pass; run it now.
                presenter.UpdateLayout();
                if (VisualTreeHelper.GetParent(page) != null)
                {
                    Log.Warn(Source, "Page of " + item.Id + " is still attached elsewhere");
                    return false;
                }
            }

            ModuleWindow window;
            try
            {
                window = new ModuleWindow(item, page) { Icon = ModuleTrayIcons.RenderTile(item, 32) };
            }
            catch (Exception ex)
            {
                Log.Error(Source, "Could not open the window of " + item.Id, ex);
                return false;
            }
            window.Closed += (s, e) =>
            {
                if (_windows.TryGetValue(item.Id, out ModuleWindow current) && ReferenceEquals(current, window))
                {
                    _windows.Remove(item.Id);
                }
            };
            _windows[item.Id] = window;
            window.Show();
            window.BringToFront();
            try
            {
                ModuleWindow.Lifecycle(page)?.OnViewShown();
            }
            catch (Exception ex)
            {
                Log.Error(Source, "Module view lifecycle callback failed", ex);
            }
            return true;
        }

        public void Dispose()
        {
            _manager.Changed -= OnChanged;
            _shell.PageClaimed = null;
            foreach (ModuleWindow window in _windows.Values.ToList())
            {
                window.Close();
            }
        }

        private void OnPageClaimed(FrameworkElement page)
        {
            ModuleWindow holder = _windows.Values.FirstOrDefault(w => ReferenceEquals(w.PageElement, page));
            holder?.Close();
        }

        /// <summary>A disabled or uninstalled module disposes its page: close its window first.</summary>
        private void OnChanged(object sender, EventArgs e)
        {
            foreach (ModuleWindow window in _windows.Values.Where(w => !w.Item.IsEnabled).ToList())
            {
                window.Close();
            }
        }
    }
}

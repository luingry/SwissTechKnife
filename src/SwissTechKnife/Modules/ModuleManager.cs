using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwissTechKnife.Core;
using SwissTechKnife.ViewModels;

namespace SwissTechKnife.Modules
{
    /// <summary>
    /// Merges catalog and install state into <see cref="Items"/> and performs install, update, enable,
    /// disable and uninstall. UI-thread only (file and network work is awaited off-thread).
    /// </summary>
    public sealed class ModuleManager
    {
        private const string Source = "Modules";
        private readonly HostPaths _paths;
        private readonly StateStore _state;
        private readonly CatalogClient _catalog;
        private readonly PackageInstaller _installer;
        private readonly ModuleRuntime _runtime;
        private readonly IconCache _icons;

        public ModuleManager(HostPaths paths, StateStore state, CatalogClient catalog, PackageInstaller installer, ModuleRuntime runtime)
        {
            _paths = paths;
            _state = state;
            _catalog = catalog;
            _installer = installer;
            _runtime = runtime;
            _icons = new IconCache(paths);
        }

        public ObservableCollection<ModuleItem> Items { get; } = new ObservableCollection<ModuleItem>();

        public CatalogResult LastCatalog { get; private set; }

        /// <summary>Raised after any change that can move an item between lists.</summary>
        public event EventHandler Changed;

        public ModuleRuntime Runtime => _runtime;

        /// <summary>Assigned to every item; the shell owns enable/disable (navigation, notices).</summary>
        public Action<ModuleItem, bool> ToggleHandler { get; set; }

        public ModuleItem Find(string id) => Items.FirstOrDefault(i => i.Id == id);

        /// <summary>Shows installed modules immediately (works offline, before the catalog arrives).</summary>
        public void LoadInstalled()
        {
            foreach (InstalledModule installed in _state.Modules)
            {
                GetOrAdd(installed.Id).Installed = installed;
            }
            RaiseChanged();
        }

        public void ActivateStartupModules()
        {
            foreach (ModuleItem item in Items.Where(i => i.IsEnabled && i.StartsWithHost).ToList())
            {
                TryActivate(item);
            }
        }

        /// <param name="live">False = use the saved catalog (no network) because an update check is not due.</param>
        public async Task RefreshCatalogAsync(Uri catalogUri, bool live, CancellationToken cancellationToken)
        {
            CatalogResult result = live
                ? await _catalog.LoadAsync(catalogUri, cancellationToken)
                : await _catalog.LoadCachedAsync(catalogUri, cancellationToken);
            LastCatalog = result;
            var listed = new System.Collections.Generic.HashSet<string>(result.Catalog.Modules.Select(m => m.Id));
            foreach (CatalogEntry entry in result.Catalog.Modules)
            {
                GetOrAdd(entry.Id).CatalogEntry = entry;
            }
            // Modules that left the catalog stay visible only while installed.
            foreach (ModuleItem item in Items.Where(i => !listed.Contains(i.Id)).ToList())
            {
                if (item.IsInstalled)
                {
                    item.CatalogEntry = null;
                }
                else
                {
                    Items.Remove(item);
                }
            }
            Sort();
            RaiseChanged();
            _ = LoadIconsAsync(result.Source);
        }

        /// <summary>Loads module icon images in the background; tiles keep the vector icon until they arrive.</summary>
        private async Task LoadIconsAsync(Uri catalogUri)
        {
            foreach (ModuleItem item in Items.ToList())
            {
                string url = item.IconImageUrl;
                if (string.IsNullOrWhiteSpace(url) || item.IconImage != null)
                {
                    continue;
                }
                Uri uri;
                try
                {
                    uri = new Uri(catalogUri, url);
                }
                catch (UriFormatException)
                {
                    continue;
                }
                System.Windows.Media.ImageSource image = await _icons.GetAsync(uri, CancellationToken.None);
                if (image != null)
                {
                    item.IconImage = image;
                }
            }
        }

        /// <summary>Installs, or updates to the catalog version.</summary>
        public async Task InstallAsync(ModuleItem item)
        {
            CatalogEntry entry = item.CatalogEntry;
            if (entry == null || !entry.IsAvailable || item.IsBusy || LastCatalog == null)
            {
                return;
            }
            InstalledModule previous = item.Installed;
            item.Error = null;
            item.IsBusy = true;
            item.Progress = 0;
            item.BusyText = Strings.Get(previous == null ? "Installing" : "Updating");
            try
            {
                Uri packageUri = CatalogClient.ResolvePackageUri(LastCatalog.Source, entry.Package);
                var progress = new Progress<double>(value => item.Progress = value);
                InstalledModule installed = await _installer.InstallAsync(entry, packageUri, progress, CancellationToken.None);
                if (previous != null)
                {
                    installed.Enabled = previous.Enabled;
                    installed.ShowTrayIcon = previous.ShowTrayIcon;
                }
                else
                {
                    // A fresh install shows its notification-area icon; the user can turn it off in the module's settings.
                    installed.ShowTrayIcon = true;
                }
                _state.Upsert(installed);

                bool codeLoaded = _runtime.LoadedVersion(item.Id) != null;
                if (previous != null && previous.Folder != installed.Folder)
                {
                    _state.DeleteOrQueue(Path.Combine(_paths.ModuleRoot(item.Id), previous.Folder));
                }
                item.Installed = installed;
                if (codeLoaded)
                {
                    item.RestartRequired = true;
                    item.RestartReason = Strings.Format("VersionAfterRestart", installed.Version);
                }
                else if (installed.Enabled && installed.Manifest.StartsWithHost)
                {
                    TryActivate(item);
                }
                Log.Info(Source, (previous == null ? "Installed " : "Updated ") + item.Id + " " + installed.Version);
            }
            finally
            {
                item.IsBusy = false;
                item.BusyText = null;
                RaiseChanged();
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>
        /// Reinstalls installed modules whose files are gone (deleted outside SwissTechKnife) from the catalog.
        /// Returns the modules that were restored.
        /// </summary>
        public async Task<System.Collections.Generic.List<ModuleItem>> RepairMissingAsync()
        {
            var restored = new System.Collections.Generic.List<ModuleItem>();
            var candidates = Items
                .Where(i => i.IsInstalled && !i.IsBusy && i.CatalogEntry != null && i.CatalogEntry.IsAvailable && _runtime.LoadedVersion(i.Id) == null)
                .Select(i => new { Item = i, Entry = Path.Combine(_runtime.InstallDirectory(i.Installed), i.Installed.Manifest.EntryAssembly) })
                .ToList();
            if (candidates.Count == 0 || LastCatalog == null)
            {
                return restored;
            }
            var missing = await Task.Run(() => candidates.Where(c => !File.Exists(c.Entry)).Select(c => c.Item).ToList());
            foreach (ModuleItem item in missing)
            {
                Log.Warn(Source, "Files missing for " + item.Id + "; reinstalling");
                try
                {
                    await InstallAsync(item);
                    item.PageError = null;
                    restored.Add(item);
                }
                catch (Exception ex)
                {
                    Log.Error(Source, "Could not reinstall " + item.Id, ex);
                }
            }
            return restored;
        }

        public void Uninstall(ModuleItem item)
        {
            if (!item.IsInstalled || item.IsBusy)
            {
                return;
            }
            bool locked = _runtime.HoldsFileLocks(item.Id);
            _runtime.Deactivate(item.Id);
            _state.Remove(item.Id);
            _state.DeleteOrQueue(_paths.ModuleRoot(item.Id));
            item.Installed = null;
            item.Error = null;
            item.PageError = null;
            item.RestartRequired = locked;
            item.RestartReason = locked ? Strings.Get("FilesRemovedOnRestart") : null;
            if (item.CatalogEntry == null)
            {
                Items.Remove(item);
            }
            Log.Info(Source, "Uninstalled " + item.Id);
            RaiseChanged();
        }

        public void SetEnabled(ModuleItem item, bool enabled)
        {
            if (!item.IsInstalled || item.IsEnabled == enabled)
            {
                return;
            }
            if (!enabled)
            {
                _runtime.Deactivate(item.Id);
            }
            _state.SetEnabled(item.Id, enabled);
            item.Installed = _state.Find(item.Id);
            item.PageError = null;
            if (enabled && item.StartsWithHost)
            {
                TryActivate(item);
            }
            RaiseChanged();
        }

        public void SetTrayIcon(ModuleItem item, bool show)
        {
            if (!item.IsInstalled)
            {
                return;
            }
            _state.SetTrayIcon(item.Id, show);
            item.Installed = _state.Find(item.Id);
            RaiseChanged();
        }

        /// <summary>Creates the module instance if needed; failures are recorded on the item, never thrown.</summary>
        public bool TryActivate(ModuleItem item)
        {
            if (!item.IsEnabled)
            {
                return false;
            }
            try
            {
                _runtime.Activate(item.Installed);
                item.PageError = null;
                return true;
            }
            catch (RestartRequiredException ex)
            {
                item.RestartRequired = true;
                item.RestartReason = ex.Message;
                item.PageError = ex.Message;
            }
            catch (Exception ex)
            {
                Exception root = ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                Log.Error(Source, "Activation failed for " + item.Id, root);
                item.PageError = Strings.Format("ModuleStartFailed", root.Message);
            }
            return false;
        }

        private ModuleItem GetOrAdd(string id)
        {
            ModuleItem item = Find(id);
            if (item == null)
            {
                item = new ModuleItem(id) { ToggleHandler = ToggleHandler, TrayIconHandler = SetTrayIcon };
                Items.Add(item);
            }
            return item;
        }

        private void Sort()
        {
            var ordered = Items.OrderBy(i => i.IsComingSoon).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            for (int target = 0; target < ordered.Count; target++)
            {
                int current = Items.IndexOf(ordered[target]);
                if (current != target)
                {
                    Items.Move(current, target);
                }
            }
        }

        private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}

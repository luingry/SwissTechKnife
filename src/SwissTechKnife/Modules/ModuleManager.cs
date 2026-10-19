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

        public ModuleManager(HostPaths paths, StateStore state, CatalogClient catalog, PackageInstaller installer, ModuleRuntime runtime)
        {
            _paths = paths;
            _state = state;
            _catalog = catalog;
            _installer = installer;
            _runtime = runtime;
        }

        public ObservableCollection<ModuleItem> Items { get; } = new ObservableCollection<ModuleItem>();

        public CatalogResult LastCatalog { get; private set; }

        /// <summary>Raised after any change that can move an item between lists.</summary>
        public event EventHandler Changed;

        public ModuleRuntime Runtime => _runtime;

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

        public async Task RefreshCatalogAsync(Uri catalogUri, CancellationToken cancellationToken)
        {
            CatalogResult result = await _catalog.LoadAsync(catalogUri, cancellationToken);
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
            item.BusyText = previous == null ? "Instalando…" : "Atualizando…";
            try
            {
                Uri packageUri = CatalogClient.ResolvePackageUri(LastCatalog.Source, entry.Package);
                var progress = new Progress<double>(value => item.Progress = value);
                InstalledModule installed = await _installer.InstallAsync(entry, packageUri, progress, CancellationToken.None);
                if (previous != null)
                {
                    installed.Enabled = previous.Enabled;
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
                    item.RestartReason = "A versão " + installed.Version + " será usada após reiniciar.";
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
            item.RestartReason = locked ? "Os arquivos serão removidos ao reiniciar." : null;
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
                item.PageError = "O módulo não conseguiu iniciar: " + root.Message;
            }
            return false;
        }

        private ModuleItem GetOrAdd(string id)
        {
            ModuleItem item = Find(id);
            if (item == null)
            {
                item = new ModuleItem(id);
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

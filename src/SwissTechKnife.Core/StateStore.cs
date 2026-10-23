using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SwissTechKnife.Core
{
    /// <summary>Installed-module state (state.json). Thread-safe; every mutation is persisted atomically.</summary>
    public sealed class StateStore
    {
        private const string Source = "State";
        private readonly object _gate = new object();
        private readonly HostPaths _paths;
        private HostState _state = new HostState();

        public StateStore(HostPaths paths)
        {
            _paths = paths;
        }

        public void Load()
        {
            HostState loaded = Json.ReadFileOrBackup<HostState>(_paths.StateFile, (file, ex) => Log.Error(Source, "Unreadable state file " + file, ex));
            lock (_gate)
            {
                _state = loaded ?? new HostState();
                _state.Modules = (_state.Modules ?? new List<InstalledModule>())
                    .Where(m => m != null && ModuleId.IsValid(m.Id) && m.Manifest != null && !string.IsNullOrEmpty(m.Folder))
                    .GroupBy(m => m.Id)
                    .Select(g => g.Last())
                    .ToList();
                _state.PendingDeletes = _state.PendingDeletes ?? new List<string>();
            }
        }

        public IReadOnlyList<InstalledModule> Modules
        {
            get
            {
                lock (_gate)
                {
                    return _state.Modules.ToList();
                }
            }
        }

        public InstalledModule Find(string id)
        {
            lock (_gate)
            {
                return _state.Modules.FirstOrDefault(m => m.Id == id);
            }
        }

        public void Upsert(InstalledModule module)
        {
            lock (_gate)
            {
                _state.Modules.RemoveAll(m => m.Id == module.Id);
                _state.Modules.Add(module);
                SaveLocked();
            }
        }

        public void SetEnabled(string id, bool enabled)
        {
            lock (_gate)
            {
                InstalledModule module = _state.Modules.FirstOrDefault(m => m.Id == id);
                if (module == null || module.Enabled == enabled)
                {
                    return;
                }
                module.Enabled = enabled;
                SaveLocked();
            }
        }

        public void SetTrayIcon(string id, bool show)
        {
            lock (_gate)
            {
                InstalledModule module = _state.Modules.FirstOrDefault(m => m.Id == id);
                if (module == null || module.ShowTrayIcon == show)
                {
                    return;
                }
                module.ShowTrayIcon = show;
                SaveLocked();
            }
        }

        public InstalledModule Remove(string id)
        {
            lock (_gate)
            {
                InstalledModule module = _state.Modules.FirstOrDefault(m => m.Id == id);
                if (module != null)
                {
                    _state.Modules.Remove(module);
                    SaveLocked();
                }
                return module;
            }
        }

        /// <summary>Deletes a folder now, or queues it for the next start if a file is locked.</summary>
        public void DeleteOrQueue(string directory)
        {
            if (TryDelete(directory))
            {
                return;
            }
            lock (_gate)
            {
                if (!_state.PendingDeletes.Contains(directory))
                {
                    _state.PendingDeletes.Add(directory);
                    SaveLocked();
                }
            }
        }

        /// <summary>Startup cleanup: queued deletes, orphan version folders and interrupted staging folders.</summary>
        public void CollectGarbage()
        {
            lock (_gate)
            {
                int before = _state.PendingDeletes.Count;
                _state.PendingDeletes.RemoveAll(TryDelete);
                if (_state.PendingDeletes.Count != before)
                {
                    SaveLocked();
                }
            }
            if (!Directory.Exists(_paths.Modules))
            {
                return;
            }
            var keep = new HashSet<string>(Modules.Select(m => Path.GetFullPath(Path.Combine(_paths.ModuleRoot(m.Id), m.Folder))), StringComparer.OrdinalIgnoreCase);
            foreach (string moduleRoot in Directory.EnumerateDirectories(_paths.Modules))
            {
                foreach (string folder in Directory.EnumerateDirectories(moduleRoot))
                {
                    if (!keep.Contains(Path.GetFullPath(folder)))
                    {
                        TryDelete(folder);
                    }
                }
                if (!Directory.EnumerateFileSystemEntries(moduleRoot).Any())
                {
                    TryDelete(moduleRoot);
                }
            }
            foreach (string file in Directory.EnumerateFiles(_paths.Downloads))
            {
                if (HostUpdater.OwnsDownload(Path.GetFileName(file)))
                {
                    // The self-update record and installer must survive the restart they cause; HostUpdater cleans them.
                    continue;
                }
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private void SaveLocked()
        {
            try
            {
                Json.WriteFileAtomic(_paths.StateFile, _state);
            }
            catch (Exception ex)
            {
                Log.Error(Source, "Could not save state", ex);
                throw;
            }
        }

        internal static bool TryDelete(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>User settings (settings.json).</summary>
    public sealed class SettingsStore
    {
        private readonly HostPaths _paths;

        public SettingsStore(HostPaths paths)
        {
            _paths = paths;
        }

        public HostSettings Current { get; private set; } = new HostSettings();

        public void Load()
        {
            Current = Json.ReadFileOrBackup<HostSettings>(_paths.SettingsFile, (file, ex) => Log.Error("Settings", "Unreadable settings " + file, ex)) ?? new HostSettings();
        }

        public void Save()
        {
            try
            {
                Json.WriteFileAtomic(_paths.SettingsFile, Current);
            }
            catch (Exception ex)
            {
                Log.Error("Settings", "Could not save settings", ex);
            }
        }
    }
}

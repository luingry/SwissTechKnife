using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using SwissTechKnife.Core;
using SwissTechKnife.Sdk;

namespace SwissTechKnife.Modules
{
    /// <summary>Thrown when a different version of a module's code is already loaded in this process.</summary>
    public sealed class RestartRequiredException : Exception
    {
        public RestartRequiredException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Loads module assemblies into the host process and owns module instances.
    /// .NET Framework cannot unload assemblies from the default AppDomain, so disabling a module disposes
    /// its instance (its code stays mapped until exit). A newer version of memory-loaded code is loaded
    /// side by side (<see cref="Unload"/>); file-loaded code (Assembly.LoadFrom) needs a restart.
    /// All members are UI-thread only.
    /// </summary>
    public sealed class ModuleRuntime
    {
        private const string Source = "Runtime";
        private readonly HostPaths _paths;
        private readonly Func<InstalledModule, ModuleContext> _contextFactory;
        private readonly Dictionary<string, LoadedCode> _code = new Dictionary<string, LoadedCode>(StringComparer.Ordinal);
        private readonly Dictionary<string, ActiveModule> _active = new Dictionary<string, ActiveModule>(StringComparer.Ordinal);
        // Code replaced by Unload: old instances may still run leftover callbacks that resolve their own dependencies.
        private readonly List<LoadedCode> _retired = new List<LoadedCode>();

        public ModuleRuntime(HostPaths paths, Func<InstalledModule, ModuleContext> contextFactory)
        {
            _paths = paths;
            _contextFactory = contextFactory;
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        public bool IsActive(string id) => _active.ContainsKey(id);

        /// <summary>True while a started background module (hooks, listeners) is running: the host must stay alive.</summary>
        public bool HasBackgroundModules => _active.Values.Any(a => a.Instance is IBackgroundModule);

        /// <summary>Version whose code is loaded in this process, or null.</summary>
        public string LoadedVersion(string id) => _code.TryGetValue(id, out LoadedCode code) ? code.Version : null;

        /// <summary>True if the module's files are locked by this process (file load mode).</summary>
        public bool HoldsFileLocks(string id) => _code.TryGetValue(id, out LoadedCode code) && code.FromFile;

        public string InstallDirectory(InstalledModule module) => Path.Combine(_paths.ModuleRoot(module.Id), module.Folder);

        /// <summary>Creates and initializes the module instance (idempotent).</summary>
        public ActiveModule Activate(InstalledModule module)
        {
            if (_active.TryGetValue(module.Id, out ActiveModule existing))
            {
                return existing;
            }

            Type entryType = LoadEntryType(module);
            var instance = (IModule)Activator.CreateInstance(entryType);
            ModuleContext context = _contextFactory(module);
            var active = new ActiveModule(module.Id, instance, context);
            try
            {
                instance.Initialize(context);
                if (module.Manifest.StartsWithHost && instance is IBackgroundModule background)
                {
                    background.Start();
                }
            }
            catch
            {
                SafeDispose(active);
                throw;
            }
            _active[module.Id] = active;
            Log.Info(Source, $"Activated {module.Id} {module.Version}");
            return active;
        }

        /// <summary>The active module's instance, or null.</summary>
        public IModule Instance(string id) => _active.TryGetValue(id, out ActiveModule active) ? active.Instance : null;

        public FrameworkElement GetView(string id)
        {
            ActiveModule active = _active[id];
            if (active.View == null)
            {
                active.View = active.Instance.CreateView() ?? throw new InvalidOperationException("CreateView returned null.");
            }
            return active.View;
        }

        /// <summary>The module's settings view, or null if it does not implement IModuleSettings.</summary>
        public FrameworkElement GetSettingsView(string id)
        {
            ActiveModule active = _active[id];
            if (active.SettingsView == null && active.Instance is IModuleSettings settings)
            {
                active.SettingsView = settings.CreateSettingsView();
            }
            return active.SettingsView;
        }

        public void Deactivate(string id)
        {
            if (_active.TryGetValue(id, out ActiveModule active))
            {
                _active.Remove(id);
                SafeDispose(active);
                Log.Info(Source, "Deactivated " + id);
            }
        }

        /// <summary>
        /// Disposes the instance and forgets the module's loaded code, so the next <see cref="Activate"/> loads
        /// the installed version. False (nothing changes) for file-loaded code, which stays until a restart.
        /// </summary>
        public bool Unload(string id)
        {
            if (!_code.TryGetValue(id, out LoadedCode code))
            {
                return true;
            }
            if (code.FromFile)
            {
                return false;
            }
            Deactivate(id);
            _code.Remove(id);
            _retired.Add(code);
            Log.Info(Source, $"Unloaded {id} {code.Version}");
            return true;
        }

        public void DeactivateAll()
        {
            foreach (string id in _active.Keys.ToList())
            {
                Deactivate(id);
            }
        }

        private Type LoadEntryType(InstalledModule module)
        {
            string folder = InstallDirectory(module);
            if (_code.TryGetValue(module.Id, out LoadedCode loaded))
            {
                if (loaded.Version != module.Version || !string.Equals(loaded.Folder, folder, StringComparison.OrdinalIgnoreCase))
                {
                    throw new RestartRequiredException(Strings.Format("RestartForVersion", module.Version));
                }
                return loaded.EntryType;
            }

            string assemblyPath = Path.Combine(folder, module.Manifest.EntryAssembly);
            Assembly assembly = module.Manifest.LoadsFromFile ? Assembly.LoadFrom(assemblyPath) : LoadIntoMemory(assemblyPath);
            Type type = assembly.GetType(module.Manifest.EntryType, throwOnError: true);
            if (!typeof(IModule).IsAssignableFrom(type) || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new InvalidOperationException(module.Manifest.EntryType + " must be a concrete IModule with a public parameterless constructor.");
            }
            var code = new LoadedCode(module.Version, folder, type, module.Manifest.LoadsFromFile);
            code.Assemblies.Add(assembly);
            _code[module.Id] = code;
            return type;
        }

        private static Assembly LoadIntoMemory(string path)
        {
            byte[] image = File.ReadAllBytes(path);
            string pdb = Path.ChangeExtension(path, ".pdb");
            return File.Exists(pdb) ? Assembly.Load(image, File.ReadAllBytes(pdb)) : Assembly.Load(image);
        }

        /// <summary>
        /// Resolves the shared SDK to the host's copy, then module-private dependencies: first from the code of
        /// the requesting module (so a reloaded version gets its own copies), then from other loaded modules and
        /// the AppDomain, then from the folders of loaded modules. Each dependency loads once per module version.
        /// </summary>
        private Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name);
            if (string.Equals(name.Name, typeof(IModule).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
            {
                return typeof(IModule).Assembly;
            }
            LoadedCode owner = args.RequestingAssembly == null
                ? null
                : _code.Values.Concat(_retired).FirstOrDefault(c => c.Assemblies.Contains(args.RequestingAssembly));
            if (owner != null)
            {
                Assembly own = owner.Find(name.Name) ?? Probe(owner, name.Name);
                if (own != null)
                {
                    return own;
                }
            }
            foreach (LoadedCode code in _code.Values)
            {
                Assembly cached = code.Find(name.Name);
                if (cached != null)
                {
                    return cached;
                }
            }
            // Newest first: after a reload, the current copy of a same-named assembly.
            Assembly already = AppDomain.CurrentDomain.GetAssemblies().LastOrDefault(a => string.Equals(a.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));
            if (already != null)
            {
                return already;
            }
            foreach (LoadedCode code in _code.Values)
            {
                Assembly probed = Probe(code, name.Name);
                if (probed != null)
                {
                    return probed;
                }
            }
            return null;
        }

        private static Assembly Probe(LoadedCode code, string name)
        {
            string candidate = Path.Combine(code.Folder, name + ".dll");
            if (!File.Exists(candidate))
            {
                return null;
            }
            try
            {
                Assembly dependency = code.FromFile ? Assembly.LoadFrom(candidate) : LoadIntoMemory(candidate);
                code.Assemblies.Add(dependency);
                return dependency;
            }
            catch (Exception ex)
            {
                Log.Error(Source, "Failed to load dependency " + candidate, ex);
                return null;
            }
        }

        private static void SafeDispose(ActiveModule active)
        {
            try
            {
                if (active.Instance is IBackgroundModule background)
                {
                    background.Stop();
                }
            }
            catch (Exception ex)
            {
                Log.Error(Source, "Stop failed for " + active.Id, ex);
            }
            try
            {
                active.Instance.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(Source, "Dispose failed for " + active.Id, ex);
            }
            active.View = null;
            active.SettingsView = null;
        }

        private sealed class LoadedCode
        {
            public LoadedCode(string version, string folder, Type entryType, bool fromFile)
            {
                Version = version;
                Folder = folder;
                EntryType = entryType;
                FromFile = fromFile;
            }

            public string Version { get; }
            public string Folder { get; }
            public Type EntryType { get; }
            public bool FromFile { get; }

            /// <summary>Entry assembly and the dependencies loaded for this version.</summary>
            public List<Assembly> Assemblies { get; } = new List<Assembly>();

            public Assembly Find(string name) => Assemblies.FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class ActiveModule
    {
        public ActiveModule(string id, IModule instance, ModuleContext context)
        {
            Id = id;
            Instance = instance;
            Context = context;
        }

        public string Id { get; }

        public IModule Instance { get; }

        public ModuleContext Context { get; }

        public FrameworkElement View { get; set; }

        public FrameworkElement SettingsView { get; set; }
    }
}

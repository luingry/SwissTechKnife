using System;
using System.IO;

namespace SwissTechKnife.Core
{
    /// <summary>Per-user folders. Everything lives under %LOCALAPPDATA%\SwissTechKnife.</summary>
    public sealed class HostPaths
    {
        public HostPaths(string root)
        {
            Root = root;
            Modules = Path.Combine(root, "modules");
            Data = Path.Combine(root, "data");
            Logs = Path.Combine(root, "logs");
            Cache = Path.Combine(root, "cache");
            Downloads = Path.Combine(root, "downloads");
            Jit = Path.Combine(root, "jit");
            StateFile = Path.Combine(root, "state.json");
            SettingsFile = Path.Combine(root, "settings.json");
            CatalogCacheFile = Path.Combine(Cache, "catalog.json");
        }

        /// <summary>%LOCALAPPDATA%\SwissTechKnife, or STK_HOME (isolated profile for development and tests).</summary>
        public static HostPaths Default { get; } = new HostPaths(
            Environment.GetEnvironmentVariable("STK_HOME") is string home && home.Length > 0
                ? Path.GetFullPath(home)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwissTechKnife"));

        public string Root { get; }
        public string Modules { get; }
        public string Data { get; }
        public string Logs { get; }
        public string Cache { get; }
        public string Downloads { get; }
        public string Jit { get; }
        public string StateFile { get; }
        public string SettingsFile { get; }
        public string CatalogCacheFile { get; }

        public string ModuleRoot(string id) => Path.Combine(Modules, ModuleId.Validate(id));

        public string ModuleData(string id) => Path.Combine(Data, ModuleId.Validate(id));

        public void EnsureCreated()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Modules);
            Directory.CreateDirectory(Data);
            Directory.CreateDirectory(Logs);
            Directory.CreateDirectory(Cache);
            Directory.CreateDirectory(Downloads);
            Directory.CreateDirectory(Jit);
        }
    }

    public static class ModuleId
    {
        /// <summary>Ids are used as folder names: lowercase letters, digits and dashes only (no path tricks).</summary>
        public static bool IsValid(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length < 2 || id.Length > 64)
            {
                return false;
            }
            if (!IsLowerAlnum(id[0]))
            {
                return false;
            }
            foreach (char c in id)
            {
                if (!IsLowerAlnum(c) && c != '-')
                {
                    return false;
                }
            }
            return true;
        }

        public static string Validate(string id)
        {
            if (!IsValid(id))
            {
                throw new ArgumentException("Invalid module id: " + (id ?? "<null>"));
            }
            return id;
        }

        private static bool IsLowerAlnum(char c) => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
    }
}

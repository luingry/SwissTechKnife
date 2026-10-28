using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SwissTechKnife.Core
{
    /// <summary>Remote list of modules (catalog.json).</summary>
    [DataContract]
    public sealed class Catalog
    {
        [DataMember(Name = "schema", Order = 0)]
        public int Schema { get; set; } = 1;

        [DataMember(Name = "modules", Order = 1)]
        public List<CatalogEntry> Modules { get; set; } = new List<CatalogEntry>();

        /// <summary>Latest SwissTechKnife release (self-update). Optional.</summary>
        [DataMember(Name = "host", Order = 2, EmitDefaultValue = false)]
        public HostRelease Host { get; set; }
    }

    /// <summary>The host's own release, advertised next to the modules.</summary>
    [DataContract]
    public sealed class HostRelease
    {
        [DataMember(Name = "version", Order = 0)]
        public string Version { get; set; }

        /// <summary>Inno Setup installer URL (absolute, or relative to the catalog).</summary>
        [DataMember(Name = "installer", Order = 1)]
        public string Installer { get; set; }

        [DataMember(Name = "sha256", Order = 2)]
        public string Sha256 { get; set; }

        [DataMember(Name = "size", Order = 3, EmitDefaultValue = false)]
        public long Size { get; set; }

        /// <summary>Release notes, one entry per version (same shape as module notes).</summary>
        [DataMember(Name = "releaseNotes", Order = 4, EmitDefaultValue = false)]
        public List<ReleaseNote> ReleaseNotes { get; set; }
    }

    /// <summary>
    /// downloads/host-update.json: written before the installer starts, read by the next start
    /// (new version on success, old version on failure) to report the outcome.
    /// </summary>
    [DataContract]
    public sealed class PendingHostUpdate
    {
        [DataMember(Name = "fromVersion", Order = 0)]
        public string FromVersion { get; set; }

        [DataMember(Name = "toVersion", Order = 1)]
        public string ToVersion { get; set; }

        [DataMember(Name = "installer", Order = 2)]
        public string Installer { get; set; }

        [DataMember(Name = "log", Order = 3)]
        public string Log { get; set; }

        /// <summary>Written by the installer: "started" (elevated and running), then "ok" or "failed".</summary>
        [DataMember(Name = "signal", Order = 4)]
        public string Signal { get; set; }

        [DataMember(Name = "startedUtc", Order = 5)]
        public string StartedUtc { get; set; }
    }

    /// <summary>One module as advertised by the catalog.</summary>
    [DataContract]
    public sealed class CatalogEntry
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        [DataMember(Name = "name", Order = 1)]
        public string Name { get; set; }

        /// <summary>One line shown on the card.</summary>
        [DataMember(Name = "tagline", Order = 2)]
        public string Tagline { get; set; }

        /// <summary>Longer explanation shown in the details dialog.</summary>
        [DataMember(Name = "description", Order = 3)]
        public string Description { get; set; }

        [DataMember(Name = "category", Order = 4)]
        public string Category { get; set; }

        [DataMember(Name = "author", Order = 5)]
        public string Author { get; set; }

        [DataMember(Name = "version", Order = 6)]
        public string Version { get; set; }

        [DataMember(Name = "minHostVersion", Order = 7, EmitDefaultValue = false)]
        public string MinHostVersion { get; set; }

        /// <summary>Stroke icon as SVG path data on a 24×24 grid.</summary>
        [DataMember(Name = "icon", Order = 8)]
        public string Icon { get; set; }

        /// <summary>Module color (#RRGGBB) used for its icon tile.</summary>
        [DataMember(Name = "accent", Order = 9)]
        public string Accent { get; set; }

        /// <summary>Package URL (absolute, or relative to the catalog). Empty = announced, not yet available.</summary>
        [DataMember(Name = "package", Order = 10, EmitDefaultValue = false)]
        public string Package { get; set; }

        [DataMember(Name = "sha256", Order = 11, EmitDefaultValue = false)]
        public string Sha256 { get; set; }

        [DataMember(Name = "size", Order = 12, EmitDefaultValue = false)]
        public long Size { get; set; }

        [DataMember(Name = "homepage", Order = 13, EmitDefaultValue = false)]
        public string Homepage { get; set; }

        /// <summary>Optional raster icon (PNG URL, absolute or relative to the catalog). Wins over <see cref="Icon"/>.</summary>
        [DataMember(Name = "iconImage", Order = 14, EmitDefaultValue = false)]
        public string IconImage { get; set; }

        /// <summary>Optional tile color (#RRGGBB) behind <see cref="IconImage"/>; defaults to the accent tint.</summary>
        [DataMember(Name = "iconBackground", Order = 15, EmitDefaultValue = false)]
        public string IconBackground { get; set; }

        /// <summary>Release notes, one entry per version (any order; the UI sorts newest first).</summary>
        [DataMember(Name = "releaseNotes", Order = 16, EmitDefaultValue = false)]
        public List<ReleaseNote> ReleaseNotes { get; set; }

        public bool IsAvailable => !string.IsNullOrWhiteSpace(Package) && !string.IsNullOrWhiteSpace(Sha256);

        /// <summary>True when the package needs a newer host than <paramref name="hostVersion"/> (it may use newer SDK types).</summary>
        public bool RequiresNewerHost(string hostVersion) => !string.IsNullOrWhiteSpace(MinHostVersion) && VersionText.IsNewer(MinHostVersion, hostVersion);

        public CatalogEntry Clone() => (CatalogEntry)MemberwiseClone();
    }

    [DataContract]
    public sealed class ReleaseNote
    {
        [DataMember(Name = "version", Order = 0)]
        public string Version { get; set; }

        /// <summary>Release date, yyyy-MM-dd.</summary>
        [DataMember(Name = "date", Order = 1, EmitDefaultValue = false)]
        public string Date { get; set; }

        /// <summary>User-facing changes, one sentence each (pt-BR).</summary>
        [DataMember(Name = "notes", Order = 2)]
        public List<string> Notes { get; set; } = new List<string>();
    }

    /// <summary>module.json at the root of every package.</summary>
    [DataContract]
    public sealed class ModuleManifest
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        [DataMember(Name = "version", Order = 1)]
        public string Version { get; set; }

        /// <summary>File name of the assembly that contains the entry type.</summary>
        [DataMember(Name = "entryAssembly", Order = 2)]
        public string EntryAssembly { get; set; }

        /// <summary>Full name of the class implementing IModule.</summary>
        [DataMember(Name = "entryType", Order = 3)]
        public string EntryType { get; set; }

        [DataMember(Name = "sdkVersion", Order = 4)]
        public int SdkVersion { get; set; } = 1;

        /// <summary>"onDemand" (default: loaded when first opened) or "startup" (loaded with the host).</summary>
        [DataMember(Name = "activation", Order = 5, EmitDefaultValue = false)]
        public string Activation { get; set; }

        /// <summary>
        /// "memory" (default): assemblies are read into memory, so files are never locked and uninstall/update
        /// apply immediately. "file": Assembly.LoadFrom, for code that needs Assembly.Location or native
        /// side-by-side DLLs (e.g. WebView2); file changes then apply on the next start.
        /// </summary>
        [DataMember(Name = "loadMode", Order = 6, EmitDefaultValue = false)]
        public string LoadMode { get; set; }

        /// <summary>True when the entry type implements IModuleSettings (shows the "Settings" tab).</summary>
        [DataMember(Name = "settings", Order = 7, EmitDefaultValue = false)]
        public bool HasSettings { get; set; }

        /// <summary>True when the entry type implements IModuleWindow (double-clicking the tray icon opens it).</summary>
        [DataMember(Name = "window", Order = 8, EmitDefaultValue = false)]
        public bool HasWindow { get; set; }

        public bool StartsWithHost => string.Equals(Activation, "startup", System.StringComparison.OrdinalIgnoreCase);

        public bool LoadsFromFile => string.Equals(LoadMode, "file", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A module present on disk.</summary>
    [DataContract]
    public sealed class InstalledModule
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        [DataMember(Name = "version", Order = 1)]
        public string Version { get; set; }

        [DataMember(Name = "enabled", Order = 2)]
        public bool Enabled { get; set; } = true;

        [DataMember(Name = "installedUtc", Order = 3)]
        public string InstalledUtc { get; set; }

        /// <summary>Folder name under modules/&lt;id&gt;/ holding this version.</summary>
        [DataMember(Name = "folder", Order = 4)]
        public string Folder { get; set; }

        [DataMember(Name = "manifest", Order = 5)]
        public ModuleManifest Manifest { get; set; }

        /// <summary>Catalog entry snapshot at install time, so the module still shows offline or after it leaves the catalog.</summary>
        [DataMember(Name = "entry", Order = 6)]
        public CatalogEntry Entry { get; set; }

        /// <summary>The module has its own notification-area icon while enabled.</summary>
        [DataMember(Name = "trayIcon", Order = 7, EmitDefaultValue = false)]
        public bool ShowTrayIcon { get; set; }
    }

    [DataContract]
    public sealed class HostState
    {
        [DataMember(Name = "schema", Order = 0)]
        public int Schema { get; set; } = 1;

        [DataMember(Name = "modules", Order = 1)]
        public List<InstalledModule> Modules { get; set; } = new List<InstalledModule>();

        /// <summary>Folders that could not be deleted (locked); retried at the next start.</summary>
        [DataMember(Name = "pendingDeletes", Order = 2)]
        public List<string> PendingDeletes { get; set; } = new List<string>();
    }

    [DataContract]
    public sealed class HostSettings
    {
        /// <summary>Catalog location: http(s) URL, absolute path, or a path relative to the app folder.</summary>
        [DataMember(Name = "catalog", Order = 0, EmitDefaultValue = false)]
        public string Catalog { get; set; }

        [DataMember(Name = "window", Order = 1, EmitDefaultValue = false)]
        public WindowPlacement Window { get; set; }

        [DataMember(Name = "lastPage", Order = 2, EmitDefaultValue = false)]
        public string LastPage { get; set; }

        /// <summary>"daily" (default), "weekly", "monthly" or "never": how often a remote catalog is checked for updates.</summary>
        [DataMember(Name = "updateFrequency", Order = 3, EmitDefaultValue = false)]
        public string UpdateFrequency { get; set; }

        /// <summary>UTC time of the last successful live catalog check (ISO 8601).</summary>
        [DataMember(Name = "lastUpdateCheckUtc", Order = 4, EmitDefaultValue = false)]
        public string LastUpdateCheckUtc { get; set; }

        /// <summary>Closing the window keeps the host running in the notification area.</summary>
        [DataMember(Name = "hideToTrayOnClose", Order = 5)]
        public bool HideToTrayOnClose { get; set; }

        /// <summary>UI language: "en" (default) or "pt-BR".</summary>
        [DataMember(Name = "language", Order = 6, EmitDefaultValue = false)]
        public string Language { get; set; }
    }

    [DataContract]
    public sealed class WindowPlacement
    {
        [DataMember(Name = "left", Order = 0)] public double Left { get; set; }
        [DataMember(Name = "top", Order = 1)] public double Top { get; set; }
        [DataMember(Name = "width", Order = 2)] public double Width { get; set; }
        [DataMember(Name = "height", Order = 3)] public double Height { get; set; }
        [DataMember(Name = "maximized", Order = 4)] public bool Maximized { get; set; }
    }
}

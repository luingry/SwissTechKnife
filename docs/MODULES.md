# Writing a module

Modules live in their own repositories. A module is a .NET Framework 4.8 WPF class library that implements `SwissTechKnife.Sdk.IModule`, packaged as a zip with a `module.json`, and listed in the host catalog.

## 1. Project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <UseWPF>true</UseWPF>
    <AssemblyName>SmoothMice.Module</AssemblyName>
    <DebugType>embedded</DebugType>
  </PropertyGroup>
  <ItemGroup>
    <!-- Compile-only: the host provides the one shared copy at runtime. -->
    <Reference Include="SwissTechKnife.Sdk" HintPath="lib\SwissTechKnife.Sdk.dll" Private="false" />
    <None Include="module.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

Copy `SwissTechKnife.Sdk.dll` from a host build (or `dotnet pack src/SwissTechKnife.Sdk` and reference the package). SDK 1.x only grows additively.

## 2. Entry point

```csharp
public sealed class SmoothMiceModule : IBackgroundModule
{
    private IModuleContext _context;
    public void Initialize(IModuleContext context) => _context = context; // cheap, never block
    public void Start() { /* install hooks */ }                         // activation: startup only
    public void Stop() { /* remove hooks */ }
    public FrameworkElement CreateView() => new SettingsView(_context);  // created on first open, kept alive
    public void Dispose() { }
}
```

`IModuleContext` gives: `DataDirectory` (settings that survive updates), `InstallDirectory` (read-only), `Dispatcher`, `Log`, `Notify(...)` (host toast) and `ShowModulePage()`.
Implement `IModuleViewLifecycle` on the view (or its DataContext) to pause timers while hidden.

### Settings

If the module offers customization, implement `IModuleSettings.CreateSettingsView()` and set `"settings": true` in `module.json`. The host then shows a **Settings** tab next to **Description** in the module's details dialog (enabled modules only). Changes apply immediately; persist them under `DataDirectory`. See the Tamarillo module (github.com/luingry/Tamarillo).

## 3. module.json

```json
{
  "id": "smooth-mice",
  "version": "3.0.0",
  "entryAssembly": "SmoothMice.Module.dll",
  "entryType": "SmoothMice.Module.SmoothMiceModule",
  "sdkVersion": 1,
  "activation": "startup",
  "loadMode": "memory"
}
```

| Field | Values |
|---|---|
| `activation` | `onDemand` (default): created when the page is first opened. `startup`: created with the host; `IBackgroundModule.Start` is called. |
| `settings` | `true` when the entry type implements `IModuleSettings` (shows the Settings tab without loading the module first). |
| `loadMode` | `memory` (default): assemblies are read into memory, files are never locked, uninstall/update apply at once. `file`: `Assembly.LoadFrom`, for code that needs `Assembly.Location` or native side-by-side DLLs (WebView2). File changes then apply after a restart. |

## 4. Package and catalog

Zip the build output (no `SwissTechKnife.Sdk.dll`), `module.json` at the root. Publish it (e.g. GitHub release asset) and add/update the entry in the host's `catalog/catalog.json`:

```json
{
  "id": "smooth-mice", "name": "SmoothMice", "tagline": "…", "description": "…",
  "version": "3.0.0",
  "icon": "<24×24 stroke path>", "accent": "#F2A65A",
  "iconImage": "icons/smooth-mice.png", "iconBackground": "#ECE6DD",
  "package": "https://github.com/luingry/SmoothMice/releases/download/v3.0.0/smooth-mice-3.0.0.zip",
  "sha256": "<lowercase hex>", "size": 123456
}
```

### Automatic catalog updates

After the first entry is in the catalog, new versions reach every install on their own: the module repository only publishes a GitHub release with a `<id>-<x.y.z>.zip` asset (any tag name; the module repositories do it from CI when the version changes on `main`). The **Sync catalog** workflow (`.github/workflows/sync-catalog.yml`, every 15 minutes or on demand) runs `scripts/sync-catalog.mjs`, which reads the releases of each module's GitHub `homepage` (this repository for modules without one, such as `samples/`), and for a newer version checks the package's `module.json` (same id and version) and commits `version`, `package`, `sha256`, `size` and a release-notes entry to `main`. The repository must be public so installs can download the package.

Release notes for the catalog come from an optional `releaseNotes` list in the package's `module.json`, same shape as the catalog's without the date (`[{ "version": "1.2.0", "notes": ["…"] }]`, English, one sentence per change; the host ignores the field). Only the entry for the released version is used; without one, the bullet lines of the GitHub release body are. Samples in this repository are released by `.github/workflows/release-samples.yml` when the version in their `module.json` changes on `main`.

`releaseNotes` is a list of `{ "version", "date": "yyyy-MM-dd", "notes": ["…"] }` (English, one sentence per change): the details dialog shows them in **Release notes** (up to the current version, marked **Current**), and the update confirmation shows the versions being installed. `iconImage` (PNG, ideally 256 px, put it in `catalog/icons/`) is the module's own icon and replaces the vector `icon` once loaded; it is cached for offline use. `iconBackground` sets the tile color behind it. An entry without `package`/`sha256` shows as **Coming soon**. The host verifies SHA-256, extracts into a staging folder, validates `module.json` (id, version, entry, SDK) and only then swaps it in. A newer catalog `version` shows **Update available**.

## 5. Rules

- Use only design-system keys (`docs/DESIGN_SYSTEM.md`); no native-looking controls, no hard-coded colors.
- The module shares the host process: never block the UI thread, never call `Environment.Exit`, catch your own background exceptions, release hooks/handles in `Stop`/`Dispose`.
- Do not bundle another copy of the SDK or of WPF/.NET assemblies.
- Single-instance, tray icon, auto-start and updates belong to the host: remove them from the app when converting it. While any `IBackgroundModule` is active, closing the host window keeps the process in the notification area (a one-time hint explains why), so hooks and shortcuts keep working.
- Local testing from a module's own repository: build its package to `<repo>/artifacts/<id>-<version>.zip` next to this repo; `scripts/pack-modules.ps1` adds it to the local feed for a catalog entry with that id and version.

## Conversion notes

| App | Notes |
|---|---|
| SmoothMice | `activation: startup` + `IBackgroundModule` for the `WH_MOUSE_LL` hook and scroll worker. Its settings move to `DataDirectory`. The host's single-instance guard replaces the app's. Its own WPF theme is replaced by the SDK theme. |
| Winput LAN | Same supervised-process design as SmoothMice (`--hosted`), so WebView2 lives only in the child process and the module keeps `loadMode: memory`. No UAC on host start: the module launches an elevated scheduled task only when the user enabled admin control (one UAC on that click; the elevated copy lives in Program Files) and asks for the firewall rule on request. Details: `docs/SWISSTECHKNIFE.md` in its repository. Uninstalling the module cannot remove the task, the Program Files copy or the firewall rules (needs elevation; the SDK has no uninstall hook). |
| VDGET | Module lives in `vd-get/stk`. The native page replaces `index.html`; the unchanged Python backend (`servidor.py`, `sniffer.py`) ships under `backend/` and runs hidden in a kill-on-close job, configured by `VDGET_*` environment variables (port, idle exit, data folder). `activation: onDemand`: started when the page opens, closes itself after 3 idle minutes. Needs Python 3 on the machine. |
| AQ Tracker | Supervised process (`--hosted`): keeps SQLite, System.Text.Json 8 and the Codex app-server out of the host. Data in the module folder (copied once from `%APPDATA%\AqTracker`); the Claude token file stays shared with the standalone app because refresh tokens rotate. The module page replaces the tray menu. |

# Development

Windows host for modular apps: the user picks which modules to install, enable, disable or remove; every module runs inside the same lightweight host and shares one design system.

- **Stack:** WPF on .NET Framework 4.8 (ships with Windows: no runtime to bundle). Host binaries ≈ 250 KB, installer ≈ 2.5 MB.
- **Startup:** first paint before any disk/network work, multicore JIT profile, ngen at install.
- **Reliability:** SHA-256-verified packages, staged installs, atomic state writes with `.bak` fallback, offline catalog cache, single instance, faulty modules are contained and reported.

## Layout

| Path | What |
|---|---|
| `src/SwissTechKnife` | Host app (shell, store, module runtime, settings, self-update UI) |
| `src/SwissTechKnife.Sdk` | Module contract + design system (`Theme/`) referenced by module repos |
| `src/SwissTechKnife.Core` | Catalog, install state, package installer, host updater (no UI) |
| `samples/` | Sample modules used to exercise the host |
| `catalog/catalog.json` | Published module catalog (+ `icons/`) |
| `assets/brand` | Logo rasters generated from `src/SwissTechKnife/Theme/Brand.xaml` |
| `docs/` | [Design system](DESIGN_SYSTEM.md) · [Writing a module](MODULES.md) |

## Build

```powershell
powershell -File scripts/build.ps1              # feed + build + tests
powershell -File scripts/build.ps1 -Installer   # + artifacts/installer/SwissTechKnife-<ver>-setup.exe (Inno Setup 6)
powershell -File scripts/render-brand.ps1       # after editing the logo
```

Run: `src/SwissTechKnife/bin/Release/net48/SwissTechKnife.exe`.

User data lives in `%LOCALAPPDATA%\SwissTechKnife` (`modules/`, `data/<module>/`, `state.json`, `settings.json`, `logs/host.log`).
`STK_HOME=<folder>` runs the app with an isolated profile (and its own single-instance mutex) for testing. `STK_FRAMEDIAG=1` logs animation frame gaps.

## Localization

The host UI ships in English (default) and Brazilian Portuguese; users pick the language in Settings and the whole UI switches immediately (saved as `language` in `settings.json`).

- Every string lives in `src/SwissTechKnife.Core/Strings.cs`, one table per language. Add each key to both tables: the tests fail on a missing translation, and a missing key falls back to English at runtime.
- XAML: `Text="{infra:Loc Key}"` (a one-way binding, so it works in setters and follows language changes).
- Code: `Strings.Get("Key")` / `Strings.Format("Key", args)`. Text a view model builds in code must be raised again on `Strings.LanguageChanged`.
- Catalog text (names, taglines, descriptions, release notes) is English only.

## Catalog

The app reads the published `catalog/catalog.json` of this repository (raw GitHub URL), cached for offline use. Before anything is cached, the copy bundled next to the exe (`feed/catalog.json`, written by `scripts/pack-modules.ps1`) is the fallback. Override with `"catalog"` in `settings.json` or the `STK_CATALOG` environment variable (path or https URL).

Pushing `catalog/catalog.json` is what announces new modules and updates. How often installs check is the user's choice in Settings (daily by default).

## Self-update

The catalog's `"host"` entry advertises the latest SwissTechKnife:

```json
"host": {
  "version": "0.4.0",
  "installer": "https://github.com/luingry/SwissTechKnife/releases/download/v0.4.0/SwissTechKnife-0.4.0-setup.exe",
  "sha256": "<64 hex>",
  "size": 2461961,
  "releaseNotes": [ { "version": "0.4.0", "date": "2026-10-20", "notes": [ "..." ] } ]
}
```

When a check finds a newer version, Settings shows a notice and a toast; *Update* shows the notes, downloads the installer to `%LOCALAPPDATA%\SwissTechKnife\downloads`, verifies SHA-256 (again right before running it) and starts it with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /STKUPDATE …`. The installer (`[Code]` in `installer/SwissTechKnife.iss`) writes a signal file once it runs elevated, the app then exits, the installer waits for the app's mutex, installs, and relaunches the app (success or failure). The next start reads `downloads/host-update.json` and reports the outcome; on failure the installer log path is shown.

## Releasing

1. Bump `<Version>` in `Directory.Build.props` and commit.
2. `powershell -File scripts/publish-host.ps1 -Notes "Nota 1.", "Nota 2."` builds the installer, creates the GitHub release `v<version>`, checks the served file's hash and writes the catalog `"host"` entry.
3. Module packages whose catalog `package` is a local file name: upload them to the release (`gh release upload`) and set the absolute URL, `sha256` and `size` in `catalog/catalog.json`.
4. Commit and push `catalog/catalog.json`: installs pick the update up at their next check.

`powershell -File scripts/test-host-update.ps1` runs an end-to-end update from a local feed (per-user test install, no UAC).

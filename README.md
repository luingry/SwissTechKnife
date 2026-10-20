# SwissTechKnife

Windows host for modular apps. The user picks which modules to install, enable, disable or remove; every module runs inside the same lightweight process and shares one design system.

- **Stack:** WPF on .NET Framework 4.8 (ships with Windows: no runtime to bundle). Host binaries ≈ 250 KB, installer ≈ 2 MB.
- **Startup:** first paint before any disk/network work, multicore JIT profile, ngen at install.
- **Reliability:** SHA-256-verified packages, staged installs, atomic state writes with `.bak` fallback, offline catalog cache, single instance, faulty modules are contained and reported.

## Layout

| Path | What |
|---|---|
| `src/SwissTechKnife` | Host app (shell, store, module runtime) |
| `src/SwissTechKnife.Sdk` | Module contract + design system (`Theme/`) referenced by module repos |
| `src/SwissTechKnife.Core` | Catalog, install state, package installer (no UI) |
| `samples/` | Two sample modules used to exercise the host |
| `catalog/catalog.json` | Module catalog (source) |
| `assets/brand` | Logo rasters generated from `Theme/Brand.xaml` |
| `docs/` | [Design system](docs/DESIGN_SYSTEM.md) · [Writing a module](docs/MODULES.md) |

## Build

```powershell
powershell -File scripts/build.ps1              # feed + build + tests
powershell -File scripts/build.ps1 -Installer   # + artifacts/installer/SwissTechKnife-<ver>-setup.exe (Inno Setup 6)
powershell -File scripts/render-brand.ps1       # after editing the logo
```

Run: `src/SwissTechKnife/bin/Release/net48/SwissTechKnife.exe`.

User data lives in `%LOCALAPPDATA%\SwissTechKnife` (`modules/`, `data/<module>/`, `state.json`, `settings.json`, `logs/host.log`).
The catalog defaults to the published `catalog/catalog.json` of this repository (raw GitHub URL), cached for offline use; before anything is cached, the copy bundled next to the exe (`feed/catalog.json`) is the fallback. Override with `"catalog"` in `settings.json` or the `STK_CATALOG` environment variable (path or https URL).

## Self-update

The catalog's optional `"host"` entry advertises the latest SwissTechKnife:

```json
"host": {
  "version": "0.4.0",
  "installer": "https://github.com/luingry/SwissTechKnife/releases/download/v0.4.0/SwissTechKnife-0.4.0-setup.exe",
  "sha256": "<64 hex>",
  "size": 2123456,
  "releaseNotes": [ { "version": "0.4.0", "date": "2026-10-20", "notes": [ "..." ] } ]
}
```

When a check (Configurações › Verificar atualizações) finds a newer version, Configurações shows a notice and a toast; *Atualizar* shows the notes, downloads the installer to `%LOCALAPPDATA%\SwissTechKnife\downloads`, verifies SHA-256 (again right before running it) and starts it with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /STKUPDATE …`. The installer (`[Code]` in `installer/SwissTechKnife.iss`) writes a signal file once it runs elevated, the app then exits, the installer waits for the app's mutex, installs, and relaunches the app (success or failure). The next start reads `downloads/host-update.json` and reports the outcome; on failure the installer log path is shown.

```powershell
powershell -File scripts/publish-host.ps1 -Notes "Nota 1.", "Nota 2."   # installer + GitHub release v<ver> + catalog "host" entry
powershell -File scripts/test-host-update.ps1                          # end-to-end update from a local feed (per-user test install, no UAC)
```

Installs fetch the published catalog by default, so pushing `catalog/catalog.json` is what announces an update (the bundled `feed/` copy only serves offline first starts).

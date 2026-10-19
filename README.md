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
The catalog defaults to `feed/catalog.json` next to the exe; override with `"catalog"` in `settings.json` or the `STK_CATALOG` environment variable (path or https URL).

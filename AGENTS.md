## Conventions

- Everything in English: code, comments, docs, commit messages, README, catalog text and release notes (technical docs live in `docs/`).
- UI text is localized: English (default) and Brazilian Portuguese. Host strings live in `src/SwissTechKnife.Core/Strings.cs` (add every key to both tables; the tests fail on a missing translation) and are used from XAML with `{infra:Loc Key}`, from code with `Strings.Get`/`Strings.Format`. Never hardcode UI text.
- UI uses only design-system tokens and styles from `src/SwissTechKnife.Sdk/Theme` (see `docs/DESIGN_SYSTEM.md`). No solid borders, no native-looking controls, every state transition animated.
- Performance first: no work before first paint, nothing on the UI thread that touches disk/network, prefer opacity/transform animations, freeze brushes and geometries.
- The SDK is a public contract for module repositories: only additive changes within 1.x; bump `SdkInfo.ContractVersion` for breaking changes.
- Run `powershell -File scripts/build.ps1` before handing off; record non-trivial fixes in `ERRORS.md`.
- Logo changes: edit `src/SwissTechKnife/Theme/Brand.xaml`, then `scripts/render-brand.ps1`.

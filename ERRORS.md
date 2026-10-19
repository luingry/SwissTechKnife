# Resolved errors

## 2026-10-19 — Startup crash: `'{DependencyProperty.UnsetValue}' não é um valor válido para a propriedade 'Foreground'` / `Não é possível encontrar o recurso denominado 'Motion.Fast'`

- Symptom: the main window failed to load; the process stayed alive with no window (the exception was swallowed by the dispatcher handler).
- Root cause: theme files were siblings in one `MergedDictionaries` list. A `StaticResource` inside a style/template trigger or storyboard is resolved without the parent chain, so it cannot see keys from a *sibling* merged dictionary (`Tokens.xaml`).
- Solution: every theme file merges `Tokens.xaml` itself; `Shell.xaml` merges the whole `Theme.xaml`; `App.xaml` merges only `Brand.xaml` + `Shell.xaml`. A main-window load failure now shows a message and exits instead of lingering.
- Diagnose: `SwissTechKnife.exe --diag-resources` logs every resource that fails to resolve to `logs/host.log`.

## 2026-10-19 — Sample module projects: `Could not find a part of the path ...\samples\src\...`

- Cause: relative paths in `samples/Directory.Build.props` resolve from each project's folder, not from the props file.
- Solution: prefix with `$(MSBuildThisFileDirectory)`.

## 2026-10-19 — CS0120 on `FileMode` in a module view

- Cause: a control named `FileMode` (`x:Name`) shadowed `System.IO.FileMode`.
- Solution: don't name controls after BCL types (renamed to `FileTab`).

## 2026-10-19 — `XamlReader.Parse` of Brand.xaml: `DrawingGroup.Children` threw

- Cause: a `<StaticResource>` element reused a `DrawingGroup` as a child of another group.
- Solution: share a frozen `DrawingCollection` (`Brand.MarkShapes`) via `Children="{StaticResource ...}"`.

## 2026-10-19 — Status chips rendered as ovals

- Cause: `Radius.Pill` was 999. WPF does not clamp corner radii to half the element size; it scales oversized radii proportionally, which turns a short pill into an ellipse.
- Solution: chips have a fixed height (22) and `Radius.Pill` is half of it (11). Keep any pill radius ≤ half its height.

## 2026-10-19 — Accents corrupted in catalog.json ("animaÃ§Ã£o")

- Cause: editing the catalog through Windows PowerShell 5.1 JSON/text round-trips re-decoded UTF-8 text as ANSI in some steps.
- Solution: repaired with a Latin-1 → UTF-8 re-decode; edit and format JSON with Node (`JSON.parse`/`JSON.stringify(…, null, 2)`) or the app's own serializer, never with PS 5.1 `ConvertTo-Json`.
- Check: count `Ã`/`Â`/U+FFFD in the file (read as UTF-8) before committing.

## 2026-10-19 — Switch knob stuttered when toggled

- Cause: the switch's bound setter ran disk writes (state/settings), list refreshes (all cards regenerated) and module start/stop on the UI thread during the 220 ms knob animation.
- Solution: switches flip their visual state at once and run the real work via `AfterAnimation.Run` (260 ms later); list views refresh only when their membership actually changes.
## 2026-10-19 — Switch knob jumped at the end of the toggle (stutter, part 2)

- Symptom: after deferring the switch side effects, toggling still looked like a stutter. A per-frame trace (`STK_FRAMEDIAG=1`) showed the knob `TranslateTransform.X` stayed at 0 for ~200 ms and then jumped to 16, while the track color faded smoothly.
- Cause: VisualStateManager *generated* transitions (`GeneratedDuration`) do not interpolate properties of Freezable targets such as `TranslateTransform`/`ScaleTransform`; they only apply the new state at the end. Same for button/card press scale and the radio dot.
- Solution: explicit `VisualTransition From/To` storyboards animate the transforms (switch knob slide and press stretch, radio dot, button and card press). Animations also run at the display refresh rate (`FrameRate.MatchDisplay`, 74–75 Hz here) instead of WPF's fixed 60 fps.
- Prevention: any transform in a VSM state needs an explicit transition storyboard; verify motion with `STK_FRAMEDIAG=1` (it logs the knob position per frame and frame gaps).
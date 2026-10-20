# Resolved errors

## 2026-10-19 — Host self-update design: avoid the micesmooth OTA failures

- Context: micesmooth's OTA (D:\Dev\micesmooth\ERRORS.md, 2026-10-01) lost the installer exit code (`%%ERRORLEVEL%%` in a .bat) and skipped waits (`timeout.exe` without a console / GNU `timeout` first in PATH), so setup ran while the app still held its files.
- Solution here: no batch file at all. The app starts the Inno installer directly (shell execute, so Inno raises UAC itself and `ExecAsOriginalUser` relaunches non-elevated). The installer's `[Code]` writes `/STKSIGNAL` = `started` only in the instance that installs (`IsAdmin` on the admin build: with UAC a non-elevated instance may run first), the app exits only after seeing it, and `InitializeSetup` polls the app's single-instance mutex (`/STKMUTEX`, up to 60 s, then 1 s for process teardown) before any file is replaced; `/CLOSEAPPLICATIONS` is the fallback. `DeinitializeSetup` writes `ok`/`failed` and relaunches `/STKAPP` either way, so the user is never left without the app. An installer that exits before `started` (UAC declined) is reported with its real exit code while the app keeps running. Downloads retry 3× and the hash is re-checked right before running.
- Testing: `scripts/test-host-update.ps1` runs the whole flow on a per-user `/DTestInstall` build with an isolated `STK_HOME` (which also isolates the single-instance mutex), driving the UI through UI Automation.

## 2026-10-19 — PowerShell 5.1 script fails to parse: `')' ausente na chamada do método` / garbled `→`

- Cause: Windows PowerShell 5.1 reads a `.ps1` without a BOM as ANSI, so UTF-8 characters (`→`, `—`, `ç`) in strings become multi-byte garbage that can break quoting.
- Solution: keep scripts ASCII-only (build non-ASCII UI strings with `[char]0xE7`), or save them as UTF-8 with BOM.
- Related trap: static `[regex]::Replace($s, $pattern, $evaluator, 1)` binds `1` to `RegexOptions` (IgnoreCase), not a count; use `[regex]::Match` + `Substring`, or an instance `Regex.Replace(input, evaluator, count)`.

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

## 2026-10-20 — Language combo box items read as "[pt-BR, Português (Brasil)]" to UI Automation
- Symptom: the language options showed correctly (`DisplayMemberPath`), but screen readers and UI Automation saw each item as `[pt-BR, Português (Brasil)]`.
- Cause: a `ComboBoxItem`'s accessible name comes from the item's `ToString()`, and the items were `KeyValuePair<string, string>`.
- Solution: bind the combo to small option classes (`LanguageOption`, `FrequencyOption`) whose `ToString()` returns the display text.
- Prevention: never use `KeyValuePair`/tuples as items of a selectable control; give options a class with a meaningful `ToString()`.

## 2026-10-20 — Self-update stuck on "Waiting for Windows permission to install…"
- Symptom: after confirming UAC the progress card stayed at 100 % forever. The installer log showed "App still running after 60 s", then it installed anyway and wrote the `ok` signal, but the old app neither exited nor reported a cancel.
- Cause: `Process.Start` (UseShellExecute + elevation) never returned, so the code never got to polling the signal file. Seen when the app ran inside another app's MSIX package container (launched from the Claude desktop app: the installer log's "Original Setup EXE" pointed into `Packages\Claude_…\LocalCache`).
- Solution: `HostUpdater.StartInstallerAsync` launches on its own background STA thread and polls the signal file while the launch is still pending; the launch result only matters when it fails (UAC declined → canceled) or the installer quits. `App.ExitForUpdate` also ends the process after 15 s if a module hangs the normal exit.
- Prevention: never make the update hand-off depend on a shell call returning; the installer's signal is the source of truth.

## 2026-10-20 — Self-update installer aborted: "Some applications could not be shut down"
- Symptom: the app exited for the update and came back on the old version; the record said `signal: failed`. Installer log: "RestartManager found an application using one of our files: SwissTechKnife", then "Some applications could not be shut down… Defaulting to Abort".
- Cause: the installer only waited for the app's mutex plus a fixed 1 s. The mutex is released in `OnExit`, but the process lives on a little longer (modules and the runtime tearing down). Restart Manager cannot close a process with no window, and with `/SUPPRESSMSGBOXES` the Abort/Retry/Ignore prompt defaults to Abort.
- Solution: `PrepareToInstall` in `installer/SwissTechKnife.iss` (it runs before Restart Manager) waits up to 30 s until `{app}\SwissTechKnife.exe` can be opened for writing, which means no process maps it any more. The app-side 15 s exit watchdog covers a process that never ends.
- Prevention: the installer that runs is the new version's, so installer-side waits fix updates from any older version. Never rely on a fixed sleep for "the process is gone".

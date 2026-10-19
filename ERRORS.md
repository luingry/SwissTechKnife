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

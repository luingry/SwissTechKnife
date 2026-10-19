# SwissTechKnife Design System

Source of truth: `src/SwissTechKnife.Sdk/Theme/`. The host merges `Theme.xaml` once, so every module page inherits it. Never hard-code a color, size, radius or duration in a view: use a token. If a token is missing, add it to `Tokens.xaml` first.

## Principles

1. **Tone, not strokes.** No solid borders. Elevation is expressed by surface tone (Canvas → Surface1 → … → Surface5). Separators are tonal gaps.
2. **One primary action per surface.** Orange (`Brush.Accent`) marks the main action and the current selection. Nothing else is orange.
3. **Semantic colors are status only.** Success, Info, Warning, Danger are never decoration.
4. **Every state animates.** Hover, pressed, checked, focus, disabled and page/dialog transitions fade or move with the `Motion.*` durations. Opacity and transforms only (GPU-cheap); never animate layout.
5. **Nothing native.** Every control a module may use has an implicit style. A native-looking control is a bug.

## Tokens (`Tokens.xaml`)

| Group | Keys |
|---|---|
| Surfaces | `Canvas #141413` · `Surface1 #1B1A18` (content) · `Surface2 #22211E` (cards, dialogs) · `Surface3` (inputs, hover) · `Surface4` (secondary buttons, popups) · `Surface5` (tracks, tooltips) |
| Text | `Text.Primary` · `Text.Secondary` (paragraphs) · `Text.Tertiary` (captions, labels) · `Text.Disabled` |
| Accent | `Accent #E8743C` · `.Hover` · `.Pressed` · `.Soft` (tinted fills) · `.Focus` (focus ring) · `OnAccent` (text on orange) |
| Status | `Success` `Info` `Warning` `Danger` + `.Soft` fills (chips, banners) |
| Overlays | `Overlay.Hover` / `Overlay.Pressed` (white tints) · `Overlay.Lighten` / `Overlay.Darken` (on accent) · `Scrim` |
| Type | `Font.Text` / `Font.Display` (Segoe UI Variable, one family) · `Font.Icon` (Segoe Fluent Icons) · sizes `Overline 11` `Caption 12` `Body 13` `Subtitle 15` `Title 20` `Display 28` |
| Space | `Space.1…10` on a 4 px grid |
| Radius | `Sm 6` · `Md 10` (controls) · `Lg 14` (cards, panels) · `Xl 20` (dialogs) · `Pill` |
| Size | `Size.Control.Sm 28` · `Size.Control 36` · `Size.Control.Lg 42` · `Size.TitleBar 40` · `Size.Sidebar 236` |
| Motion | `Motion.Fast 120ms` (hover in) · `Motion.Normal 200ms` (hover out, fades) · `Motion.Slow 280ms` (entrances) · `Ease.Out` · `Ease.InOut` · `Ease.Emphasized` |
| Elevation | `Elevation.Popup` (menus, toasts) · `Elevation.Overlay` (dialogs). Only on floating layers. |

Colors (`Color.*`) and brushes (`Brush.*`) exist for every entry; use `Color.*` inside storyboards and `Brush.*` everywhere else.

## Typography (`Typography.xaml`)

`Text.Display` page titles · `Text.Title` dialog titles · `Text.Subtitle` card titles · `Text.Body` · `Text.BodyStrong` values · `Text.Secondary` · `Text.Paragraph` wrapped prose · `Text.Caption` metadata · `Text.Overline` section labels · `Text.Icon` glyphs.

Hierarchy comes from size + weight + tone. Pages have one Display title.

## Components

| Component | Key | Use |
|---|---|---|
| Button | `Button.Primary` | The single main action |
| | `Button.Secondary` (implicit) | Alternatives next to the primary |
| | `Button.Ghost` | Toolbar and low-emphasis actions |
| | `Button.Danger` | Destructive actions; require a second click to confirm |
| | `Button.Icon` / `Button.Icon.Sm` | Icon-only; always set a `ToolTip` |
| Toggle | `Button.Toggle` (implicit `ToggleButton`) | On/off tool |
| Segmented | `Segmented.Container` > `ui:SegmentedPanel` > `Segmented.Item` (RadioButtons) | 2–4 mutually exclusive views; the panel slides one shared indicator to the checked item |
| Switch | `Switch` (CheckBox) | Settings that apply immediately |
| CheckBox / RadioButton | implicit | Forms |
| TextBox | implicit · `TextBox.Search` | `ui:Ui.Placeholder`, `ui:Ui.Icon` |
| ComboBox, Slider, ListBox, ContextMenu, ToolTip, ProgressBar, ScrollViewer | implicit | |
| Card | `Card` (Border) | Grouped content on a page. Never nest cards |
| Chip | `Chip` (Border) | Status labels: `*.Soft` background + matching strong text |

Attached properties (`xmlns:ui="clr-namespace:SwissTechKnife.Sdk;assembly=SwissTechKnife.Sdk"`):
`ui:Ui.Icon` (glyph), `ui:Ui.Placeholder`, `ui:Ui.CornerRadius`, `ui:Ui.HoverBackground`, `ui:Ui.PressedBackground`.

## Icons

- UI chrome: Segoe Fluent Icons glyphs (`Font.Icon`), 14–16 px.
- Module icons: stroke path on a 24×24 grid (catalog `icon`), 1.8 stroke, round caps, drawn in the module's `accent` over its 15% tint. Keep them to one simple idea.

## Layout

- Page padding 36 (store) / 28 (module pages); card padding 18–24; gaps 14–16.
- Module pages fill the content surface below the host header (icon, name, version, Detalhes).
- Empty, loading (skeleton), error and offline states are required for every list.

## Brand

`src/SwissTechKnife/Theme/Brand.xaml` holds the logo as vector drawings (`Brand.Logo`, `Brand.Mark`): a Swiss cross of five modular tiles (one tool, many modules) with an orange core (the host) and a blade for the right arm. `scripts/render-brand.ps1` renders the same drawings into the `.ico`, PNGs and installer art. Edit the XAML, then rerun the script; never edit the rasters.

## Pitfall

A `StaticResource` inside a template trigger or storyboard cannot see a *sibling* merged dictionary. Each theme file merges `Tokens.xaml` itself; keep it that way (see ERRORS.md).

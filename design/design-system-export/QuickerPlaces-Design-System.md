# QuickerPlaces Design System (version 2)

A portable export of the QuickerPlaces design system, for reuse in other apps. Colours are given for Dark and Light themes. Token names like `Bg-Base` map to CSS variables `--Bg-Base` (see the CSS block at the end). The original is a Windows WPF app; the component notes describe the intent, not WPF code.

QuickerPlaces is a Windows desktop app (WPF) for opening saved folders and links in one click. Its look borrows the materials of a 1990s Saab 900: dark green paint, tan leather, silver trim, a black dash with orange-lit needles, and red tail lights. It comes in a **Dark** and a **Light** theme, and the person picks a **highlight colour**. Keep it simple, clean and easy to understand on first use: the car supplies the palette, not decoration.

## Content fundamentals

- **Speak to the person, plainly.** Short second-person sentences that say what happens: "Your saved folders and links, one click away." "Folders you open in File Explorer, day by day."
- **Use everyday words for things.** A place has a *Name* (not an alias), and a *Folder or link* (not a path or URL). Buttons say "Add folder" and "Add link".
- **Sentence case everywhere:** "Add folder", "Hide list", "Track a folder", "Reset to default", "Highlight colour". Labels set in capitals (`label-caps`) are typed in sentence case and capitalised by style.
- **Say where things go and how to get them back:** "Removed "Old drawings". It stays in Recently Deleted for 7 days." with an Undo button.
- **Show shortcuts where they help:** as keycaps beside the thing ("Ctrl F" in the search box, "Ctrl Z" on Undo), or as a hint beside a label ("Ctrl+1 to Ctrl+4 open them from the keyboard").
- **Empty states tell the next step.**
- **British spelling:** Favourite, colour.
- **No emoji, no exclamation marks.**

## Visual foundations

### Colour roles
- **Surfaces:** `Bg-Base` is the window ground (dash black in dark, silver-white in light). Lists and grouped panels sit on `Bg-Panel`; buttons, table headers, chips and the status bar on `Bg-Raised`; inputs on `Bg-Sunken`. The title bar is `Bg-TitleBar`.
- **Lines, not shadows.** `Border-Default` for row dividers and outlines of panels and tables; `Border-Strong` for control outlines.
- **Text:** `Text-Primary` for content, `Text-Secondary` for subheaders, hints, capitals labels and secondary columns, `Text-Tertiary` only for placeholders and keyboard hints.
- **Highlight (the paint):** `Highlight` fills Primary buttons, the header monogram, the lower band of the racing stripe, the selected row and the on-state of segmented controls and checkboxes, with `On-Highlight` text. `Highlight-Text` is the same colour tuned for use as text or icons on the ground (type glyphs, the sort arrow and its underline, "Saved", hover borders). `Highlight-Soft` washes a selected chip. The default is Hull green.
- **Leather (the seats)** is for things the person pinned or visited: favourite cards (`Leather` with `On-Leather` text and a `Leather-Deep` number badge) and the Recents heat scale `Heat-0` to `Heat-4` plus `Heat-Untracked`. Don't use it for buttons or selection.
- **Signal (the needles)** marks indicators only: a favourite's star, today's calendar cell, the new-activity dot, the focus ring, warnings.
- **Danger (the tail lights)** is only for errors and removing things.
- **Racing stripe:** every window has a full-width stripe under its header: a 4px `Trim-Stripe` band (silver in dark, near-black in light) over a 4px `Highlight` band. `Trim-Line` rings the monogram.
- **`Brand-Green`** colours the app icon in both themes and never follows the highlight choice. The header monogram follows the highlight.

### Highlight presets
The highlight is a setting with five choices: **Hull green** (default), **Steel blue**, **Tail red**, **Cognac**, and **Windows accent**. Each preset defines four colours per theme (`Preset-<Name>`, `-Hover`, `-Text`, `-Soft`). Choosing one copies its values into `Highlight`, `Highlight-Hover`, `Highlight-Text` and `Highlight-Soft`. Leather, Signal and Danger never change, so the theme keeps its character. White text passes 5.5:1 on every preset fill and 4.5:1 on every hover in both themes. Windows accent takes the system colour: the app uses white `On-Highlight` whenever white reaches 4.5:1 on it (so Windows' default blue gets white text, as in Windows), otherwise black, then darkens or lightens the fill and hover until that text reaches 4.5:1, and pushes the text colour until it reaches 4.5:1 on the ground.

### Type
- **TASA Orbiter** for everything (`--font-sans`): 800 for window titles (`title-window`), 700 in spaced capitals for labels and column headers (`label-caps`, `label-column`), 600 for buttons and setting names, 400 to 500 for text (`body`, `body-strong`). The default text size is 13.5px; place names in the list (15px) and on favourite cards (14.5px) are a step larger, because they are what people scan for.
- **IBM Plex Mono** (`--font-mono`) only for folder paths and links (`path`), so they read as addresses.
- Both are free under the Open Font License and are embedded in the app, not assumed installed (TASA Orbiter as five static weights, since WPF ignores variable-font weight axes).

### Shape and spacing
- **Squared-off, like the car:** 6px corners (`radius-6`) on buttons, inputs, segmented controls and favourite cards; 8px (`radius-8`) on windows, tables and the status bar; 10px (`radius-10`) on grouped panels.
- **Pills only for tracked folders** (`radius-15`); circles only for highlight swatches.
- **Windows:** 18px top, 20px sides, 16px bottom padding; dialogs use 24px sides. Siblings in a row sit 8px apart.
- **Heights:** standard controls 34px, compact controls 28px, favourite cards 32px, table rows 36px (Recents 38px), table headers 34px.

### States
- **Hover:** a control's border turns `Highlight-Text`; the Primary button fills `Highlight-Hover`; a list row fills `Bg-RowHover`.
- **Selected:** a list row fills `Highlight` with `On-Highlight` text and icons; a chip gets a `Highlight-Text` border on `Highlight-Soft`; a segment or checkbox fills `Highlight`.
- **Sorted column:** `Text-Primary` label, a `Highlight-Text` arrow and a 2px `Highlight-Text` underline.
- **Focus:** a solid 2px `Signal` ring with a 2px gap on every focusable control.
- **Disabled:** `Text-Tertiary` label on a `Border-Default` outline.

### Elevation
- Flat and bordered. `Shadow-Card` lifts favourite cards slightly; `Shadow-Primary` gives the Primary button a chrome top edge. `Shadow-Window` is for mockups only.

### Layout
- **Main window,** top to bottom: title bar; header (monogram, name, subheader, Recents / Add folder / Add link / Settings); racing stripe; Favourites (label, shortcut hint, cards); All places row (label, count, search, the Options menu with Recently Deleted, Places file, Import and Export, Hide list); the places table (Name, Folder or link, Last opened, Opens; no Type column, since the icon shows it); the status bar when there is a message.
- **Primary buttons:** Recents, Add folder and Add link in the main window header; one per dialog (Save or the confirm action).
- **Settings** groups options under capitals labels (Appearance, Shortcut, Tray) separated by `Border-Default` lines, with Cancel and Save at the bottom right.

## Iconography

- **Simple stroke icons,** 16px on a 16px grid, 1.6px stroke, round caps and joins, drawn in the current text colour: folder, globe (link), star, search, clock (Recents), sliders (Settings), menu, close, plus, tick, arrows and chevrons, sun, moon, monitor. The app needs a matching set as XAML path geometry; the Segoe icon fonts don't fit this style.
- A type glyph sits 9px left of a name in `Highlight-Text` (on a selected row, `On-Highlight`). On a favourite card it takes `On-Leather` at 15px. The icon is how a folder is told from a link; there is no Type column.
- A favourite's star is filled `Signal`; on a hovered or selected row that isn't a favourite, an outline star appears in `Text-Tertiary`.
- **The app icon** (Logos group) is a `Brand-Green` tile with an off-white "QP" in TASA Orbiter ExtraBold and a thin silver edge: the monogram in Hull green, whatever highlight is chosen.

## Built in the app

The app implements version 2 (branch `claude/ui-refresh`, 2026-09-28): Dark, Light and Match Windows; the five highlight presets including Windows accent; TASA Orbiter and IBM Plex Mono embedded; the stroke icon set as XAML geometry; the new app icon; every window and dialog restyled. Component previews here are still HTML renditions of the design, not WPF controls. ComboBox and MessageBadge were not on the canvas and are restyled here to match.

**Where WPF differs from this system:**
- Labels in capitals are typed in capitals (WPF has no text-transform) and have no letter-spacing (a TextBlock has none), so `label-caps` and `label-column` render without their 0.12em tracking.
- Tables have square corners; rounding them would need a clipped DataGrid template.
- The Recents day cells are 12px at a 14px pitch rather than 11/13, because the calendar's layout maths is built on that pitch.
- The focus ring on a Recents day cell is a tighter 1.5px `Signal` outline 2px outside the cell, so it doesn't cover the neighbouring days.

**Changed while building, to meet this system's contrast rules:**
- `Preset-Green-Hover` dark is now #32846e (was #33866f, 4.4:1 with white text); every preset's hover now reaches 4.5:1 with `On-Highlight`.
- `Text-Tertiary` dark is now #848b88 (was #7f8683, 4.2:1 on `Bg-Raised`).
- `Preset-Red-Soft` light is 10%, like the other presets' washes.
- Windows accent text prefers white once white reaches 4.5:1 (see Highlight presets).
- A 'Paused' label on a selected tracked-folder chip uses `Text-Secondary`, since `Text-Tertiary` on the `Highlight-Soft` wash is only about 3.4:1.
- `Heat-Untracked` days keep a `Border-Default` outline so they stay visible on `Bg-Panel` in dark.

**Changed after trying the build (app commit `4a29e79`):**
- Recents, Add folder and Add link are all Primary buttons, and Settings moved out of the Options menu to a `Button` after Add link.
- The header monogram fills with `Highlight` rather than `Brand-Green`; the app icon keeps Brand green.
- The sort arrow and its underline use `Highlight-Text` rather than `Signal`.
- The trim band became a racing stripe: `Trim-Stripe` (new; replaces `Trim-Band`) over `Highlight`, 4px each.


## Colour tokens

| Token | Dark | Light | Usage |
|---|---|---|---|
| `Bg-Base` | `#121514` | `#eceeed` | The window ground: dash black in dark, studio silver-white in light. |
| `Bg-Panel` | `#171b1a` | `#f7f8f7` | Lists, tables and grouped panels (the Recents calendar). |
| `Bg-Raised` | `#1f2422` | `#ffffff` | Buttons, table headers, chips, the status bar, the count badge, keycaps. |
| `Bg-Sunken` | `#0e1110` | `#f4f5f4` | Text inputs: the search box and the shortcut box. |
| `Bg-TitleBar` | `#1b1f1e` | `#e1e4e3` | The window title bar. |
| `Bg-RowHover` | `#1b201f` | `#eef1f0` | A list row under the pointer. |
| `Border-Default` | `#2a302e` | `#d8dcda` | Row dividers, panel and table outlines, section dividers in dialogs. |
| `Border-Strong` | `#3d4542` | `#b3bab7` | Outlines of interactive controls: buttons, inputs, chips, segmented controls. Only about 1.9:1 (dark) and 1.7:1 (light) against Bg-Base, so a control is never marked by its outline alone: it also has a Bg-Raised or Bg-Sunken fill and a label or icon. |
| `Text-Primary` | `#ebe9e3` | `#161a19` | Everything a person reads, on every surface. |
| `Text-Secondary` | `#a7adaa` | `#515956` | Subheaders, hints, labels in capitals, column headers, the Type and path columns. 6:1 or better on Bg-Base, Bg-Panel and Bg-Raised in both themes. |
| `Text-Tertiary` | `#848b88` | `#646c69` | Placeholders, keyboard hints, 'Paused', calendar labels. 4.5:1 or better on Bg-Base, Bg-Panel, Bg-Raised and Bg-RowHover in both themes (dark was #7f8683 until the app build, which fell to 4.2:1 on Bg-Raised). Still only for hints and placeholders, never for content; on a Highlight-Soft wash use Text-Secondary. |
| `Trim-Stripe` | `#aeb4b1` | `#161a19` | The upper band of the racing stripe under each window's header: 4px, full width, silver in dark and near-black in light, with a 4px Highlight band straight below it. Replaced Trim-Band (a 7px dark band with a 1px Trim-Line pinstripe) after the app build. |
| `Trim-Line` | `#aeb4b1` | `#c6cbc8` | The 1px silver ring around the monogram. |
| `Brand-Green` | `#1f5c4d` | `#1f5c4d` | The app icon (and the tiny monogram in the title bar, which is the app icon), in both themes. Fixed: it does not follow the highlight choice. The header monogram used it until the app build; it now fills with Highlight. |
| `Highlight` | `{Preset-Green}` | `{Preset-Green}` | The user's highlight colour (default Hull green): Primary button fill (including Recents, Add folder and Add link in the main window header), the header monogram, the lower band of the racing stripe, selected row, on-state of segmented controls and checkboxes. The app swaps it for another preset's value at runtime. |
| `Highlight-Hover` | `{Preset-Green-Hover}` | `{Preset-Green-Hover}` | Primary button under the pointer. |
| `Highlight-Text` | `{Preset-Green-Text}` | `{Preset-Green-Text}` | Highlight used as text or an icon on the ground: type glyphs in lists, the sort arrow and its 2px underline, 'Saved', success ticks, hover borders, the selected-week outline. 5.5:1 or better on Bg-Base in both themes. |
| `Highlight-Soft` | `{Preset-Green-Soft}` | `{Preset-Green-Soft}` | A wash of the highlight behind a selected chip. |
| `On-Highlight` | `#ffffff` | `#ffffff` | Text and icons on Highlight, including the monogram's 'QP'. 5.5:1 or better on every preset's fill and 4.5:1 or better on every preset's hover, in both themes. For Windows accent the app picks white when white reaches 4.5:1, otherwise black. |
| `Preset-Green` | `#2a7563` | `#1f5c4d` | Hull green preset (default): fill. |
| `Preset-Green-Hover` | `#32846e` | `#174a3e` | Hull green preset: fill under the pointer. Dark was #33866f until the app build; that gave white text only 4.4:1. |
| `Preset-Green-Text` | `#7cc7ad` | `#1f6b58` | Hull green preset: text and icons on the ground. |
| `Preset-Green-Soft` | `rgba(124, 199, 173, 0.14)` | `rgba(31, 92, 77, 0.10)` | Hull green preset: wash. |
| `Preset-Blue` | `#36679a` | `#2f5d8a` | Steel blue preset: fill. |
| `Preset-Blue-Hover` | `#4077ae` | `#264d73` | Steel blue preset: fill under the pointer. |
| `Preset-Blue-Text` | `#9cc0e6` | `#2c5a86` | Steel blue preset: text and icons on the ground. |
| `Preset-Blue-Soft` | `rgba(156, 192, 230, 0.14)` | `rgba(47, 93, 138, 0.10)` | Steel blue preset: wash. |
| `Preset-Red` | `#b1372f` | `#a8322b` | Tail red preset: fill. |
| `Preset-Red-Hover` | `#c4453b` | `#8f2923` | Tail red preset: fill under the pointer. |
| `Preset-Red-Text` | `#f08f84` | `#a8322b` | Tail red preset: text and icons on the ground. |
| `Preset-Red-Soft` | `rgba(240, 143, 132, 0.14)` | `rgba(168, 50, 43, 0.10)` | Tail red preset: wash. |
| `Preset-Cognac` | `#8f5a33` | `#8a5530` | Cognac preset: fill. |
| `Preset-Cognac-Hover` | `#a0683e` | `#734526` | Cognac preset: fill under the pointer. |
| `Preset-Cognac-Text` | `#dcaa7d` | `#86522e` | Cognac preset: text and icons on the ground. |
| `Preset-Cognac-Soft` | `rgba(220, 170, 125, 0.14)` | `rgba(138, 85, 48, 0.10)` | Cognac preset: wash. |
| `Signal` | `#f0902f` | `#c25e0c` | Gauge orange, for indicators only: favourite stars, today's calendar cell, the new-activity dot, the keyboard focus ring, warnings. (The sort arrow used it until the app build; it now follows the highlight.) About 4.3:1 on white in light, so use it for icons, lines and bold text there, not body text. |
| `Danger` | `#ec6a5e` | `#b3261e` | Tail red, for errors and removing things only. 5.7:1 on Bg-Base in dark, 6.5:1 on Bg-Raised in light. |
| `Info` | `#9cc0e6` | `#2c5a86` | Information messages (the info badge). |
| `Leather` | `#c28a5c` | `#c28a5c` | Tan leather: favourite cards and the visits bars in Recents. Same in both themes. |
| `Leather-Deep` | `#7a4c2b` | `#8e5c36` | The number badge on a favourite card. |
| `On-Leather` | `#26170d` | `#26170d` | Text and icons on Leather. About 6:1. |
| `On-Leather-Badge` | `#f6e7d6` | `#f6e7d6` | The number on a favourite card's Leather-Deep badge. 4.7:1 or better. |
| `Heat-0` | `#1c211f` | `#ffffff` | Recents calendar: a tracked day with no visits (drawn with a Border-Default outline). |
| `Heat-1` | `#3a2e24` | `#f0e0cf` | Recents calendar: fewest visits. |
| `Heat-2` | `#6a4930` | `#ddb48d` | Recents calendar: some visits. |
| `Heat-3` | `#a06a40` | `#b97f4d` | Recents calendar: many visits. |
| `Heat-4` | `#d9a068` | `#84512c` | Recents calendar: most visits. Lightest in dark, darkest in light, so more visits always means more contrast with the ground. |
| `Heat-Untracked` | `#191d1c` | `#e2e5e4` | Recents calendar: a day outside tracked history, including future days. Drawn with a Border-Default outline, because in dark the fill alone is almost Bg-Panel. |

## Typography

- Sans: `"TASA Orbiter", "Segoe UI", sans-serif`
- Mono: `"IBM Plex Mono", Consolas, monospace`

| Style | Family | Size | Weight | Line height | Usage |
|---|---|---|---|---|---|
| `title-window` | sans | 23px | 800 | 1.1 | The name at the top of a window: QuickerPlaces, Recents. |
| `title-section` | sans | 20px | 600 | 1.15 | The heading over a result, such as the Recents period. |
| `label-caps` | sans | 12px | 700 | 1.2 | Section labels, set in capitals in Text-Secondary: Favourites, All places, Tracked folders, Appearance. (letter-spacing 0.12em, ) |
| `label-column` | sans | 11.5px | 700 | 1.2 | Table column headers, in capitals. (letter-spacing 0.12em, ) |
| `label-field` | sans | 14px | 600 | 1.3 | The name of a setting above its control. |
| `body` | sans | 13.5px | 400 | 1.4 | The window default: subheaders, table cells, messages. |
| `body-strong` | sans | 15px | 500 | 1.4 | A place's name in the list, larger than the other columns so names are what the eye finds first. |
| `button` | sans | 13.5px | 600 | 1.2 | Buttons, segmented controls. Compact buttons use 13px. |
| `favourite` | sans | 14.5px | 600 | 1.2 | The name on a favourite card. |
| `hint` | sans | 12.5px | 400 | 1.4 | Help text under a setting; keyboard hints beside a label. |
| `calendar` | sans | 10.5px | 400 | 1 | Month and weekday labels on the Recents strip. |
| `path` | mono | 12.5px | 400 | 1.4 | Folder paths and links in tables and chips. |

## Spacing

| Token | Value | Usage |
|---|---|---|
| `space-2` | `2px` | Gap between calendar cells and week columns. |
| `space-4` | `4px` | Gap between legend cells. |
| `space-6` | `6px` | Gap between keycaps; icon-to-label gap in compact controls. |
| `space-8` | `8px` | Gap between buttons in a row, between favourite cards, and between chips. |
| `space-10` | `10px` | Label-to-hint gap in a row; space above a table or status bar. |
| `space-12` | `12px` | Horizontal cell padding; gap between a section label and its content. |
| `space-14` | `14px` | Horizontal padding of a standard button; gap between the monogram and the title. |
| `space-16` | `16px` | Space above the racing stripe; the bottom window padding. |
| `space-18` | `18px` | Top window padding; space above the All places row. |
| `space-20` | `20px` | Side window padding; space around dividers in dialogs. |
| `space-24` | `24px` | Dialog side padding. |

## Corner radius

| Token | Value | Usage |
|---|---|---|
| `radius-2` | `2px` | Recents calendar cells. |
| `radius-4` | `4px` | Keycaps and the favourite number badge. |
| `radius-6` | `6px` | Buttons, inputs, segmented controls and favourite cards. |
| `radius-8` | `8px` | Windows, tables, the status bar and the monogram tile. |
| `radius-10` | `10px` | Grouped panels, such as the Recents calendar. |
| `radius-15` | `15px` | Tracked-folder chips (a pill at 30px tall). |
| `radius-20` | `20px` | Highlight swatches in Settings (40px circles). |

## Shadows

| Token | Value | Usage |
|---|---|---|
| `Shadow-Card` | `inset 0 1px 0 rgba(255, 255, 255, 0.22), 0 1px 2px rgba(0, 0, 0, 0.22)` | Favourite cards: a faint top highlight and a small drop shadow. |
| `Shadow-Primary` | `inset 0 1px 0 rgba(255, 255, 255, 0.2)` | The chrome-edge highlight along the top of the Primary button. |
| `Shadow-Window` | `0 18px 40px rgba(20, 24, 22, 0.28), 0 0 0 1px rgba(0, 0, 0, 0.18)` | Mockups only: a window on a backdrop. Windows draws the real one. |

## Components

### ActivityCalendar

The Recents year strip: one 11px square per day in week columns, shaded in leather tones by how often tracked folders were visited.

- **Cells:** 11 × 11, `radius-2`, 2px apart (13px pitch). `Heat-0` (with a `Border-Default` outline) for a tracked day with no visits, `Heat-1` to `Heat-4` for more visits, `Heat-Untracked` before tracking began, a dashed `Border-Default` outline for days after today.
- **Today** gets a 1.5px `Signal` ring. **The period shown** in the table below is outlined in `Highlight-Text` (a whole week column in Week mode).
- **Labels:** Mon / Wed / Fri and month names in the `calendar` style, `Text-Tertiary`.
- **Panel:** `Bg-Panel`, `Border-Default`, `radius-10`, 12 × 16 padding. Its top row holds a "Year activity" `label-caps` label, a compact `SegmentedControl` (Week / Month / Day), a "Fewer visits … More" legend, previous/next icon buttons, and the year.
- **Visits bars** in the table below use `Leather` on a `Border-Default` track, 70 × 8px, `radius-2`.
- **The consumer provides** each day's intensity and the selected period. The preview shows example data for 20 weeks; the real strip shows the whole year.

### AppHeader

The top of a QuickerPlaces window: the header with monogram, name, subheader and main actions, then the racing stripe.

- **Monogram:** 40 × 40, `Highlight`, `radius-8`, a faint inner edge and a 1px `Trim-Line` ring; "QP" in TASA Orbiter 800, 16px, `On-Highlight`. It follows the highlight choice; the title bar shows the app icon, which stays `Brand-Green`.
- **Name and subheader:** 14px right of the monogram. The name uses `title-window`; the subheader is `body` in `Text-Secondary`, one line, ending in "…" if it doesn't fit. It mentions the global shortcut only when one is set.
- **Actions,** right-aligned 8px apart: Recents (a `PrimaryButton` with a clock icon and a 7px `Signal` dot, ringed in `Highlight`, when there is new activity), Add folder and Add link (`PrimaryButton`s), then Settings (a `Button` with the sliders icon).
- **Racing stripe:** two full-width 4px bands, `Trim-Stripe` (silver in dark, near-black in light) over `Highlight`, 16px below the header and 14px above what follows. Every window has one under its header; it bleeds past the window padding to both edges.
- **Window padding:** 18px top, 20px sides, 16px bottom.
- **The consumer provides** the app name, subheader text and commands.

### Banner

A message pinned above a window's content when something needs attention. Restyled to match the Saab direction; it was not drawn on the canvas.

- **Look:** `Bg-Raised`, `radius-8`, 10px padding (14px on the left), a 1px outline in the status colour, a 16px icon in the same colour, and the message in `Text-Primary` (the colour sits on the outline and icon, not the text, so the text stays readable).
- **Warning** (unsaved changes): `Signal` outline and triangle icon; actions Retry (compact `PrimaryButton`) and Show data folder.
- **Error:** `Danger` outline and circle icon; action Retry save.
- **Behaviour:** takes no space when hidden and never takes focus when it appears.
- **The consumer provides** the message and actions. The wording in the preview is a placeholder.

### Button

The standard button for every action that isn't a main one (Settings in the header, Cancel, Hide list and the rest): `Bg-Raised` fill, `Border-Strong` outline, `radius-6`, `button` text.

- **Sizes:** standard 34px tall with 14px side padding; compact (`--sm`) 28px with 10px padding and 13px text, for rows inside panels, tables and the status bar; icon-only 34 × 34 or 28 × 28.
- **Icon:** optional 16px stroke icon 7px before the label ("Add folder" with a plus).
- **Shortcut:** a compact button may carry a keycap after its label ("Undo" + `Ctrl Z`).
- **States:** hover turns the outline `Highlight-Text`; keyboard focus draws a 2px `Signal` ring 2px outside; disabled shows `Text-Tertiary` on a `Border-Default` outline.
- **The consumer provides** the label in sentence case, the command, and an `aria-label` / `AutomationProperties.Name` for icon-only buttons.
- **Don't** use it for a main action: that is `PrimaryButton`.

### Checkbox

An on/off option with its label to the right.

- **Look:** a 16px box, filled `Highlight` with a white tick when on; the label in `body` 10px to its right. Under a group, an optional `hint` in `Text-Secondary`.
- **Used for:** the tray options in Settings and "Group by folder level" in Recents.
- **WPF:** needs its own template (box, tick path, `Highlight` fill); version 1 left CheckBox to the stock template.
- **The consumer provides** the label, the bound value, and any hint.

### ComboBox

A dropdown picker for a longer list of choices, such as the roll-up in Folder tracking settings. Restyled here to match the Saab direction; it was not drawn on the canvas.

- **Closed:** 34px, `Bg-Raised`, `Border-Strong` outline, `radius-6`, the value in `body` and a chevron in `Text-Secondary`. Hover turns the outline `Highlight-Text`.
- **Open:** a `Bg-Raised` menu 4px below, `Border-Strong` outline, `radius-8`, 4px padding, no shadow. Items are 30px with `radius-6`: the chosen one fills `Highlight` with `On-Highlight` text; the one under the pointer fills `Bg-RowHover`.
- **Prefer a `SegmentedControl`** when there are three or fewer choices.
- **The consumer provides** the items and the bound selection. The option names in the preview are placeholders.

### FavouriteCard

A favourite place as a small tan-leather card above the places list; clicking it opens the place.

- **Look:** 32px tall, `radius-6`, `Leather` fill, `On-Leather` text in the `favourite` style (14.5px, 600), `Shadow-Card`. No border, no stitching.
- **Contents, left to right:** a 22px `Leather-Deep` number badge (`radius-4`, `On-Leather-Badge` numeral, 12px bold) showing its Ctrl+number shortcut; the type icon (folder or globe) at 15px in `On-Leather`; the name.
- **Row:** cards wrap with 8px gaps under a "Favourites" `label-caps` label and a `hint` naming the shortcuts ("Ctrl+1 to Ctrl+4 open them from the keyboard"). Only the first nine get numbers.
- **Behaviour:** click opens; drag reorders; right-click offers Open, Copy folder or link, Remove from favourites. Focus draws the `Signal` ring.
- **The consumer provides** the name, type, position and open command.
- **Replaces** version 1's pill-shaped FavouriteBubble.

### HighlightPicker

The Settings control for choosing the highlight colour: five round swatches with names underneath.

- **Swatches:** 40px circles (`radius-20`) filled `Preset-Green`, `Preset-Blue`, `Preset-Red` and `Preset-Cognac` for the current theme, plus "Windows accent" drawn as a `Bg-Raised` circle with a `Border-Strong` edge and a four-pane icon. Names sit 6px below in 12px `Text-Secondary`; each column is 64px, 18px apart.
- **Chosen:** a white tick in the swatch and a 2px `Text-Primary` ring outside a 2px `Bg-Base` gap.
- **Effect:** choosing one copies that preset into `Highlight`, `Highlight-Hover`, `Highlight-Text` and `Highlight-Soft` straight away, before Save, so the person sees the result. Cancel restores the previous choice.
- **Above it:** a "Highlight colour" `label-field` and a `hint`: "Colours the main buttons, the QP badge, selected rows, switches and the sort arrow."
- **Keyboard:** one radio group; arrow keys move the choice.
- **The consumer provides** the current preset and the change handler.

### MessageBadge

The 36px round badge that opens a message dialog and tells its kind at a glance. Restyled to match the Saab direction; it was not drawn on the canvas.

- **Look:** a `Bg-Raised` circle with a 2px outline and one bold 16px character, both in the kind's colour: Info "i" in `Info`, Warning "!" in `Signal`, Error "×" in `Danger`, Question "?" in `Highlight-Text`.
- **Why outlined:** version 1 put white characters on bright fills, which failed contrast (1.7–2.8:1). Coloured characters on `Bg-Raised` clear 4.3:1 or better in both themes, well above the 3:1 needed for an icon-like mark.
- **Layout:** the message sits 14px to the right in 14px text, wrapping.
- **The consumer provides** the kind and the message. The message in the preview is a placeholder.

### PlacesGrid

The list of saved places: a table on `Bg-Panel` with a `Bg-Raised` header row, row dividers instead of stripes, and a `Border-Default` outline with `radius-8`.

- **Columns:** Name (2.4*, type icon + name + star), Folder or link (3*, `path` style in IBM Plex Mono), Last opened (1.45*, regional date format), Opens (0.6*, centred). There is no Type column: the folder or globe icon already says it. Widths are proportional so the table scales with the window.
- **Header:** 34px; labels in `label-column` capitals, `Text-Secondary`. The sorted column shows its label in `Text-Primary`, a `Highlight-Text` arrow, and a 2px `Highlight-Text` underline, so both follow the highlight choice.
- **Rows:** 36px, 12px side padding, `Border-Default` divider, long text ends in "…". Names are `body-strong` (15px), a step larger than the other columns; paths are `Text-Secondary`.
- **Hover:** `Bg-RowHover`. **Selected:** `Highlight` fill with `On-Highlight` text and icons (secondary columns at 85%).
- **Star:** a filled `Signal` star follows a favourite's name. On a hovered or selected row that isn't a favourite, an outline star in `Text-Tertiary` (on selection, `On-Highlight` at 80%) offers to add it; clicking toggles, as does Ctrl+D.
- **Type icon:** folder or globe, 16px, `Highlight-Text`, 9px before the name.
- **Keyboard:** Enter opens, F2 renames, Ctrl+E edits the folder or link, Ctrl+D toggles favourite, Delete removes.
- **The consumer provides** the places and the sort. Rows in the preview are examples.
- The same table style serves Recents, Recently Deleted, Import and Export.

### PrimaryButton

The main action on a screen: a `Highlight` fill with `On-Highlight` text and a `Shadow-Primary` chrome edge along the top, otherwise shaped like `Button`.

- **Where:** in the main window header, Recents, Add folder and Add link; in a dialog, one only (Save, Import, Export or the confirm action), placed last, at the bottom right.
- **Colour follows the highlight setting** (Hull green by default). White text passes 5.5:1 on every preset in both themes.
- **States:** hover fills `Highlight-Hover`; focus draws the `Signal` ring; disabled looks like a disabled `Button`.
- **The consumer provides** the label, the command, and whether it is the default for Enter.

### SearchBox

The main window's search field: a 34px `Bg-Sunken` input with a `Border-Strong` outline, `radius-6`, a search icon, and a `Ctrl F` keycap.

- **Width:** 330px, right-aligned in the All places row.
- **Placeholder:** "Search by name, folder or link" in `Text-Tertiary`.
- **While searching,** the keycap gives way to a 24px icon-only clear button; the outline turns `Highlight-Text` while focused.
- **Keyboard:** Ctrl+F focuses; Enter opens the top result; Down moves into the list; Esc clears.
- **The same field style** is used for the shortcut box in Settings, which shows keycaps instead of text.
- **The consumer provides** the bound text and the placeholder.

### SegmentedControl

A row of mutually exclusive choices joined into one control, for switching a mode.

- **Look:** a `Bg-Raised` strip with a `Border-Strong` outline and `radius-6`; segments divided by `Border-Default` lines. The chosen segment fills `Highlight` with `On-Highlight` text; the others are `Text-Secondary`.
- **Sizes:** standard 32px segments (Settings: Light / Dark / Match Windows, each with a sun, moon or monitor icon, stretched to the dialog width); compact 28px (Recents: Week / Month / Day).
- **Keyboard:** it is one radio group; arrow keys move the choice; focus draws the `Signal` ring.
- **The consumer provides** the options, the selected one, and a group name for screen readers.

### StatusBar

A short confirmation at the bottom of a window after Remove, Undo, Copy or a restore.

- **Look:** 38px, `Bg-Raised`, `Border-Default` outline, `radius-8`, 10px above the table.
- **Contents:** a tick in `Highlight-Text`, the message in `body` (one line, ending in "…"), an optional compact Undo `Button` with a `Ctrl Z` keycap, and a compact icon-only dismiss button.
- **Timing:** hides 10 seconds after a message that offers Undo, 8 seconds after any other; stays while the pointer or keyboard focus is inside it. It never takes focus when it appears.
- **Wording:** say what happened and where it went ("It stays in Recently Deleted for 7 days.").
- **The consumer provides** the message, whether Undo is offered, and the commands.

### TrackedFolderChip

A folder that Recents watches, as a selectable pill; choosing one shows its activity below.

- **Look:** 30px tall, `radius-15` (a pill), `Bg-Raised` fill, `Border-Strong` outline, a folder icon in `Text-Secondary`, and the folder path in the `path` style.
- **Selected:** `Highlight-Text` outline on `Highlight-Soft`, with the icon in `Highlight-Text`.
- **Paused:** a "Paused" tag after the path in 12px bold `Text-Tertiary`.
- **Row:** a "Tracked folders" `label-caps` label, the chips 8px apart and wrapping, and a compact "Track a folder" `Button` at the right end.
- **Right-click:** Edit tracking settings…, About folder…, Stop tracking, Delete tracked folder….
- **The consumer provides** the tracked folders, the selection and the paused state. Paths in the preview are examples.

## CSS variables

```css
:root {
  --font-sans: "TASA Orbiter", "Segoe UI", sans-serif;
  --font-mono: "IBM Plex Mono", Consolas, monospace;
  --Brand-Green: #1f5c4d;
  --On-Highlight: #ffffff;
  --Leather: #c28a5c;
  --On-Leather: #26170d;
  --On-Leather-Badge: #f6e7d6;
  --radius-2: 2px;
  --radius-4: 4px;
  --radius-6: 6px;
  --radius-8: 8px;
  --radius-10: 10px;
  --radius-15: 15px;
  --radius-20: 20px;
  --Shadow-Card: inset 0 1px 0 rgba(255, 255, 255, 0.22), 0 1px 2px rgba(0, 0, 0, 0.22);
  --Shadow-Primary: inset 0 1px 0 rgba(255, 255, 255, 0.2);
  --Shadow-Window: 0 18px 40px rgba(20, 24, 22, 0.28), 0 0 0 1px rgba(0, 0, 0, 0.18);
  --Bg-Base: #121514;
  --Bg-Panel: #171b1a;
  --Bg-Raised: #1f2422;
  --Bg-Sunken: #0e1110;
  --Bg-TitleBar: #1b1f1e;
  --Bg-RowHover: #1b201f;
  --Border-Default: #2a302e;
  --Border-Strong: #3d4542;
  --Text-Primary: #ebe9e3;
  --Text-Secondary: #a7adaa;
  --Text-Tertiary: #848b88;
  --Trim-Stripe: #aeb4b1;
  --Trim-Line: #aeb4b1;
  --Highlight: {Preset-Green};
  --Highlight-Hover: {Preset-Green-Hover};
  --Highlight-Text: {Preset-Green-Text};
  --Highlight-Soft: {Preset-Green-Soft};
  --Preset-Green: #2a7563;
  --Preset-Green-Hover: #32846e;
  --Preset-Green-Text: #7cc7ad;
  --Preset-Green-Soft: rgba(124, 199, 173, 0.14);
  --Preset-Blue: #36679a;
  --Preset-Blue-Hover: #4077ae;
  --Preset-Blue-Text: #9cc0e6;
  --Preset-Blue-Soft: rgba(156, 192, 230, 0.14);
  --Preset-Red: #b1372f;
  --Preset-Red-Hover: #c4453b;
  --Preset-Red-Text: #f08f84;
  --Preset-Red-Soft: rgba(240, 143, 132, 0.14);
  --Preset-Cognac: #8f5a33;
  --Preset-Cognac-Hover: #a0683e;
  --Preset-Cognac-Text: #dcaa7d;
  --Preset-Cognac-Soft: rgba(220, 170, 125, 0.14);
  --Signal: #f0902f;
  --Danger: #ec6a5e;
  --Info: #9cc0e6;
  --Leather-Deep: #7a4c2b;
  --Heat-0: #1c211f;
  --Heat-1: #3a2e24;
  --Heat-2: #6a4930;
  --Heat-3: #a06a40;
  --Heat-4: #d9a068;
  --Heat-Untracked: #191d1c;
}
:root[data-theme="light"] {
  --Bg-Base: #eceeed;
  --Bg-Panel: #f7f8f7;
  --Bg-Raised: #ffffff;
  --Bg-Sunken: #f4f5f4;
  --Bg-TitleBar: #e1e4e3;
  --Bg-RowHover: #eef1f0;
  --Border-Default: #d8dcda;
  --Border-Strong: #b3bab7;
  --Text-Primary: #161a19;
  --Text-Secondary: #515956;
  --Text-Tertiary: #646c69;
  --Trim-Stripe: #161a19;
  --Trim-Line: #c6cbc8;
  --Highlight: {Preset-Green};
  --Highlight-Hover: {Preset-Green-Hover};
  --Highlight-Text: {Preset-Green-Text};
  --Highlight-Soft: {Preset-Green-Soft};
  --Preset-Green: #1f5c4d;
  --Preset-Green-Hover: #174a3e;
  --Preset-Green-Text: #1f6b58;
  --Preset-Green-Soft: rgba(31, 92, 77, 0.10);
  --Preset-Blue: #2f5d8a;
  --Preset-Blue-Hover: #264d73;
  --Preset-Blue-Text: #2c5a86;
  --Preset-Blue-Soft: rgba(47, 93, 138, 0.10);
  --Preset-Red: #a8322b;
  --Preset-Red-Hover: #8f2923;
  --Preset-Red-Text: #a8322b;
  --Preset-Red-Soft: rgba(168, 50, 43, 0.10);
  --Preset-Cognac: #8a5530;
  --Preset-Cognac-Hover: #734526;
  --Preset-Cognac-Text: #86522e;
  --Preset-Cognac-Soft: rgba(138, 85, 48, 0.10);
  --Signal: #c25e0c;
  --Danger: #b3261e;
  --Info: #2c5a86;
  --Leather-Deep: #8e5c36;
  --Heat-0: #ffffff;
  --Heat-1: #f0e0cf;
  --Heat-2: #ddb48d;
  --Heat-3: #b97f4d;
  --Heat-4: #84512c;
  --Heat-Untracked: #e2e5e4;
}
```

## Reference CSS (HTML renditions of the components)

```css
@import url("https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500&family=TASA+Orbiter:wght@400..800&display=swap");

/* QuickerPlaces, Saab 900 direction: HTML renditions of the design for previews. Colours come from tokens.css. */

body { margin: 0; padding: 16px; background: var(--Bg-Base); color: var(--Text-Primary); font-family: var(--font-sans); font-size: 13.5px; line-height: 1.4; }
.qp-row { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
.qp-stack { display: flex; flex-direction: column; gap: 12px; }
.qp-note { font-size: 12px; color: var(--Text-Tertiary); }
.qp-caps { font-weight: 700; font-size: 12px; letter-spacing: 0.12em; text-transform: uppercase; color: var(--Text-Secondary); }
.qp-mono { font-family: var(--font-mono); font-size: 12.5px; }
.qp-ico { width: 16px; height: 16px; flex: none; fill: none; stroke: currentColor; stroke-width: 1.6; stroke-linecap: round; stroke-linejoin: round; }
.qp-kbd { font-weight: 600; font-size: 11px; letter-spacing: 0.04em; padding: 1px 6px; border-radius: var(--radius-4); border: 1px solid var(--Border-Strong); color: var(--Text-Secondary); background: var(--Bg-Raised); }
:focus-visible, .is-focus { outline: 2px solid var(--Signal); outline-offset: 2px; }

/* Button */
.qp-btn { font-family: var(--font-sans); font-weight: 600; font-size: 13.5px; height: 34px; box-sizing: border-box; padding: 0 14px; border-radius: var(--radius-6); border: 1px solid var(--Border-Strong); background: var(--Bg-Raised); color: var(--Text-Primary); display: inline-flex; align-items: center; gap: 7px; cursor: pointer; white-space: nowrap; }
.qp-btn:hover, .qp-btn.is-hover { border-color: var(--Highlight-Text); }
.qp-btn:disabled { color: var(--Text-Tertiary); border-color: var(--Border-Default); cursor: default; }
.qp-btn--sm { height: 28px; padding: 0 10px; font-size: 13px; }
.qp-btn--icon { width: 34px; padding: 0; justify-content: center; }
.qp-btn--icon.qp-btn--sm { width: 28px; }
.qp-btn--primary { background: var(--Highlight); border-color: transparent; color: var(--On-Highlight); box-shadow: var(--Shadow-Primary); }
.qp-btn--primary:hover, .qp-btn--primary.is-hover { background: var(--Highlight-Hover); border-color: transparent; }

/* Favourite card */
.qp-fav { height: 32px; box-sizing: border-box; padding: 0 12px 0 5px; border-radius: var(--radius-6); border: 0; background: var(--Leather); color: var(--On-Leather); font-family: var(--font-sans); font-weight: 600; font-size: 14.5px; display: inline-flex; align-items: center; gap: 7px; cursor: pointer; box-shadow: var(--Shadow-Card); }
.qp-fav .qp-ico { width: 15px; height: 15px; }
.qp-fav__num { width: 22px; height: 22px; border-radius: var(--radius-4); background: var(--Leather-Deep); color: var(--On-Leather-Badge); font-size: 12px; font-weight: 700; display: inline-flex; align-items: center; justify-content: center; }

/* Search box and other text inputs */
.qp-search { display: flex; align-items: center; gap: 8px; width: 330px; max-width: 100%; height: 34px; box-sizing: border-box; padding: 0 8px 0 10px; border-radius: var(--radius-6); background: var(--Bg-Sunken); border: 1px solid var(--Border-Strong); color: var(--Text-Tertiary); }
.qp-search input { flex: 1; min-width: 0; border: 0; background: transparent; font-family: var(--font-sans); font-size: 13.5px; color: var(--Text-Primary); outline: none; }
.qp-search input::placeholder { color: var(--Text-Tertiary); }
.qp-search.is-focus { border-color: var(--Highlight-Text); }

/* Segmented control */
.qp-segs { display: inline-flex; border: 1px solid var(--Border-Strong); border-radius: var(--radius-6); overflow: hidden; background: var(--Bg-Raised); }
.qp-seg { height: 32px; padding: 0 14px; border: 0; border-right: 1px solid var(--Border-Default); background: transparent; color: var(--Text-Secondary); font-family: var(--font-sans); font-weight: 600; font-size: 13.5px; display: inline-flex; align-items: center; justify-content: center; gap: 7px; cursor: pointer; }
.qp-seg:last-child { border-right: 0; }
.qp-seg.is-on { background: var(--Highlight); color: var(--On-Highlight); }
.qp-segs--sm .qp-seg { height: 28px; padding: 0 12px; font-size: 13px; }

/* Checkbox */
.qp-check { display: flex; align-items: center; gap: 10px; }
.qp-check input { width: 16px; height: 16px; margin: 0; accent-color: var(--Highlight); }

/* Combo box */
.qp-combo { display: inline-flex; align-items: center; justify-content: space-between; gap: 12px; min-width: 200px; height: 34px; box-sizing: border-box; padding: 0 10px 0 12px; background: var(--Bg-Raised); border: 1px solid var(--Border-Strong); border-radius: var(--radius-6); font-family: var(--font-sans); font-size: 13.5px; color: var(--Text-Primary); }
.qp-combo.is-hover { border-color: var(--Highlight-Text); }
.qp-menu { margin-top: 4px; min-width: 200px; box-sizing: border-box; background: var(--Bg-Raised); border: 1px solid var(--Border-Strong); border-radius: var(--radius-8); padding: 4px; }
.qp-menu__item { display: flex; align-items: center; gap: 8px; height: 30px; padding: 0 10px; border-radius: var(--radius-6); }
.qp-menu__item.is-hover { background: var(--Bg-RowHover); }
.qp-menu__item.is-on { background: var(--Highlight); color: var(--On-Highlight); }

/* Table */
.qp-table { border: 1px solid var(--Border-Default); border-radius: var(--radius-8); overflow: hidden; background: var(--Bg-Panel); font-variant-numeric: tabular-nums; }
.qp-tr { display: grid; align-items: center; height: 36px; border-bottom: 1px solid var(--Border-Default); }
.qp-tr:last-child { border-bottom: 0; }
.qp-tr--head { height: 34px; background: var(--Bg-Raised); }
.qp-tr.is-hover { background: var(--Bg-RowHover); }
.qp-tr.is-selected { background: var(--Highlight); color: var(--On-Highlight); border-bottom-color: transparent; }
.qp-td { padding: 0 12px; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.qp-th { padding: 0 12px; height: 100%; display: flex; align-items: center; gap: 6px; font-weight: 700; font-size: 11.5px; letter-spacing: 0.12em; text-transform: uppercase; color: var(--Text-Secondary); }
.qp-th.is-sorted { color: var(--Text-Primary); box-shadow: inset 0 -2px 0 var(--Highlight-Text); }
.qp-th.is-sorted .qp-ico { color: var(--Highlight-Text); width: 14px; height: 14px; }
.qp-td--muted { color: var(--Text-Secondary); }
.qp-tr.is-selected .qp-td--muted { color: var(--On-Highlight); opacity: 0.85; }
.qp-name { display: flex; align-items: center; gap: 9px; font-weight: 500; font-size: 15px; }
.qp-name .qp-glyph { color: var(--Highlight-Text); }
.qp-tr.is-selected .qp-glyph { color: var(--On-Highlight); }
.qp-star { width: 14px; height: 14px; }
.qp-star--on { fill: var(--Signal); stroke: var(--Signal); }
.qp-star--off { color: var(--Text-Tertiary); }
.qp-tr.is-selected .qp-star--off { color: var(--On-Highlight); opacity: 0.8; }

/* Window, header, racing stripe */
.qp-window { background: var(--Bg-Base); border-radius: var(--radius-8); overflow: hidden; box-shadow: var(--Shadow-Window); }
.qp-titlebar { height: 32px; display: flex; align-items: center; gap: 8px; padding-left: 12px; background: var(--Bg-TitleBar); color: var(--Text-Secondary); font-size: 12px; }
.qp-titlebar__btn { width: 46px; height: 32px; display: flex; align-items: center; justify-content: center; }
.qp-monogram { width: 40px; height: 40px; flex: none; border-radius: var(--radius-8); background: var(--Highlight); box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.22), 0 0 0 1px var(--Trim-Line); display: flex; align-items: center; justify-content: center; color: var(--On-Highlight); font-weight: 800; font-size: 16px; letter-spacing: 0.02em; }
.qp-monogram--tiny { width: 16px; height: 16px; border-radius: var(--radius-4); font-size: 7.5px; box-shadow: none; background: var(--Brand-Green); color: #f2f1ec; }
.qp-header { display: flex; align-items: center; gap: 14px; }
.qp-header__text { flex: 1; min-width: 0; }
.qp-header__title { font-weight: 800; font-size: 23px; line-height: 1.1; }
.qp-header__sub { color: var(--Text-Secondary); margin-top: 2px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.qp-trim { height: 8px; background: linear-gradient(to bottom, var(--Trim-Stripe) 0 4px, var(--Highlight) 4px 8px); }
.qp-dot { position: relative; display: inline-flex; }
.qp-dot::after { content: ""; position: absolute; top: -2px; right: -3px; width: 7px; height: 7px; border-radius: 4px; background: var(--Signal); box-shadow: 0 0 0 2px var(--Bg-Raised); }
.qp-btn--primary .qp-dot::after { box-shadow: 0 0 0 2px var(--Highlight); }
.qp-count { font-weight: 700; font-size: 12px; color: var(--Text-Secondary); background: var(--Bg-Raised); border: 1px solid var(--Border-Default); border-radius: 9px; padding: 0 7px; }

/* Status bar and banners */
.qp-status { height: 38px; box-sizing: border-box; display: flex; align-items: center; gap: 10px; padding: 0 5px 0 14px; border-radius: var(--radius-8); background: var(--Bg-Raised); border: 1px solid var(--Border-Default); }
.qp-status__text { flex: 1; min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.qp-status .qp-glyph { color: var(--Highlight-Text); }
.qp-banner { display: flex; align-items: center; gap: 12px; padding: 10px 10px 10px 14px; border-radius: var(--radius-8); background: var(--Bg-Raised); border: 1px solid var(--Signal); }
.qp-banner__icon { color: var(--Signal); }
.qp-banner__text { flex: 1; min-width: 0; }
.qp-banner--danger { border-color: var(--Danger); }
.qp-banner--danger .qp-banner__icon { color: var(--Danger); }

/* Message badge */
.qp-badge { width: 36px; height: 36px; flex: none; box-sizing: border-box; border-radius: 18px; border: 2px solid currentColor; background: var(--Bg-Raised); display: flex; align-items: center; justify-content: center; font-weight: 800; font-size: 16px; }
.qp-badge--info { color: var(--Info); }
.qp-badge--warning { color: var(--Signal); }
.qp-badge--error { color: var(--Danger); }
.qp-badge--question { color: var(--Highlight-Text); }

/* Tracked-folder chip */
.qp-chip { height: 30px; box-sizing: border-box; padding: 0 12px 0 10px; border-radius: var(--radius-15); border: 1px solid var(--Border-Strong); background: var(--Bg-Raised); color: var(--Text-Primary); font-family: var(--font-sans); font-weight: 500; font-size: 13.5px; display: inline-flex; align-items: center; gap: 7px; cursor: pointer; }
.qp-chip .qp-ico { color: var(--Text-Secondary); }
.qp-chip.is-on { border-color: var(--Highlight-Text); background: var(--Highlight-Soft); }
.qp-chip.is-on .qp-ico { color: var(--Highlight-Text); }
.qp-chip__paused { font-size: 12px; font-weight: 600; color: var(--Text-Tertiary); }

/* Recents calendar */
.qp-panel { border: 1px solid var(--Border-Default); border-radius: var(--radius-10); background: var(--Bg-Panel); padding: 12px 16px 14px; }
.qp-cal { display: flex; gap: 8px; }
.qp-cal__days { display: flex; flex-direction: column; gap: 2px; padding-top: 18px; width: 26px; font-size: 10.5px; color: var(--Text-Tertiary); }
.qp-cal__days span { height: 11px; line-height: 11px; }
.qp-cal__months { position: relative; height: 18px; font-size: 10.5px; color: var(--Text-Tertiary); }
.qp-cal__months span { position: absolute; top: 0; }
.qp-cal__weeks { display: flex; gap: 2px; }
.qp-wk { display: flex; flex-direction: column; gap: 2px; border-radius: 3px; }
.qp-wk.is-selected { outline: 1.5px solid var(--Highlight-Text); outline-offset: 1px; }
.qp-c { width: 11px; height: 11px; box-sizing: border-box; border-radius: var(--radius-2); }
.qp-c.h0 { background: var(--Heat-0); border: 1px solid var(--Border-Default); }
.qp-c.h1 { background: var(--Heat-1); }
.qp-c.h2 { background: var(--Heat-2); }
.qp-c.h3 { background: var(--Heat-3); }
.qp-c.h4 { background: var(--Heat-4); }
.qp-c.hx { background: var(--Heat-Untracked); }
.qp-c.ho { background: transparent; border: 1px dashed var(--Border-Default); }
.qp-c.is-today { box-shadow: 0 0 0 1.5px var(--Signal); }
.qp-bar { display: inline-block; width: 70px; height: 8px; border-radius: 2px; background: var(--Border-Default); overflow: hidden; vertical-align: middle; }
.qp-bar span { display: block; height: 100%; background: var(--Leather); }

/* Highlight picker */
.qp-swatches { display: flex; gap: 18px; }
.qp-swatch { display: flex; flex-direction: column; align-items: center; gap: 6px; width: 64px; font-size: 12px; color: var(--Text-Secondary); text-align: center; line-height: 1.2; }
.qp-swatch button { width: 40px; height: 40px; border-radius: var(--radius-20); border: 0; padding: 0; display: flex; align-items: center; justify-content: center; color: #ffffff; box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.18); cursor: pointer; }
.qp-swatch button.is-on { box-shadow: 0 0 0 2px var(--Bg-Base), 0 0 0 4px var(--Text-Primary); }
.qp-swatch .p-green { background: var(--Preset-Green); }
.qp-swatch .p-blue { background: var(--Preset-Blue); }
.qp-swatch .p-red { background: var(--Preset-Red); }
.qp-swatch .p-cognac { background: var(--Preset-Cognac); }
.qp-swatch .p-windows { background: var(--Bg-Raised); color: var(--Text-Primary); box-shadow: inset 0 0 0 1px var(--Border-Strong); }
```

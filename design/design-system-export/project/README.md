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

---
status: current — built and tested; the section 8 walk on Windows is still to do
branch: claude/ui-refresh
date: 2026-09-28
---

# UI refresh hand-off — the Windows walk

Read [the UI refresh plan](260927_UI%20Refresh%20Detailed%20Plan.md) for the decisions (U1–U13) and the checklist. The look it builds is in the [QuickerPlaces Design System](https://claude.ai/artifact/MCDp4Ax8HWUgVUw3S39N2W), version 2, and on the [design canvas](https://claude.ai/artifact/46meR5TGTPBdtUDscAFGuS). `BUILD_SUMMARY.md` has what was built, every departure from the plan, and the known gaps. Nothing has been pushed; merging is the user's call.

## What's done

Tasks 1–19 of the plan are committed on `claude/ui-refresh`, one commit per task, with the review fixes beside them. Task 18 (the full test and build pass) has no commit of its own. Task 21, the Design System update, records the WPF differences and colour changes in that artifact.

| Task | Commits |
|---|---|
| 1 Branch and baseline | `d1f81a7` |
| 2 Theme and highlight settings (schema 5) | `c819288` |
| 3 Colour maths and highlight presets | `34c7dce`, review `44781c3` |
| 4 Palette files and their test | `24e7889`, review `38dbb4c` |
| 5 Places grid sort without the Type column | `1f57992` |
| 6 View models lose glyph strings | `cf9f7b3` |
| 7 Fonts | `eae6005` |
| 8 Icons | `e7f0b90` |
| 9 Styles.xaml replaces Theme.xaml | `35d0bd7` |
| 10 ThemeManager and startup | `b8a2491` |
| 11 Rename resource keys across the views | `703d24f` |
| 12 Main window | `83b77db`, `ceeb4b9`, review of 9–12 `8c82aef` |
| 13 Settings with Appearance | `9e8771e`, review `3e7b64d` |
| 14 Recents | `45cf773`, review `8c9cb4a` |
| 15 Dialogs | `5c0eae2`, `122dede` |
| 16 Name wording in messages | `f238b30`, `f05c4a6` |
| 17 App icon | `3aaa784` |
| 19 User guide | `c54108c`, `2349dc5` |
| 20 Build summary, README and this hand-off | this commit |

Departures from the plan found in review, in short (details in `BUILD_SUMMARY.md`):

- Hull green dark hover is `#32846E`, not `#33866F` (4.39:1 with white text). Dark `Text.Tertiary` is `#848B88`, not `#7F8683` (4.23:1 on `Bg.Raised`, 4.43:1 on `Bg.RowHover`). Both failed 4.5:1.
- Windows accent text is white whenever white reaches 4.5:1, so Windows blue keeps white text.
- The focus ring had never shown; `FocusVisualStyle` is now set on every focusable app style.
- Settings scrolls within the work area, and Cancel no longer re-applies the theme while the app shuts down (it crashed at exit).
- Recents: untracked days keep an outline (they were invisible in Dark), day cells have a compact focus ring, and the weekday labels line up with the rows again (a bug also on `main`).
- `Loaded="Window_Loaded"` is back on `MainWindow`. `48c88db`, on `main`, had dropped it, which disabled the sort arrow, clearing a sort with no column, and the hotkey-failure notice.
- Export and Import's NAME column sorts again (`SortMemberPath`).

## What's verified

On 2026-09-28: the solution builds with 0 warnings and 0 errors, and all 643 tests pass (547 before the refresh). The app launches to the main window with a clean log. Each window and dialog was also loaded in a WPF probe in Dark and in Light, with no binding or resource warnings. None of this is a look at the running app by a person.

## What's not: the section 8 walk

Walk each item in Dark, then Light, then Match Windows (switch the Windows app theme while the app is open). What each must show is in the plan's section 8. Record each as passed, failed or not done in `BUILD_SUMMARY.md`.

- [ ] 1. Launch: title bar, the five TASA Orbiter weights, paths in IBM Plex Mono.
- [ ] 2. Main window header, trim band, leather favourite cards with numbers, Ctrl+1, drag, right-click menu.
- [ ] 3. Places list: no Type column, icons, hover, selected row, star states, sort arrow and underline.
- [ ] 4. Search box, Ctrl+F keycap, clear, Esc.
- [ ] 5. Status bar after Remove, with Undo.
- [ ] 6. Unsaved-changes banner (make `places.json` read-only first).
- [ ] 7. Settings: live preview in every open window, Cancel and title-bar close restore, Save persists, Windows accent follows Windows, a very light accent stays readable.
- [ ] 8. Recents: chips, Week/Month/Day, leather heat, today outline, selected day, year picker, table.
- [ ] 9. Every dialog, including message boxes, recovery prompt, Export/Import, Recently Deleted, Track a folder and Folder tracking settings.
- [ ] 10. Keyboard only through the main window and Settings: the orange ring on every focused control.
- [ ] 11. App icon in Explorer, taskbar, Alt+Tab and title bars.

Also look at these on the walk; review raised them but no one has seen them on screen:

- The places grid shows no focus when nothing is selected (the cell focus style is null).
- A Recents chip shows focus only by the ring.
- Untracked days in Dark, and the 16px focus ring on day cells.
- The implicit Window style reaches the `MainWindow` subclasses; the focus ring shows under Fluent `ThemeMode`; Kbd and Badge show their text.
- The Light "!" badge on warning and recovery messages (4.29:1, treated as a graphic).
- The app icon at 16px.

If the walk finds a fault, fix it on this branch before merging.

## Known gaps

- Plan section 12: the default stays dark with Hull green (to default to Match Windows, change the two defaults in `AppSettings` and the Task 2 test); no visits bars in the Recents table; square table corners; no letter-spacing on capitals; no free colour picker.
- The type text ("Folder"/"URL") is shown nowhere now, so screen readers get no folder/link cue in the grid, Export, Import or Recently Deleted. Idea: say "Link" and use it as the type icon's `AutomationProperties.Name`.
- The app icon is soft at 16px; TASA Orbiter's Q has a detached tail bar.

## Constraints carried forward

- Refer to every colour with `DynamicResource`. A new colour goes in both palette files with the same key; `PaletteFileTests` fails otherwise.
- Keep `ThemeColor`, `HighlightPalette` and `ThemePreference` free of `System.Windows`, so the tests can link them.
- Theme and highlight stay tolerant strings in settings.json; a bad value must not reset other settings.
- Change the Design System first, then the palettes.

## Next

After the walk and the merge: Phase 4, general file support (roadmap §1.1). Write its detailed plan against `main` as it is then.

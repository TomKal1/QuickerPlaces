---
title: QuickerPlaces — configurable canvas M0: baseline and parity checklist
status: current; the migration checklist for M3–M7 of the configurable canvas plan
created: 2026-09-29
branch: ccr-6156d37a-mo223d
baseline: 5443e9f (main at 6ba577d plus the canvas planning documents)
---

# Configurable canvas M0: baseline and parity checklist

Milestone M0 of [the configurable canvas plan](260929_Configurable%20Canvas%20Implementation%20Plan.md) §5: a reproducible baseline and a concrete list of everything the workspace must keep before it replaces the current main window (M7). No functional change.

## 1. Baseline

| Check | Result |
|---|---|
| `dotnet test src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj` (Linux cloud session, .NET SDK 10.0.112) | 877 passed, 0 failed, 0 skipped |
| `dotnet build src/QuickerPlaces/QuickerPlaces.csproj -c Release` | **Not run at M0.** Since M2 it runs on Linux with `-p:EnableWindowsTargeting=true` (XAML included): 0 warnings, 0 errors. Running the app still needs Windows |
| Main compared with the plan's §2 inventory | No drift: every file the plan names exists with the responsibility it describes |

Development checks of the workspace UI must use isolated stores (plan M0). Every store already takes its storage or path through a constructor (`PlacesService`, `SessionStore`, `RecentFilesStore`, `SettingsService`, `ActivityTrackingHost.CreateStore`), so M1's `WorkspaceStore` follows the same pattern. M3 added the app-wide switch for manual runs: `--data-root <folder>` keeps every store, settings.json and the log under that folder, with its own single-instance gate and without touching the Windows startup entry. `--workspace` shows the workspace.

## 2. Parity checklist

Everything below is reachable today. Tick each item when the workspace offers it (or deliberately keeps its existing dialog) and it has been tried on Windows. M7 does not remove a window wrapper while an item it provides is unticked.

### Main window (`Views/MainWindow.xaml`, `MainViewModel`)

- [ ] Search box: filter places by alias/path; Esc clears the search (or focuses the list when empty); Enter opens the top result; Down moves into the list. Ctrl+F (`ApplicationCommands.Find`) focuses it.
- [ ] Favourites bar: open by click; Ctrl+1…Ctrl+9 open the Nth favourite; drag to reorder favourites; context menu Open / Copy / Unfavourite.
- [ ] Places list: sort by column (a remembered Favourite sort falls back to stored order); star beside the alias toggles favourite; double-click opens.
- [ ] Row shortcuts: Enter open, F2 rename alias, Ctrl+E edit path/URL, Ctrl+D favourite, Ctrl+C copy path/URL, Delete remove.
- [ ] Row context menu: Open, Copy, Rename, Edit, Favourite, Remove.
- [ ] Add folder (Ctrl+N) and Add link (Ctrl+U) through `PlaceFormDialog`.
- [ ] Remove with Undo (Ctrl+Z, and the status bar's Undo button); status dismiss.
- [ ] Hide/show the list (Ctrl+H) — compact launcher mode. Decided in M3: it stays a window mode; in the workspace it hides the panels and keeps the header, favourites and toolbar.
- [ ] Options menu: Recently Deleted…, Show data folder, Import…, Export… (Export disabled with no places).
- [ ] Unsaved-changes banner: Retry, Show data folder, Show log.
- [ ] Settings dialog: theme, highlight, global hotkey (reset/turn off), keep running in tray, start with Windows.
- [ ] Recents button with the tracking indicator (count of tracked folders); opens `ActivityWindow`.
- [ ] Library button (`LibraryWindow`) and Sessions button (`SessionsWindow`). Since M2 both windows host the workspace panels (`YearActivityPanel`, `FileShelfPanel`, `SessionsPanel`).

### Recents (`Views/ActivityWindow.xaml`)

- [ ] Tracked folders: add, edit tracking settings, about folder, stop/start tracking, delete; save failure with Retry save; status Dismiss.
- [ ] Period: Previous/Next, Day/Week/Month, year picker (Enter picks, Esc closes), calendar day click, arrow-key calendar navigation.
- [ ] Folder activity rows with "Add as place" (hands off to `MainViewModel.AddFolderFromActivity`).

### Library (`Views/LibraryWindow.xaml`, `LibraryViewModel`)

- [ ] Year activity: day click selects, Clear day, previous/next year.
- [ ] Kind chips; Show All/Saved/Recent; Group by Type/Tag; search by name, path, tag or session (Esc clears, Down enters list).
- [ ] Rows: double-click/Enter opens (places report usage through `NotePlaceOpened`); context menu Open, Copy path or link, Remove from Recent Files.
- [ ] Recent Files opt-in: enable, kinds (PDF/Word/Excel), scope, Clear Recent Files.

### Sessions (`Views/SessionsWindow.xaml`, `SessionsViewModel`, `SessionEditorDialog`)

- [ ] Save what's open (capture with detected-open files checked, recent candidates unchecked; held-files clue).
- [ ] Tag chips; search (Esc clears, Down enters list).
- [ ] Session list: double-click/Enter reopens, Delete deletes; files grid: double-click/Enter opens one file.
- [ ] Open all (with missing/partial-launch report), Open file, Edit (rename/retag/add/remove files), Delete (files untouched).

### Application lifecycle (`App.xaml.cs`, `TrayIcon`, `SingleInstance`, `GlobalHotkey`)

- [ ] Single-instance gate before any store is constructed; second launch activates the running window.
- [ ] Global hotkey restores, activates and focuses search (`BringToFront`).
- [ ] Tray: Open QuickerPlaces, Pause/Resume tracking, Exit; `--tray` start hidden when start-with-Windows is on; close-to-tray only when enabled.
- [ ] Startup recovery prompt for places.json (Damaged / Unreadable / newer version) before the main window shows.
- [ ] Close flow: places unsaved-changes prompt, sessions retry, Recent Files and activity host disposal, theme disposal, window bounds saved, clean-exit log. The workspace's pending layout write joins this flow (plan §4).
- [ ] One `ActivityTrackingHost` and one `RecentFilesHost` for the life of the app, whatever panels are shown.
- [ ] Window bounds restored across monitors; minimise/restore state.

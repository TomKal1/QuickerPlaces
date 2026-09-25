# QuickerPlaces — Build Summary

How this app came together: the starting point, the decisions made along the way, the bugs that turned up after the first hand-off, and where things stand now. Written for whoever (human or AI) next opens this repo and wants the full story without re-reading the whole chat history.

## Starting point

Two inputs kicked off the build:

- **A System Instructions (SI) document** (`ai/QuickerPlaces-SI.md`) — a requirements/spec handoff written in an earlier planning conversation, describing QuickerPlaces as a lightweight Windows desktop utility for storing and quickly opening remembered "places" (folder paths and URLs), part of the broader **QuickerLinks** project ("a better path launcher than Quick Links").
- **A WPF project template** (`SystemApp`) — a pre-built starter with a hand-rolled MVVM setup (`ObservableObject`, `RelayCommand`), a dark violet theme (`Theme.xaml`), a themed message-dialog replacement for `MessageBox` (`MessageForm`), a JSON `SettingsService`, and — notably — a system-tray icon with three run modes (Silent / WindowedExitOnClose / WindowedTrayOnClose), none of which QuickerPlaces needed.

The SI's own instructions were explicit: inspect the template first, follow its conventions where they don't conflict with a hard requirement, and resolve every open decision against what the template actually contains rather than guessing blind.

## Decisions made resolving the SI against the template

| Question | Resolution |
|---|---|
| Combined dialog or two-step wizard for adding a place? | Single combined dialog (Alias + Resource together) — the template had no existing data-entry-dialog precedent, so this took the SI's own stated default. |
| Framework / target | WPF, net10.0-windows, hand-rolled MVVM, `Nullable` enabled — all confirmed from the template as-is. |
| Reuse the template's settings pattern? | Partially. Window chrome (bounds, grid-expanded state) stayed in the template's `AppSettings`/`SettingsService` JSON pattern, saved once on clean exit. The actual Place list got its own file and service (`PlacesStore`/`PlacesService`) at `%AppData%\QuickerPlaces\QuickerPlaces\places.json`, written through **atomically on every single change** (temp file + `File.Move` replace) — a harder requirement than the template's "save on exit" default, so it got its own path instead of overloading the existing one. |
| Favourite "bubble" visual treatment | No existing Chip/Tag control in the template, so a new `Button.Bubble` style was added to `Theme.xaml`: a pill-shaped variant of the existing `Button.Primary` pattern, same Accent palette. |
| System tray / background running | Removed entirely. The SI is explicit that QuickerPlaces has no tray icon and always exits on close, so `TrayIconService`, the `RunMode` enum, and the WinForms dependency that came with the tray icon were deleted rather than left dormant. Folder browsing uses the native `Microsoft.Win32.OpenFolderDialog` (WPF, .NET 8+) instead. |
| Project naming | Went beyond the template's "just edit AppInfo's 3 constants" instruction and did a full rename — namespace, assembly name, `.sln`/`.csproj` filenames, folder — from `SystemApp` to `QuickerPlaces`, since this is a real named product rather than a demo fork. |

## What got built

- **Data model** — `Place` (Alias, Type, Resource, IsFavourite, FavouriteOrder, DateAdded), a `PlaceType` enum (Folder/Url), and `PlacesStore` (schema version + places array, matching the SI's suggested JSON shape).
- **`PlacesService`** — the single source of truth for validation and persistence: case-insensitive alias uniqueness, exact-match (not normalized) resource duplicate checks, folder/URL format validation, add/rename/edit/toggle-favourite/reorder/remove, all write-through to disk immediately. Export and import (with upfront collision filtering, so a colliding item is simply never offered as an import candidate rather than shown and rejected). Loads gracefully from a missing or corrupt file rather than crashing, surfacing a one-time notice instead.
- **Main window** — header with Add Folder / Add URL / Export / Import; a favourite-bubble row above the grid with drag-to-reorder; a collapsible DataGrid with the spec's exact context-menu order (Open, Rename Alias, Edit Path/URL, Toggle Favourite, Remove) and double-click-to-open.
- **Dialogs** — one combined `PlaceFormDialog` handles Add (Folder/URL) and the two single-field edits (Rename Alias, Edit Path/URL) via a mode flag; `ExportDialog`/`ImportDialog` are near-identical checkbox-grid dialogs.
- **App icon** — a generated multi-resolution "QP" monogram icon on the same accent gradient as the in-app header badge, wired as both the compiled exe's icon and every window's title-bar/taskbar icon.

## Bugs found after first hand-off, and fixes

The build environment for this project has no Windows/.NET SDK available, so nothing here was compiler-verified before delivery — only manually reviewed (XML well-formedness, brace balance, cross-referencing every XAML event handler and `x:Name` against its code-behind). That review caught a WPF-specific `RowDefinition.Height` binding gotcha before first delivery (fixed by binding via `ElementName` instead of relying on `RowDefinition`'s non-existent `DataContext` inheritance), but two more issues only surfaced once the user actually ran the app in Visual Studio:

1. **Startup crash, every single launch.** `MainViewModel`'s constructor called `RebuildFavourites()` — which calls `ExportCommand.RaiseCanExecuteChanged()` — *before* `ExportCommand` and the other `RelayCommand` properties were assigned further down that same constructor. Result: `NullReferenceException` on `ExportCommand.get` returning null, unconditionally, not just on a fresh install as first suspected. **Fix:** moved all command construction above the `RebuildFavourites()` call, and audited every other constructor in the project for the same "field used before assignment" ordering mistake (none found).

2. **`InvalidOperationException` the moment a Place was added:** *"A TwoWay or OneWayToSource binding cannot work on the read-only property 'IsFavourite'."* The DataGrid's Favourite column (`DataGridCheckBoxColumn`) bound `IsFavourite` — a get-only property — with no explicit binding mode. Text columns don't hit this, because their read-only display element is a plain `TextBlock` (`OneWay` by default) — the `TwoWay`-by-default `TextBox` editor is never instantiated while the grid is read-only. But `DataGridCheckBoxColumn` uses the *same* live `CheckBox` for both display and edit, and `CheckBox.IsChecked` defaults to `TwoWay` in its own metadata, so the column's `IsReadOnly="True"` (which only blocks entering edit mode) didn't stop WPF from building — and immediately validating — a two-way binding against a property with no setter. It only threw once a real row existed to bind against, which is why the window loaded fine until the first place was added. **Fix:** explicit `Mode=OneWay` on that one binding. Every other `IsChecked` binding in the project (the Export/Import selection checkboxes) binds to a real read/write property, so those were left as intentional two-way bindings.

## First compiler-verified build, tests, and review fixes

A later session installed the .NET 10 SDK on Linux and built the solution with `-p:EnableWindowsTargeting=true`. This was the first real compile, and it came back with **0 warnings, 0 errors**. WPF can't *run* on Linux, so the UI still hasn't been exercised outside Visual Studio. To cover the logic side, that session added a test project and reviewed the code by hand. The review found four bugs, and each now has a regression test that fails with its fix reverted:

1. **Settings silently lost when closing maximized or minimized.** `AppSettings` uses `double.NaN` for "no saved window position yet", and `System.Text.Json` refuses to serialize NaN by default. If the window had never been closed in its normal state, closing it maximized or minimized made `SettingsService.Save` throw. The empty `catch` swallowed the error, so *every* setting was dropped, the grid-collapsed state included. **Fix:** `JsonNumberHandling.AllowNamedFloatingPointLiterals` on the settings serializer.
2. **Relative folder paths were accepted.** `Path.GetFullPath` happily resolves `Projects` or `..\Docs` against the working directory, so these passed validation, and Open then went to a different folder depending on how the app was launched. **Fix:** folder paths must also pass `Path.IsPathFullyQualified` (a drive-rooted or UNC path).
3. **A short bubble drag that ended on the same bubble moved it to the end of the row.** The drop handler treated "dropped on itself" the same as "dropped on empty space". **Fix:** dropping a bubble on itself is now a no-op.
4. **An import file that collided with itself** (the same alias or resource twice) offered both items in the checklist, and `CommitImport` then silently skipped the second one. **Fix:** `GetImportCandidates` now keeps only the first of any repeated alias/resource within the file. Also, a bare `null` entry in `places.json` is now dropped on load instead of waiting to NRE the grid.

`PlacesService` and `SettingsService` each gained a constructor that takes an explicit file path, so the tests run against temp files and never touch the real `%AppData%` data.

### Tests

`src/QuickerPlaces.Tests` is an xUnit project covering validation, write-through persistence, favourite ordering, export/import collision filtering, and corrupt/missing file recovery. It targets plain `net10.0` and *links* the UI-free source files in, instead of referencing the WPF project. That keeps `dotnet test` working on any OS, but it means those linked files must stay free of `System.Windows.*`.

## Current status

The solution builds cleanly with the compiler, and the service layer is covered by 36 passing unit tests. Everything the UI does on top of that still needs a hands-on pass in Visual Studio on Windows, where it has only been partly exercised so far: dialogs, context menus, double-click, bubble drag-reorder, the grid collapse, window-state restore, and the corrupt-file notice.

# QuickerPlaces — Build Summary

How this app came together: the starting point, the decisions made along the way, the bugs that turned up after the first hand-off, and where things stand now. Written for whoever (human or AI) next opens this repo and wants the full story without re-reading the whole chat history.

## Starting point

Two inputs kicked off the build:

- **A System Instructions (SI) document** (`ai/260831_Initial SI brief.md`) — a requirements/spec handoff written in an earlier planning conversation, describing QuickerPlaces as a lightweight Windows desktop utility for storing and quickly opening remembered "places" (folder paths and URLs), part of the broader **QuickerLinks** project ("a better path launcher than Quick Links").
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

## Search, icons, and keyboard shortcuts

These are the SI §3 "future enhancements", added after the first review pass:

- **Search/filter box.** `MainViewModel.PlacesView` is the default `ICollectionView` over `Places`, with a filter. The grid binds to it, so header-click sorting still works on top of the filter. The matching rule lives in UI-free `Services/PlaceSearch.cs`, where it's unit-tested: split the query on whitespace, and every term must appear case-insensitively in the alias or the resource. The filtered view is refreshed after a rename or path edit, because those can change whether a row matches. If an add or import would be hidden by the current search, the search is cleared. Typing a search while the list is collapsed re-expands it. The header shows "All Places (n of m)" while filtering, and a message over the grid explains why it's empty: nothing saved yet, or nothing matches.
- **Icons.** Rows and bubbles show a fixed type glyph (folder / globe) from the Windows icon font, `Font.Icons` in `Theme.xaml`, which is Segoe Fluent Icons with Segoe MDL2 Assets as the fallback. Real favicons were deliberately skipped, because fetching them would mean contacting every saved URL, which the SI rules out. The Alias column became a `DataGridTemplateColumn` with `SortMemberPath="Alias"`, so sorting is unchanged. Bubbles also got a tooltip showing their destination.
- **Keyboard shortcuts.** Window-wide shortcuts are `KeyBinding`s: Ctrl+N / Ctrl+U / Ctrl+H, and Ctrl+1–9 via `OpenFavouriteAtCommand`. Ctrl+F uses `ApplicationCommands.Find`. Row shortcuts (Enter / F2 / Ctrl+E / Ctrl+D / Delete) are handled in `PlacesGrid_PreviewKeyDown` rather than as window bindings, so Delete or F2 typed in the search box can never act on a row. They're handled in Preview so the grid's own Enter-moves-down behaviour doesn't also fire. The search box handles Enter (open top result), Down (into the grid), and Esc (clear, then focus the grid).

## Data-safety fixes

> *Superseded by Phase 1 (below), which replaced both fixes when the branches were merged: the backup copy became Phase 1's explicit quarantine, and the one-time warning became the unsaved-changes banner. The close prompt and atomic export survived. Kept for the history.*

Two places where the app could lose the user's data without saying so:

1. **A corrupt `places.json` was destroyed by the first change.** On a load failure the app starts empty and leaves the file alone, but the next add or edit wrote the empty-plus-one list over it, so the user's original data was gone. The startup notice even said the file was "left untouched". **Fix:** `PlacesService` now copies an unreadable file to `places.corrupt-yyyyMMdd-HHmmss.json` (with a `-2`, `-3`, ... suffix if that name is taken) as soon as the load fails, and exposes the copy as `CorruptFileBackupPath`. The startup notice points the user at the copy. If even the copy fails (for example, access denied), the notice says so and tells them to copy the file themselves before changing anything.
2. **Save failures were swallowed.** `SaveToDisk` caught every exception and said nothing, so a full disk or a file locked by a sync tool made changes look saved when they only existed in memory. **Fix:** a failed save sets `HasUnsavedChanges` and raises `SaveFailed` with a readable message. The event fires only on the first failure in a run, so a disk that stays full doesn't warn on every edit. `MainViewModel` shows the warning via `Dispatcher.InvokeAsync`, so a save that fails inside a dialog's own Save click shows after that dialog closes. Every later change retries the save, and on window close `App` calls `TrySave()` once more. If that still fails, the user is asked before the window closes.

Both have regression tests that fail with the fix reverted. The save-failure test makes the write fail on any OS by creating a directory where `places.json` should be.

Two follow-ups in the same area:

- **Export is now atomic too.** It used to call `File.WriteAllText` straight onto the chosen file, so an interrupted export over an earlier backup could leave a truncated file. `Export` and `SaveToDisk` now share `WriteAtomically` (temp file + `File.Move` replace), which also deletes its temp file when the write fails instead of leaving `*.tmp` behind.
- **Open data folder button.** An icon-only folder button at the left of the header's action row (`OpenDataFolderCommand`) opens the folder holding `places.json` in Explorer, with the file selected when it exists. It's there mainly so the `places.corrupt-*.json` backups are easy to find. It's icon-only because the header already runs out of room near the window's 700px `MinWidth`.

## Global hotkey and single instance

The first feature beyond the spec, chosen to lean into the "better path launcher" goal.

- **Global hotkey.** `Services/GlobalHotkey.cs` wraps Win32 `RegisterHotKey` against the main window's HWND and listens for `WM_HOTKEY` through an `HwndSource` hook. Pressing it calls `MainWindow.BringToFront`, which restores a minimized window (back to maximized if it was maximized), activates it, and focuses and selects the search box. If a modal dialog is open, it brings the dialog forward instead, since the main window can't take input then. The window that receives a hotkey is allowed to take the foreground, so no focus-stealing workarounds are needed. Registration is `MOD_NOREPEAT`, so holding the keys doesn't retrigger it.
- **Configurable.** The hotkey is `AppSettings.GlobalHotkey` (default `"Ctrl+Alt+Space"`, `"None"` or empty turns it off), so `AppSettings.SchemaVersion` went to 2. A version-1 file just lacks the field and gets the default. Parsing is in UI-free `Models/HotkeyGesture.cs` and unit-tested. It requires Ctrl, Alt or Win, because a bare key or Shift+key would be swallowed system-wide. Turning the key name into a virtual-key code (WPF `KeyConverter`, with a bare digit mapped to `D0`–`D9`) happens in `GlobalHotkey`. A bad setting or a combination another app already owns shows a notice on startup pointing at Settings (added in the next round), and the app otherwise runs normally. The subtitle shows the active hotkey.
- **Single instance.** `Services/SingleInstance.cs` uses one named auto-reset event (`Local\...`, per logon session) as both the lock and the "show yourself" doorbell. The first launch creates it and waits on it through `ThreadPool.RegisterWaitForSingleObject`. A later launch finds it already exists, calls `AllowSetForegroundWindow(ASFW_ANY)` so the running copy may take the foreground, signals it, and exits before loading anything. Besides fitting a launcher, this closes a data-loss hole: two copies would each overwrite the other's `places.json`.

`GlobalHotkey` and `SingleInstance` are Win32/WPF code, so, like the rest of the UI, they're compile-checked here but not yet run.

## Undo remove, Copy Path/URL, and a Settings dialog

> *Partly superseded by Phase 2 (below): `RemovedPlace` and the Remove confirmation are gone, Undo un-deletes the same record, and the status bar stays 10 seconds when it offers Undo. Copy Path/URL and Settings are unchanged. Kept for the history.*

- **Undo remove.** `PlacesService.Remove` now returns a `RemovedPlace` (the same `Place` object, its list index, and its `FavouriteOrder` if it was a favourite), and `TryRestore` reinserts it there. For a favourite it shifts later bubbles right, then renumbers to close any gap left by favourites removed in the meantime. It refuses, changing nothing, if the alias or path/URL has since been reused, or if the place is already back. `MainViewModel` keeps a session-only stack, so Ctrl+Z (window `KeyBinding`) or the status bar's Undo button restores removals most-recent-first. A refused restore is dropped from the stack with a message that says so. Keeping it would jam Ctrl+Z on that entry and make every older removal unreachable. The Remove confirmation now mentions Ctrl+Z instead of "can't be undone". One test (`Restore_uses_bubble_order_not_list_order_after_a_drag_reorder`) exists because a mutation check showed the favourite shift was untested: with bubbles in list order, a tie in `FavouriteOrder` was broken by list order and hid the missing shift.
- **Status bar.** A new bottom row in `MainWindow` shows short confirmations ("Removed "Docs".", "Copied the path of "Docs"."), with Undo when it applies and a dismiss button. It hides itself after 8 seconds (`DispatcherTimer`). Undo keeps working through Ctrl+Z after the bar is gone.
- **Copy Path/URL.** Second item in both context menus, right after Open (the spec's five items keep their relative order), and Ctrl+C on a grid row. That replaces `DataGrid`'s built-in Ctrl+C, which copied every cell. A clipboard held open by another app shows a message instead of throwing.
- **Settings dialog.** A gear button in the header opens `SettingsDialog`. Clicking the shortcut box and pressing a combination records it: Alt combos arrive as `Key.System`, digits become "1" rather than "D1", and plain Tab, Enter and Esc still work as keyboard navigation. MainWindow's `ApplyGlobalHotkey` is the one routine for registering the hotkey, used at startup and handed to the dialog as a callback. Save only closes the dialog if registration works, so a combination another app owns is reported inline. The live hotkey is paused while the dialog is open, so pressing it in the box records it instead of firing it, and put back on Cancel. A saved change writes `settings.json` immediately instead of waiting for exit.

## Phase 1 — Persistence reliability and recovery

Phase 1 is documented in detail in [`ai/260901_Phase 1 Detailed Plan.md`](260901_Phase%201%20Detailed%20Plan.md); this section records what actually landed and why, for anyone who doesn't want to read the whole plan first.

### Why this was the first phase

`PlacesService.SaveToDisk()` ended in a bare `catch { }`. Every mutating method — `TryAdd`, `TryRenameAlias`, `TryEditResource`, `ToggleFavourite`, `SetFavouriteOrder`, `Remove`, `CommitImport` — called it and then reported success unconditionally, regardless of what actually happened on disk. A permissions error, a full disk, another process holding the file — none of it reached the user; QuickerPlaces just said "saved" and moved on. Everything else in Phase 1 exists to close that one gap and the three related ones sitting next to it: a corrupt file being silently overwritten by the next save, damage and unavailability being reported identically, and `schemaVersion` being written but never read back. Nothing about this phase is new *behaviour* — the plan was explicit that Phase 1 changes how data is written, not what the app does — it's entirely about making failure visible and safe instead of invisible and dangerous.

### The shape of the fix

`IPlacesStorage` is the new seam between `PlacesService` and the filesystem — read, write, quarantine, and the store's path — with `FilePlacesStorage` as the production implementation and `FakePlacesStorage` as an in-memory test double that can be told to fail on command. That seam was deliberately built and landed first, with the old bare `catch {}` still in place at the end of that commit: the point was to prove the refactor was behaviour-inert before any of the actual failure handling went in on top of it.

From there:

- **`Persist()`** replaces `SaveToDisk()` and returns a `PersistenceResult` (`Saved` + an optional `UserMessage`) instead of swallowing the outcome. Every `Try*` method keeps its existing `ValidationResult` return value and gains an `out PersistenceResult persistence` parameter, so validation ("was the input acceptable") and persistence ("did the accepted change reach disk") stay two separate answers rather than being folded into one value that forces every call site to re-interpret it.
- **`PlacesService.HasUnsavedChanges`** is true from the moment a `Persist()` call fails until the next one succeeds. It is the *only* thing the unsaved-changes banner in `MainWindow` reads, and `RefreshPersistenceState` in `MainViewModel` is the *only* place that ever assigns the view-model's mirror of it. That indirection matters: a mutation rejected by validation (a duplicate alias, say) also returns a "successful" `PersistenceResult` — because nothing was attempted, nothing failed to reach disk — so if the banner state were set from a returned result directly, typing an invalid alias into an unrelated dialog would silently clear an existing failure banner. It doesn't, and there's a test for it.
- **`StoreLoadOutcome`** (`Ok` / `NotPresent` / `Damaged` / `Unreadable` / `WrittenByNewerVersion`) replaces the old `LoadFailed` boolean.
- **`RecoveryDialog`** is a new dialog (not `MessageForm` — its `MessageFormButtons` only offer fixed OK/Cancel/Yes/No sets, and "Start with an empty list" must never occupy a slot a user could hit by reflex). It takes an ordered list of labelled options built per outcome.
- **`DiagnosticLog`** is a static, lock-guarded, size-capped plain-text logger, so the save and recovery paths have somewhere to report to. It was written and wired to startup/exit *before* the save rewrite, deliberately, so the rewrite had a place to log to from the start rather than bolting logging on afterward.
- **`SingleInstance`** makes sure a second launch never constructs a `PlacesService` at all. (Phase 1 used a named mutex plus an activation event; the merge kept the feature branch's single named event instead, see "Merging Phase 1 with the feature work" below.)

### Design decisions (D1–D6), as settled outcomes

**D1 — a failed save does not roll back the in-memory change.** The alternative was reverting to the last-persisted state on a failed write. That was rejected: rolling back throws away whatever the user just typed, and it leaves **Retry** with nothing to retry. Instead, the proposed state stays in memory, `HasUnsavedChanges` goes true, and the application simply stops claiming the change is stored. Only a real successful write clears the flag. This is also what makes Retry trivial to implement — see D2.

**D2 — every save is a whole-store write.** No append log, no per-record write, no diff. At the data scale here (hundreds of records, a file measured in kilobytes) that complexity buys nothing, and it would complicate both the version gate and the Recently Deleted purge planned for Phase 2. The practical payoff of D2 is that **Retry needs no queue of pending operations** — it just re-serializes and rewrites whatever is currently in memory, which by D1 already includes the failed change.

**D3 — unresolved recovery blocks writes outright.** While a store is `Damaged`, `Unreadable`, or `WrittenByNewerVersion` and the user hasn't yet resolved it, every mutation is refused up front with the recovery message, and no in-memory change is made at all. This is the one place in Phase 1 where a mutation is *rejected* rather than accepted and banner-flagged — and it's what stops a damaged or foreign file from ever being overwritten by an ordinary edit made before the user has dealt with the prompt.

**D4 — the diagnostic log lives in local AppData, never roaming.** `places.json` roams by design; a machine's diagnostic log must not follow the user to another machine, and must not bloat a roaming profile with megabytes of history nobody asked to sync.

**D5 — no test constructs a `Window`.** Every test exercises services and models only, so no test needs an STA thread or a message pump. (Phase 1 targeted `net10.0-windows` and referenced the WPF project; since the merge the test project targets plain `net10.0` and links the UI-free files in, so it also runs on Linux. See "Merging Phase 1 with the feature work".)

**D6 — a load failure is classified at the `catch`, not after.** Reading the file and parsing it are two separate `try` blocks in `PlacesService.LoadFromDisk`. `IOException`/`UnauthorizedAccessException` (and, as a safe default, any exception type not specifically anticipated) mean the file could not be opened — `Unreadable`. `JsonException`, or a document that parses but isn't a usable store (no `schemaVersion`, a non-numeric one, or a null/missing `places` array), mean the content itself is damaged — `Damaged`. The reason this has to happen at the catch: once a failure is flattened down to a single boolean, the way `LoadFailed` used to, the distinction between "damaged" and "unavailable" is gone for good — there's no way to recover it downstream. An absent or non-numeric `schemaVersion` is deliberately classified `Damaged` rather than "assume version 1": every store this application has ever written includes the field, so one without it is not an old-but-valid v1 file, it's damaged or foreign.

### The asymmetry — the single most important rule in this phase

`Damaged` may, on an explicit user choice (never automatically), quarantine the file aside under a timestamped name and start empty. `Unreadable` and `WrittenByNewerVersion` may **never** quarantine, rename, or write to the file, and neither one ever offers a "start with an empty list" option, anywhere, under any condition.

The reason is not symmetry for its own sake: nothing about either `Unreadable` or `WrittenByNewerVersion` establishes that the data is damaged. A file another process — a sync client, antivirus, a backup tool — is holding for a few seconds is a far more likely explanation than data loss, and a file written by a newer build is intact by definition; this build just isn't new enough to safely interpret fields it doesn't recognize. Responding to either situation by renaming, replacing, or emptying the file would destroy data that was never actually at risk. `Unreadable`'s only actions are **Try again** (re-run the load) and **Show me the file**; `WrittenByNewerVersion`'s are **Exit** (listed first, as the recommended action) and **Show me the file**. Neither code path can reach `PlacesService.QuarantineAndStartEmpty()` — it is called from exactly one place in `App.xaml.cs`, gated to the `Damaged` case.

This is stated as plainly as possible because it's the rule a future contributor is most likely to break — not through carelessness, but through a well-meant tidy-up that collapses the three recovery paths into one shared "corrupted file" handler. Don't do that. `StoreLoadOutcome`'s own XML doc comments spell out, per member, exactly what recovery may and may not do with it, specifically so that refactor doesn't happen by accident.

### The two open questions from the plan, now settled

1. **Backup and quarantine file location.** `places.bak.json` (the previous file, kept by `File.Replace` on every successful save) and `places.corrupt-<timestamp>.json` (a quarantined damaged file) both stay as sibling files next to `places.json`, rather than moving into a `backups\` subfolder. A subfolder would be tidier, but a sibling file is much easier to talk a user through finding and recovering over the phone — "look for a file next to places.json" beats "open this subfolder you've never seen."
2. **Log retention.** Stays at 256 KB with a single rollover to `quickerplaces.1.log`, as originally guessed. Revisit only if a real intermittent failure shows up that needs a longer window to diagnose — there's no evidence yet that it does, and a bigger cap just means a bigger unattended failure loop could grow before anyone notices.

### Privacy and logging rules

`DiagnosticLog` never records a place's alias or destination. A save failure logs the record count and the store path — "42 place(s) failed to save to C:\...\places.json" — never the records themselves, so a failure to save data doesn't turn into a second, less-protected copy of that same data sitting in a log file. The one deliberate exception is a quarantine path, which is logged in full because it's exactly the filename the user needs to go find their preserved data.

Logging must never throw. The swallowed exception inside `DiagnosticLog.Write`'s own try/catch is the one place in this codebase where a silent catch is correct and intentional — the entire rest of Phase 1 exists to remove that exact pattern from everywhere else, and a logger that can crash the app it's supposed to be explaining would defeat its own purpose. (Two other silent catches remain, both narrowly scoped and commented as deliberate rather than left over from before this phase: `FilePlacesStorage.Write`'s best-effort cleanup of its own temp file after a failed write, and `ExplorerReveal.Reveal`'s guard around launching `explorer.exe`, which already has a fallback — the path is shown as text everywhere it's called from.)

### The test project

`src/QuickerPlaces.Tests` is an xUnit project (`Nullable` enabled, `ImplicitUsings` off to match the main project), split one class per area: `FilePlacesStorageTests`, `PlacesServicePersistenceTests`, `PlacesServiceLoadOutcomeTests`, `PlacesServiceFavouriteTests`, `PlacesServiceValidationTests`, `PlacesServiceRoundTripTests`, `PlacesStoreFixtureTests`, and `DiagnosticLogTests` — over 40 `[Fact]`s in total, covering (and in most cases exceeding) the 28 numbered cases in the plan's section 6.

Two test doubles do most of the work: `FakePlacesStorage`, an in-memory `IPlacesStorage` with `FailNextWrite`/`FailEveryWrite`/`ReadThrows` knobs, used for almost everything; and `TempDirectory`, an `IDisposable` wrapper around a uniquely-named real directory, used only by the handful of tests that have to exercise real `FilePlacesStorage` behaviour (backup-file creation, quarantine naming, temp-file cleanup, a file held open with `FileShare.None`) — no test ever touches a real AppData path.

`src/QuickerPlaces.Tests/Fixtures/places.v1.json` is a frozen copy of a real `places.json` — favourites, a non-favourite, and a URL — committed so `PlacesStoreFixtureTests` can catch an accidental serialization change against today's exact on-disk shape. It should be treated as frozen; if it ever needs to change, that's a sign something about the v1 format changed, which shouldn't happen.

### Verification status — read this before calling Phase 1 done

**Update 2026-09-21 — built and tested; not yet run by hand.** On a Windows machine with the .NET 10 SDK (10.0.401), from `src\`:

1. `dotnet build QuickerPlaces.sln` — succeeded, 0 warnings, 0 errors. **Done.**
2. `dotnet test QuickerPlaces.sln --no-build` — 41 passed, 0 failed, 0 skipped. **Done.**
3. The manual checklist below — **walked on 2026-09-25, in part.** The result is recorded against each item. Three passed: the damaged-file recovery and both second-instance checks. The other eight are **untested**, because they need a failed save (denied write permission), a file held open, a hand-set `schemaVersion`, a USB drive or a multi-monitor setup. The banner, the close prompt and the Unreadable and newer-version prompts have still not been seen on screen.

That leaves the gap the tests cannot close: every test runs on the storage seam and never constructs a `Window`, so the recovery dialogs, the banner, and second-instance activation are unproven. The original note is kept below, because it explains why the checklist exists.

*Original note (written when no .NET SDK was available):* Nothing in Phase 1 had been compiled and no test had been run. Every file was written and reviewed by hand — cross-checked against the plan, against the existing code's conventions, and for obvious mistakes — but that is not a substitute for `dotnet build`, `dotnet test`, or running the app.

### Manual verification checklist (must be walked on Windows)

- [ ] Deny write permission on `places.json`, add a place, and confirm: the banner appears, the new place stays visible on screen, and clicking **Retry** succeeds once permission is restored. → **Untested.**
- [x] Corrupt `places.json` by hand (break the JSON), launch, and walk all three recovery options for the `Damaged` case; confirm the quarantine file appears next to `places.json` and its bytes match the original corrupted content exactly. → **Pass.** A hand-broken JSON file was tried and the recovery flow worked. The quarantine byte comparison was not reported separately.
- [ ] Hold `places.json` open from another program, launch, and confirm: the message says the file **couldn't be opened**, not that it's damaged; **no** empty-store option is offered anywhere in the dialog; and **Try Again** recovers the real data once the other program releases the handle. → **Untested.**
- [ ] Set `schemaVersion` to `99` in `places.json`, launch, confirm the newer-version message appears, and confirm the file is byte-for-byte unchanged afterward. → **Untested.**
- [x] Launch a second instance while the first is minimized — confirm it comes forward (with the search box focused) instead of a second window opening. → **Pass.** Reported working in general use.
- [x] Launch a second instance while the first is behind another window — same confirmation. → **Pass.** Reported working in general use.
- [ ] Pull a USB drive mid-session with the store on it, if a removable-media path is actually testable in the environment; otherwise record it explicitly as untested rather than silently skipped. → **Untested.** No removable-media setup was tried.
- [ ] Confirm the unsaved-changes banner never steals keyboard focus while it's showing. → **Untested.** The banner needs a failed save, which was not set up.
- [ ] Confirm the window still restores correctly (position, size, DPI) on a high-DPI multi-monitor setup. → **Untested.**
- [ ] With the banner showing (write permission still denied), close the window: confirm it asks before closing, that **No** keeps the window open, and that **Yes** closes it. → **Untested.**
- [ ] Undo a Remove while write permission is denied: confirm the place comes back on screen and the banner appears. → **Untested.**

### Known limitation (Phase 1 as written; since replaced)

> *The merge replaced the `Topmost` toggle below with `AllowSetForegroundWindow`, called by the second launch. That is not the `SetForegroundWindow` workaround this note rules out: the launching process has foreground rights because the user just started it, and hands them to the running copy, which is the documented way to do this. Whether it reliably raises the window is on the manual checklist.*

`SingleInstance`'s activation handler calls `Activate()` on the existing window from a background thread-pool callback in response to a second launch attempt. Windows' foreground-activation rules mean this may only flash the taskbar button rather than actually raising the window, because the OS restricts which processes can steal foreground focus and this callback isn't running in direct response to user input. The accepted mitigation is a brief `Topmost` toggle immediately before `Activate()` (forcing the window to the top of the z-order without requesting foreground activation, so it isn't subject to the same restriction) — that's what's implemented. P/Invoking `SetForegroundWindow` was deliberately not used. If manual testing on Windows finds the `Topmost` toggle unreliable, that should be recorded here as a known limitation rather than reached for as a reason to add the P/Invoke call.

## Merging Phase 1 with the feature work

Phase 1 was built on its own branch (`claude/root-folder-tracking-periods-6wddrq`, PR #2), started from the initial upload, in parallel with the feature work above (search, hotkey, undo, copy, Settings) that reached `main` as PR #3. The two overlapped in 12 files. They were compared area by area and merged on 2026-09-25, keeping the stronger design in each place:

| Area | Kept | Why |
|---|---|---|
| Loading a bad `places.json` | Phase 1 | Classifying Damaged / Unreadable / WrittenByNewerVersion, blocking edits until resolved (D3), and quarantining only on an explicit choice is strictly safer than the feature branch's "copy it aside and start empty", which treated a briefly locked file as corrupt and ignored `schemaVersion`. |
| Save failures | Phase 1, plus the feature branch's close prompt | The Retry banner (D1/D2) replaces the one-time warning. Phase 1 had no guard on closing with unsaved changes, so the close prompt was kept: on close, `App` retries once and asks before discarding. |
| Writing the file | Phase 1 | `FilePlacesStorage.Write` flushes to disk, uses unique temp names and keeps `places.bak.json`. Export keeps the feature branch's atomic temp-and-move write. |
| Single instance | Feature branch, plus Phase 1's logging and exception guard | One named event is atomic, needs no mutex ownership, and can't be abandoned. `AllowSetForegroundWindow` from the second launch is the documented foreground hand-off, and activation reuses `BringToFront`, the same routine as the global hotkey. The activation callback is wrapped so a shutdown race can't crash the process. |
| Undo remove | Feature branch, adapted | `Remove` now returns `PersistenceResult` and has an `out RemovedPlace?`. `TryRestore` follows the Phase 1 pattern: `ValidationResult` plus `out PersistenceResult`, blocked while recovery is unresolved, and a forward change rather than a rollback (D1), so a failed save after an undo shows the banner like any other edit. The Phase 2 plan (Recently Deleted) builds on this. |
| Data folder | Merged | One `OpenDataFolderCommand` backs both the header's folder button and the banner's "Show Data Folder". `ExplorerReveal` now opens the folder itself when `places.json` doesn't exist yet, because `/select` on a missing file opens an unrelated folder. |
| Fixes only on the feature branch | Re-applied on Phase 1's `PlacesService` | Folder paths must be fully qualified, `null` entries in the file are dropped on load, an import file that repeats itself only offers the first copy, and export is atomic. |
| Test project | Feature branch's setup, Phase 1's tests | Plain `net10.0` linking the UI-free files, so the suite runs on Linux too (Phase 1's `net10.0-windows` project could only run on Windows). Phase 1's tests were adapted in two ways. Hard-coded `C:\...` folders go through `TestPaths.Folder`, because `C:\Docs` isn't a full path on Linux. `FailedWrite_LeavesNoTempFileBehind` keeps its file lock on Windows but blocks the replace with a directory elsewhere, because Linux file locks don't stop a rename. |
| Settings schema | Both | `AppSettings.CurrentSchemaVersion` is 2 (GlobalHotkey). It must not drop back to Phase 1's 1: builds from `main` already write 2, and `SettingsService.Load` silently resets a newer file to defaults. |

Two problems turned up during the merge and were fixed:

- **Tests wrote to the real log.** `PlacesService`, `FilePlacesStorage` and `SingleInstance` log as they work, and only `DiagnosticLogTests` redirected the log, then reset it to the real `%LocalAppData%` location. So every test run appended to the developer's own `quickerplaces.log`, against the "tests never touch real AppData" rule. A module initializer (`Fakes/TestLogDirectory`) now points the log at a temp folder before any test runs, and `DiagnosticLogTests` switch back to that folder, never to the real one. They also run in their own non-parallel collection, because the log folder is process-wide and a test logging in parallel could land in their folder.
- **The recovery flow made the old startup notice unreachable.** The feature branch's "your places couldn't be read" notice in `MainWindow.Window_Loaded` was removed, because `App` now resolves any load problem through the recovery dialog before the window exists.

## Phase 2 — Seven-day Recently Deleted

Phase 2 is planned in detail in [`ai/260925_Phase 2 Detailed Plan.md`](260925_Phase%202%20Detailed%20Plan.md). Its decisions D7–D22 are in that plan's section 4 and are not restated here. This section records what actually landed, where the build departs from the plan and why, and what is still unproven.

It was implemented on 2026-09-25 on `claude/roadmap-status-4tv9nf`, one commit per step of the plan's section 10 (`ab43db7` to `cbde92f`). Every deviation below is also recorded in the commit message of the step that made it. **Phase 2 was implemented at the user's explicit request, ahead of the Phase 1 manual checklist**, which decision 4 of `ai/260921_Handoff.md` said should gate it. So Phase 2 is built on save, recovery and banner paths that have still not been exercised in the running app. Both checklists now need walking, Phase 1's first.

### What was built

- **Schema v2** (`PlacesService.CurrentSchemaVersion = 2`). `Place.DateAdded` is a UTC `DateTimeOffset`, and `Place.DeletedAt` is a nullable UTC `DateTimeOffset`, written only when set. `PlacesStoreMigration.MigrateV1ToV2` rewrites a v1 document on the `JsonObject`, before anything is bound to `Place` (D11). It converts a `dateAdded` with an offset or `Z` exactly. It reads an offset-less value as local time in the injected zone, taking the standard offset in a DST gap or overlap. A missing value becomes "now". Anything else throws `JsonException`, so D6 classifies the store `Damaged`. The migration happens in memory only, is logged with counts and the zone id (never an alias or destination), and reaches disk with the next successful save. The load gate treats versions below 1 as `Damaged`. Version 1 is migrated, version 2 is bound directly, and anything newer is `WrittenByNewerVersion`.
- **One clock.** `PlacesService(IPlacesStorage, TimeProvider? = null)` (D12). `DateAdded`, `DeletedAt`, purge decisions, the migration's zone, the quarantine file name and the dialog's countdown (`PlacesService.UtcNow`) all read it. Tests use `Fakes/ManualTimeProvider` and `Fakes/TestZones`, which are built with `CreateCustomTimeZone` so no test depends on the host's zone.
- **Soft delete (D7–D10).** `Remove(place, out bool removed)` stamps `DeletedAt` on the same record in the same list slot. Its favourite fields become a remembered bubble slot, and only the active favourites are renumbered. `Places` is now the active records, as a fresh list per call. `RecentlyDeleted` returns the rest, newest first. Every enumeration of the list was given an active-or-deleted treatment (plan 5.3). `RemovedPlace` is gone, and the Undo stack holds `Place`. `TryRestore(place, out persistence)` clears the flag and puts a favourite back in its remembered slot, shifting only active favourites.
- **Restore conflicts and permanent deletion (D15, D22).** `GetRestoreConflict` names the active place holding the alias, the destination, or both, with a user-facing `Explanation` that is never logged. `TryRestore(place, alias, resource, out persistence)` commits the edit-before-restore flow. `RestoreSelected` restores what is free in one save, newest deletion first, and returns the rest as conflicts. `DeletePermanently` ignores anything that is not deleted, and `EmptyRecentlyDeleted` keeps active places. Each writes once, or not at all when nothing changed, and each checks D3 first.
- **Expiry (D13, D14).** `RecentlyDeletedPolicy` defines seven full days as 168 hours of elapsed UTC time. Days remaining is the ceiling of the time left, clamped to 0–7: "7 days" … "1 day", then "Expiring" once past expiry but not yet purged (still restorable). `PurgeExpired` runs only after a successful load (the constructor and `Reload`, outcome `Ok`) and at the top of `Persist()`. It returns early while recovery is unresolved, and logs a count and the trigger.
- **Export and import (D16, D17).** Export filters out deleted places even when given them, and writes version 2. Import reads the version first: missing is treated as 1, 1 is migrated like the store, 2 is read as is, and newer versions are refused with a message. Records carrying `deletedAt` are dropped. Collision checks see active places only.
- **The UI.** A header bin button (U+E74D; tooltip, and `AutomationProperties.Name="Recently Deleted"`) opens `RecentlyDeletedDialog` (D20). The dialog has an extended-selection grid (Alias with type glyph, Type, Path / URL, Deleted in local time as `g`, and Days remaining with an exact-expiry tooltip), **_Restore selected**, **_Delete selected permanently**, **_Empty Recently Deleted**, and **Close** as both default and cancel button. It shows an empty state and has its own error line. `PlaceFormDialog.ShowRestore` ("Restore Place") is the one conflict flow, shared by Ctrl+Z, the status bar's Undo and Restore selected. `MessageForm.ShowDestructiveConfirm` makes Cancel the default, the cancel button, and the focused button (D18). Remove no longer asks. Its menu item shows "Delete" as its gesture and has a tooltip, and the status bar says "Moved "X" to Recently Deleted." with Undo. The bar stays 10 s with Undo and 8 s otherwise, and pauses under the pointer or keyboard focus (D19). After Delete, the row that took the removed row's place is selected and focused. The empty-grid hint mentions Recently Deleted when it has items.

### Where the build departs from the plan

| Plan | Built | Why |
|---|---|---|
| Plan section 6: only `PlacesStoreMigration.cs` and `RecentlyDeletedPolicy.cs` are newly linked into the tests; section 3 lists only a row view model | A second view model, `RecentlyDeletedViewModel`, holds every decision the dialog makes (rows, enabling, confirmation wording, error line, each service call). It is linked into the tests with `RecentlyDeletedRowViewModel`, `PlaceViewModel` and `ObservableObject` | So the dialog's behaviour is tested without a `Window` (D5, D21). Consequence: `PlaceViewModel` and `ObservableObject` must now stay free of `System.Windows.*` (step 8, `648bcaf`) |
| `ShowRestore(Place, string explanation, PlacesService)` | `ShowRestore(RestoreConflict, PlacesService, Window? owner)`. Focus starts in the field that is in the way (the alias when both are), with its text selected. The explanation is also each field's screen-reader help text | Knowing *which* field conflicts needs the conflict, not its text. The owner centres it over Recently Deleted (`648bcaf`) |
| Restore selected runs `ShowRestore` for each returned conflict | Each conflict is re-read just before its form opens (`RecentlyDeletedViewModel.CurrentConflict`). One that no longer conflicts is restored directly | An earlier edit in the same batch may have freed or taken an alias or destination (`648bcaf`) |
| Cancelling Undo's conflict flow shows the reason | The status bar says ""X" is still in Recently Deleted." Only a place that has left Recently Deleted gets the modal message | The reason was on screen a moment before, so a second modal adds nothing (`cbde92f`) |
| Plan 5.3 row 23: insert at `Places.IndexOf(place)` | `InsertRestored` counts only the active places that already have a row | A batch restore hands back several places, not in list order. The raw index would put a row after siblings that have no row yet (`77e7eba`) |
| After Recently Deleted closes: insert, `RebuildFavourites`, `RefreshPersistenceState`, `PruneUndoStack` | Also clears a search that would hide a restored place, and hides a status message whose Undo would now restore a different place than the one it names | Found while wiring it in (`cbde92f`) |
| D17: missing, 1, 2, newer, non-numeric | Also refuses a version below 1, and a document whose root is not an object, as "That file isn't a QuickerPlaces export." | No build wrote such a version, and the store's gate already treats it as unknown (`e96e74b`) |
| Migration sketch: `JsonValue.TryGetValue<DateTime>` | Values are read through a `JsonElement` | See the `JsonValue` quirk below (`e835a75`) |
| Confirmations: "Permanently delete {n} place(s)?" | Names the place when there is one ("Permanently delete "Docs"? …"), otherwise "{n} places" | Clearer for the common single case (`648bcaf`) |
| Access keys on the dialog's buttons | The theme's `Button` and `Button.Primary` templates now set `RecognizesAccessKey`. This is app-wide; no existing label contained an underscore | Without it, "_Restore selected" rendered a literal underscore (`648bcaf`) |
| Status bar pause and resume (D19) | The pause is also cleared whenever the bar hides | The bar can collapse under its own Dismiss click, and then the `MouseLeave` never comes, which would strand the next message on screen (`cbde92f`) |
| Section 10 step 4 says the edit flow arrives in step 7; step 8 lists it | Built in step 8 | The plan contradicts itself; step 7 built the service half, step 8 the dialog |
| D15: "the usual live validation" | Validation on **Restore**, shown in the error line | That is what "usual" means in `PlaceFormDialog`: no mode validates as you type |
| Plan 5.1: "Every build before this phase refuses the v2 file" | True only for builds with Phase 1's version gate (`main` from the Phase 1 merge, PR #5, onwards) | Builds before that (`main` at PR #3 and earlier) never read `schemaVersion`. They would load a v2 file, show deleted places as ordinary ones (they ignore `deletedAt`), and write it back as v1 without the flag on their next save. Found while writing the documentation. No Phase 2 code can prevent it, so the user guide warns about it |

### Bugs and edge cases found and fixed along the way

1. **Duplicate property names crashed the launch.** `JsonObject` cannot hold a duplicated key. Parsing one threw `ArgumentException`, which D6's catch does not classify, so it escaped the constructor. Both the store and import now parse with `AllowDuplicateProperties = false`, so such a store is `Damaged` and such an import is refused. QuickerPlaces never writes a duplicate key (`7a61718`).
2. **Dates at the calendar's edge.** An offset-less `"0001-01-01T00:00:00"` (a default `DateTime`), migrated in a zone east of Greenwich, made the `DateTimeOffset` constructor throw `ArgumentOutOfRangeException`, which also escapes D6. The migration now builds the value from clamped ticks (`e835a75`).
3. **The `JsonValue` parsing quirk.** `JsonValue.TryGetValue<DateTime>` accepts a string node that was *parsed*, but refuses one *built in code*. The migration would have treated the same document differently depending on how it was made. Reading through a `JsonElement` uses System.Text.Json's own ISO 8601 parser either way (`e835a75`).
4. **A stray `deletedAt` in a v1 record** meant nothing to v1, which ignored unknown properties. After the upgrade it would have meant "removed". The migration drops it, for the store and for v1 imports (`e835a75`, `e96e74b`).
5. **The old gate let `schemaVersion` 0 through** as if it were current (a `< current` branch). Versions below 1 are now `Damaged` (`7a61718`).
6. **Batch-restored rows landing out of order** and **a status message stuck on screen**: both are in the table above (`77e7eba`, `cbde92f`).

### Verification status — read this before calling Phase 2 done

**2026-09-25: tested and compile-checked on Linux; not run.**

1. `dotnet test QuickerPlaces.Tests/QuickerPlaces.Tests.csproj` from `src/`: **237 passed**, 0 failed, up from 125. The new classes are `PlacesServiceClockTests`, `PlacesStoreMigrationTests`, `PlacesServiceSchemaV2Tests`, `PlacesServiceSoftDeleteTests`, `PlacesServicePurgeTests`, `PlacesServiceRecentlyDeletedTests`, `RecentlyDeletedPolicyTests`, `RecentlyDeletedRowViewModelTests` and `RecentlyDeletedViewModelTests`. Export, import and log cases extend the existing classes. The commit messages map each step to the plan's numbered tests 1–54, plus extras for the edge cases above.
2. Time zones: no test reads the machine's zone. The DST cases were also checked against the real `Europe/Berlin` zone, and the suite was run under several `TZ` settings (`e835a75`).
3. Fixtures: `places.v1.json` is unchanged and now pinned by a SHA-256 of its text. `places.v2.json` is new, frozen and pinned the same way, and a test shows it is exactly what this build writes.
4. `dotnet build QuickerPlaces/QuickerPlaces.csproj -p:EnableWindowsTargeting=true`: builds with 0 warnings and 0 errors, XAML included. **The app has not been run.** Nothing in the UI half of step 8 has been seen on screen.
5. The manual checklist below: **walked on 2026-09-25, in part**, as was Phase 1's. The result is recorded against each item. The everyday UI passed, and the upgrade was confirmed from the user's own data. Two findings were fixed, and the re-check passed (see below). Downgrade refusal, expiry, the failure cases and the screen-reader checks are **untested**.

### Found by the manual walk, 2026-09-25

1. **Enter in Recently Deleted restored the selection.** `FocusAfterAction` focused the first row with `MoveFocus(Next)`, copied from `MainWindow.FocusGridRow`. The dialog's grid has `TabNavigation="Once"`, so that moved the focus out of the grid onto **Restore selected**, and Enter clicked it. The dialog now focuses the row's first cell.
2. **Plain letters fired access keys.** WPF fires an access key on a plain letter that nothing took as text input. The fix above does not stop this: R still restores with a read-only `DataGrid` focused. The user chose to require Alt. `App.RequireAltForAccessKeys` marks unhandled plain text input as handled at the window, so it never reaches `AccessKeyManager`. Text boxes take their own typing first, Alt+letter arrives as `SystemText`, and menus open in popups, not in a `Window`. So this applies app-wide, to every future underscore label as well.
3. **Restored places appeared only after the dialog closed.** The dialog now raises `RecentlyDeletedViewModel.Changed` after every action, with the places that action restored. `MainViewModel.SyncWithRecentlyDeleted` then updates the grid, favourites, banner, Undo stack and status bar straight away. A test covers the event. This supersedes the Phase 2 hand-off's note (§5) that the banner is refreshed after the dialog closes.

Items 1 and 2 were checked with a throwaway WPF probe. It used the same grid setup, and WPF's own input pipeline, because keystrokes sent by Windows go to whichever window is in front. With the old focus code, the focus landed on a `Button` and Enter restored. With the fix, it landed on a `DataGridCell` and Enter moved down a row. Plain R restored in both cases until the Alt rule was added, and after that it did nothing, while a text box still took typing. Alt+letter could not be synthesised that way. The user's re-check in the real app confirmed Alt+R works, plain R does nothing, and Enter no longer restores.

### Manual verification checklist (must be walked on Windows)

The plan's section 8, merged with the UI checks listed by step 8. Record a result against each item. An item that cannot be tested is recorded as untested, not skipped.

**Upgrade and downgrade**

- [x] **Upgrade a real file.** Copy a `places.json` written by a pre-Phase-2 build into place and launch. Places, favourites and the Date Added column are unchanged. The file is still `schemaVersion` 1 on disk until the first change. After one change it is 2, with `+00:00` dates, and `places.bak.json` is the v1 file. The log has the migration line with counts and a zone id, and no alias. → **Pass.** Confirmed from the user's own data: the log has the migration line (1 place, zone "Eastern Standard Time", no alias), and `places.json` is v2. `places.bak.json` is v2 too, as expected after more than one save; its brief v1 stage was not observed.
- [ ] **Downgrade refusal.** Start a build that has Phase 1's version gate but predates Phase 2 (`main` at `ca0ac72`) against the v2 file. The newer-version prompt appears, and the file is byte-identical afterwards. (Builds before the Phase 1 merge do not refuse; see the last row of the table above.) → **Untested.** No `ca0ac72` build was run against the v2 file.

**Header**

- [x] The bin icon renders (Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10). It has its tooltip, and a screen reader announces "Recently Deleted". → **Pass.** Icon and tooltip, on Windows 11. The screen-reader announcement is **untested**.
- [x] The header fits at the 700 px `MinWidth`, and on a high-DPI display. → **Pass.** At 700 px. High DPI not specifically tried.

**Remove and Undo**

- [x] Remove, from the context menu and with Delete: no confirmation, the row and its bubble go, and the status bar says "Moved "X" to Recently Deleted." with **Undo**. The menu item shows "Delete" as its gesture, and its tooltip. → **Pass.**
- [x] After Delete, the next row (the previous one, if it was the last) is selected and focused, and Delete again removes it. Removing the last row leaves the focus on the grid, and Ctrl+Z then works. → **Pass.**
- [x] Status bar: it stays about 10 s with Undo and about 8 s without. Hovering pauses it, and leaving restarts the full interval. Tabbing into it pauses it. It never takes focus when it appears. After dismissing it, a new Remove still times out. → **Pass.**
- [x] Ctrl+Z after the bar has gone still restores. Undo of a removed favourite puts its bubble back in the same position. So does restoring it from Recently Deleted after closing and reopening the app. → **Pass.**
- [x] **Undo conflict.** Remove "Docs", add a new "Docs", press Ctrl+Z. Restore Place explains the conflict (a screen reader reads the explanation). Both fields are prefilled, the conflicting field is focused with its text selected, and **Browse...** is there for folders. Restoring with a new alias brings the row back in its old position, bubble slot included. Repeat and **Cancel**: the status bar says it is still in Recently Deleted, and it is in the dialog. → **Pass.** The screen-reader part is **untested**.

**The Recently Deleted dialog**

- [x] It opens centred on the main window, with the first row selected and focused, or with **Close** focused when empty. → **Pass.**
- [x] Deleted shows local time in the user's locale format. The Days remaining tooltip shows the exact expiry. Both headers sort chronologically. → **Pass.**
- [x] Shift and Ctrl multi-select work. **Restore selected** and **Delete selected permanently** are enabled only with a selection. → **Pass.**
- [x] Alt+R, Alt+D and Alt+E work and show their underlines. No button anywhere in the app shows a stray underscore. → **Pass.**
- [x] **Check:** does a plain R, D or E (no Alt), pressed while the dialog's grid has focus, fire an access key? WPF can do this. R would restore the selection (reversible); D and E would only open a confirmation that defaults to Cancel. Record what happens. → **Found: yes, they fired.** The cause was partly the bug in the next item, and partly WPF itself: the probe under "Found by the manual walk" shows plain R still restores with the grid focused. The user chose to require Alt, so a plain letter no longer fires an access key anywhere in the app (`App.RequireAltForAccessKeys`). **Re-checked 2026-09-25: pass.** Plain R does nothing, and Alt+R restores.
- [x] Esc closes the dialog. Enter closes it: record whether it does when the grid has focus, where the `DataGrid` may take Enter itself. Delete in its grid does nothing. The whole dialog works from the keyboard, and Tab leaves the grid in one step. → **Found: Enter restored the selection instead of closing.** The dialog focused its first row with `MoveFocus(Next)`, as `MainWindow.FocusGridRow` does. Because this grid has `TabNavigation="Once"`, that moved the focus out of the grid onto **Restore selected**. `FocusAfterAction` now focuses the row's first cell. With the grid focused, Enter now moves to the next row (standard `DataGrid` behaviour) and does not close the dialog. From anywhere else, Enter closes it through the default Close button. Esc was reported working. **Re-checked 2026-09-25: pass.**
- [x] **Restore selected** with a mixed selection: the free places restore at once, each conflict opens Restore Place over the dialog one at a time, and each can be cancelled on its own. → **Pass.**
- [x] Both confirmations: Enter, Space, Esc and the title-bar X all cancel. The action buttons read "Delete permanently" and "Empty Recently Deleted". → **Pass.**
- [x] The empty state text shows, with the action buttons disabled. → **Pass.**
- [x] After closing: restored rows are in their original positions, bubbles are in order, a search that would hide a restored row is cleared, and Ctrl+Z skips places the dialog restored or deleted. → **Pass.** Changed during the walk: the main window now catches up after each action, while the dialog is still open, rather than after it closes (`RecentlyDeletedViewModel.Changed`).
- [x] The empty-grid hint mentions Recently Deleted only when it has items. → **Pass.**

**Expiry**

- [ ] With the app closed, edit a `deletedAt` in `places.json` to eight days ago, then launch. That record is not in the dialog, the log records one purge "after load", and the file still holds the record until the next change. → **Untested.**

**Failures**

- [ ] Deny write permission on `places.json`. A Remove shows the banner, and so does an Undo. Retry clears it once permission returns. → **Untested.**
- [ ] With write permission still denied: each dialog action shows the error line in the dialog, and the banner appears after closing. Restoring permission and using Retry clears both. → **Untested.**

### Known issue found, not fixed (since resolved for the grid by Phase 3)

**Dates in the main grid ignore the user's locale.** Nothing sets `FrameworkElement.Language`, so WPF formats bindings as en-US: the Date Added column's `{0:d}` shows month/day/year whatever the Windows region is. The Recently Deleted dialog formats its dates in code with `CultureInfo.CurrentCulture`, so the two windows can disagree. This predates Phase 2 and was found in step 8. The usual fix is to override `FrameworkElement.LanguageProperty`'s metadata from `CultureInfo.CurrentCulture.IetfLanguageTag` once at startup. Phase 3's Last Opened column would inherit the problem.

*Resolved for the grid in Phase 3:* Date Added left the grid, and Last Opened is formatted in the view model with `CultureInfo.CurrentCulture` (Phase 3 D31), so the main grid no longer formats any date through a `StringFormat`. `FrameworkElement.Language` is still not set. That only matters if a later phase adds a XAML `StringFormat` date (Phase 3 plan §12, question 6).

## Phase 3 — Usage tracking and sorting

Phase 3 is planned in detail in [`ai/260925_Phase 3 Detailed Plan.md`](260925_Phase%203%20Detailed%20Plan.md). Its decisions D23–D33 are in that plan's section 4 and are not restated here. It was implemented on 2026-09-25 on `claude/phase-3-usage-tracking`, one commit per step of the plan's section 10 (`e250fbb` to `3e77208`), plus one fix from a self-review (`479388b`). Phase 2 had been merged to `main` (PR #6) before the plan was written.

### What was built

- **Schema v3** (`PlacesService.CurrentSchemaVersion = 3`). `Place` gains `Id` (a `Guid`, declared first so it is written first), `LastOpenedAt` (UTC, written only when set) and `OpenCount`. `PlacesStoreMigration.MigrateV2ToV3` gives every record a fresh id and removes any stray `lastOpenedAt`/`openCount`, which v2 never gave a meaning. The loader chains v1 → v2 → v3 in memory and still never writes. Version 3 binds directly, and 4 is now `WrittenByNewerVersion`. After binding, an offset `LastOpenedAt` becomes UTC, a negative `OpenCount` becomes 0, and an empty or repeated `Id` is replaced (the first holder keeps a repeated one). The load log line counts those normalisations, never naming a place.
- **A stable place identity (D27)**, assigned by the migration, `TryAdd` and import, and never changed by rename, edit, remove or restore. Nothing displays it yet; Phase 5 keys per-file application choices by it.
- **Recording an open (D24, D26).** `PlacesService.RecordOpen` is the only writer of usage: it stamps the injected clock's now, adds one (saturating at `int.MaxValue`) and saves at once. It ignores a deleted place or one not in the store, and recovery refuses it like every mutation. `PlaceLauncher`, over an `IShell` seam, is the only caller. It records an open only when the folder pre-check passed (URLs have none) and the shell did not throw. `MainViewModel.Open` just turns its `OpenOutcome` into messages. `WindowsShell` is the production `Directory.Exists` and `Process.Start`. A failed launch logs the place type and exception type only; a recorded open logs nothing.
- **Export and import (D33).** Export writes version 3 with the new fields. Import runs the store's migration chain, and keeps each candidate's `DateAdded`, `LastOpenedAt` and `OpenCount` (normalised as on load). It keeps its `Id` unless the id is empty or already held by any record, active, deleted, or earlier in the batch. Favourite state is still not imported.
- **Sorting (D29, D30).** `PlaceSort` is a UI-free comparer over seven keys (every column plus `DateAdded`), with ties broken by alias then destination, and never-opened places treated as the oldest. `PlaceSort.Next` is the header-click cycle: a column's first direction (newest, most, favourites first for the usage columns; A–Z for text), then the other, then back to stored order. `MainWindow` replaces the DataGrid's own sorting with a `Sorting` handler that calls `MainViewModel.SortBy`. The sort is applied as the view's `CustomSort`, never to `Places`. `AppSettings` v3 remembers it as two strings, read leniently by `PlaceSort.Parse`.
- **The grid (D31, D32).** Date Added is replaced by **Last Opened** (local short date and time, or "—") and a right-aligned **Opens**. When the grid is sorted by Last Opened or Opens, a recorded open re-sorts the view at once.

### Where the build departs from the plan

| Plan | Built | Why |
|---|---|---|
| 5.1: the migration assigns `record["id"]` | It writes the Guid's text (`ToString("D")`), not a Guid-typed node | A `JsonValue` built in code from a `Guid` refuses `GetValue<string>`, which a parsed one accepts: the same class of quirk as Phase 2's dates. Written as text, the migrated tree looks exactly like one parsed from a v3 file (`e250fbb`) |
| Section 7: the existing fixture tests change only their assertions | Phase 2's `V2FixtureFile_IsWrittenBackExactly` is replaced by `V3FixtureFile_IsWrittenBackExactly` | A v3 build migrates `places.v2.json`, so it can no longer write that file back unchanged. The v2 fixture itself is untouched (`58ac9de`) |
| Section 10 step 5 updates the version-pinned export and import tests | Done in step 2 | Bumping `CurrentSchemaVersion` in step 2 broke them at once: exports read 3, and 3 was no longer "newer". The purge test's `WrittenByNewerVersion` case moved to 4 too (`58ac9de`) |
| Step 2: `CommitImport` unchanged until step 5 | Step 2 gives imported places a fresh `Id` | Otherwise step 2 would have added imported places with `Guid.Empty` (`58ac9de`) |
| Section 7: tests 28–33 extend `PlacesServiceTests` | Test 29 extends it; the rest are in `PlacesServiceImportExportV3Tests` | Easier to read as a group (`d88a252`) |
| — | Phase 2's `CommitImport_StampsTheClock` became `CommitImport_KeepsTheCandidatesDateAdded_AsUtc` | D33 reverses what it asserted (`d88a252`) |
| — | `PlaceType.Label()` holds "Folder"/"URL", used by both `PlaceViewModel.TypeLabel` and the type sort | So the sort compares exactly the text the column shows, without a service depending on a view model (`0d6d307`) |
| D30: `Parse` returns null for anything unrecognised | Matches key names exactly (case-insensitive) rather than through `Enum.TryParse` | `Enum.TryParse` also accepts numbers, padding and flag syntax: a hand-edited `"Alias, Type"` read as `Type`. Found in a self-review of the diff (`479388b`) |
| D31: fixed widths for Type, Favourite, Last Opened and Opens (90, 90, 150, 70), with Opens right-aligned | Every column is proportional (star) with a `MinWidth`, and the short columns have a `MaxWidth` too. Favourite and Opens are centred, header included. The theme's `DataGridColumnHeader` template is replaced so a sorted column shows an accent chevron (Segoe `E70E`/`E70D`) and a brighter title, app-wide, which includes Recently Deleted | The user's first look at the running app. Fixed columns never shrink until the star columns are crushed, and a column whose edge is dragged becomes fixed, so a narrower window squeezed Alias and Path / URL to nothing. The default header's sort triangle was all but invisible on the dark theme. The widths were checked in a WPF window resized in code, and the headers by rendering the real theme offscreen |
| Section 10: run the suite under two `TZ` values after steps 2, 3 and 7 | Not done | The suite ran on Windows, where .NET ignores `TZ`. Every date test in this phase injects its zone (`ManualTimeProvider`, `TestZones`, and `FormatLastOpened`'s zone parameter), so none reads the machine's. Run the `TZ` pass in the next Linux session anyway |

### Verification status — read this before calling Phase 3 done

**2026-09-25: all 311 tests pass on Windows, and the app builds with 0 warnings. The app has not been run.** Nothing added in step 7 (the columns, sorting, arrows, re-sort after an open) has been seen on screen. The session that built it deliberately didn't launch the app: `places.json` lives at a fixed `%AppData%` path with no override, and the first open would have migrated the real store to v3.

### Manual verification checklist (must be walked on Windows)

The items, with what each must show, are the Phase 3 plan's section 8. Record results here, against each item. Anything that can't be tested is recorded as untested, not skipped.

- [ ] Upgrade a real file
- [ ] Downgrade refusal
- [ ] Every open path counts once
- [ ] Missing folder
- [ ] Failed launch
- [ ] Sorting
- [ ] Re-sort after an open
- [ ] Undo while sorted
- [ ] Remembered
- [ ] Search with a usage sort
- [ ] Width
- [ ] Locale
- [ ] Save failure
- [ ] Export and import

## Current status

As of 2026-09-25, Phases 1 and 2 are on `main` (Phase 2 through PR #6), with their manual checklists closed. The user accepted the untested items as untested; they stay recorded as untested and are not to be read as passed.

Phase 3 is implemented on `claude/phase-3-usage-tracking`, which is not yet pushed or merged. All 311 tests pass, and the solution builds with 0 warnings on Windows (`dotnet build QuickerPlaces.sln`, `dotnet test QuickerPlaces.sln` from `src\`). Its manual checklist (above) has not been walked. Next: walk it, fix what it finds, merge through a pull request, then write the Phase 4 detailed plan. Where to pick up is in `ai/260925_Phase 3 Handoff.md`. The roadmap itself is `ai/260901_Professional Improvements Plan.md`, indexed in `ai/README.md`.

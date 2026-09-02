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

## Current status (before Phase 1)

Delivered as a working project structure with two real runtime bugs found and fixed via user testing in Visual Studio, plus one binding gotcha caught in review before that. It has **not** been compiler-verified end-to-end in this environment — every fix was applied by careful manual read-through rather than an actual `dotnet build`. Treat it as a strong, mostly-working draft rather than a guaranteed-clean build; the next useful step is a full build + a pass through every feature (add/rename/edit/remove, favourite/reorder, export/import, corrupt-file recovery) in Visual Studio.

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
- **`SingleInstance`** wraps a named mutex and activation event so a second launch never constructs a `PlacesService` at all.

### Design decisions (D1–D6), as settled outcomes

**D1 — a failed save does not roll back the in-memory change.** The alternative was reverting to the last-persisted state on a failed write. That was rejected: rolling back throws away whatever the user just typed, and it leaves **Retry** with nothing to retry. Instead, the proposed state stays in memory, `HasUnsavedChanges` goes true, and the application simply stops claiming the change is stored. Only a real successful write clears the flag. This is also what makes Retry trivial to implement — see D2.

**D2 — every save is a whole-store write.** No append log, no per-record write, no diff. At the data scale here (hundreds of records, a file measured in kilobytes) that complexity buys nothing, and it would complicate both the version gate and the Recently Deleted purge planned for Phase 2. The practical payoff of D2 is that **Retry needs no queue of pending operations** — it just re-serializes and rewrites whatever is currently in memory, which by D1 already includes the failed change.

**D3 — unresolved recovery blocks writes outright.** While a store is `Damaged`, `Unreadable`, or `WrittenByNewerVersion` and the user hasn't yet resolved it, every mutation is refused up front with the recovery message, and no in-memory change is made at all. This is the one place in Phase 1 where a mutation is *rejected* rather than accepted and banner-flagged — and it's what stops a damaged or foreign file from ever being overwritten by an ordinary edit made before the user has dealt with the prompt.

**D4 — the diagnostic log lives in local AppData, never roaming.** `places.json` roams by design; a machine's diagnostic log must not follow the user to another machine, and must not bloat a roaming profile with megabytes of history nobody asked to sync.

**D5 — the test project targets `net10.0-windows`, and no test constructs a `Window`.** It references the WPF project directly (so `UseWPF` has to be on in the test `.csproj`, purely to pull in the framework references the referenced assembly needs — the tests themselves never touch WPF types), and every test exercises services and models only. No test needs an STA thread or a message pump.

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

`src/QuickerPlaces.Tests` is a new xUnit project (`net10.0-windows`, `Nullable` enabled, `ImplicitUsings` off to match the main project) referencing `QuickerPlaces.csproj` directly, split one class per area: `FilePlacesStorageTests`, `PlacesServicePersistenceTests`, `PlacesServiceLoadOutcomeTests`, `PlacesServiceFavouriteTests`, `PlacesServiceValidationTests`, `PlacesServiceRoundTripTests`, `PlacesStoreFixtureTests`, and `DiagnosticLogTests` — over 40 `[Fact]`s in total, covering (and in most cases exceeding) the 28 numbered cases in the plan's section 6.

Two test doubles do most of the work: `FakePlacesStorage`, an in-memory `IPlacesStorage` with `FailNextWrite`/`FailEveryWrite`/`ReadThrows` knobs, used for almost everything; and `TempDirectory`, an `IDisposable` wrapper around a uniquely-named real directory, used only by the handful of tests that have to exercise real `FilePlacesStorage` behaviour (backup-file creation, quarantine naming, temp-file cleanup, a file held open with `FileShare.None`) — no test ever touches a real AppData path.

`src/QuickerPlaces.Tests/Fixtures/places.v1.json` is a frozen copy of a real `places.json` — favourites, a non-favourite, and a URL — committed so `PlacesStoreFixtureTests` can catch an accidental serialization change against today's exact on-disk shape. It should be treated as frozen; if it ever needs to change, that's a sign something about the v1 format changed, which shouldn't happen.

### Verification status — read this before calling Phase 1 done

**Nothing in Phase 1 has been compiled, and no test has been run.** This environment has no .NET SDK available, the same constraint noted in the "Current status" section above for the original build. Every file was written and reviewed by hand — cross-checked against the plan, against the existing code's conventions, and for obvious mistakes — but that is not a substitute for `dotnet build` and `dotnet test`, and it is not a substitute for actually running the app.

Before Phase 1 can be considered done, on a real Windows machine with the .NET 10 SDK:

1. Run `dotnet build QuickerPlaces.sln` and confirm it builds clean.
2. Run `dotnet test` and confirm all tests in `QuickerPlaces.Tests` pass.
3. Walk the manual checklist below.

### Manual verification checklist (must be walked on Windows)

- [ ] Deny write permission on `places.json`, add a place, and confirm: the banner appears, the new place stays visible on screen, and clicking **Retry** succeeds once permission is restored.
- [ ] Corrupt `places.json` by hand (break the JSON), launch, and walk all three recovery options for the `Damaged` case; confirm the quarantine file appears next to `places.json` and its bytes match the original corrupted content exactly.
- [ ] Hold `places.json` open from another program, launch, and confirm: the message says the file **couldn't be opened**, not that it's damaged; **no** empty-store option is offered anywhere in the dialog; and **Try Again** recovers the real data once the other program releases the handle.
- [ ] Set `schemaVersion` to `99` in `places.json`, launch, confirm the newer-version message appears, and confirm the file is byte-for-byte unchanged afterward.
- [ ] Launch a second instance while the first is minimized — confirm it comes forward instead of a second window opening.
- [ ] Launch a second instance while the first is behind another window — same confirmation.
- [ ] Pull a USB drive mid-session with the store on it, if a removable-media path is actually testable in the environment; otherwise record it explicitly as untested rather than silently skipped.
- [ ] Confirm the unsaved-changes banner never steals keyboard focus while it's showing.
- [ ] Confirm the window still restores correctly (position, size, DPI) on a high-DPI multi-monitor setup.

### Known limitation

`SingleInstance`'s activation handler calls `Activate()` on the existing window from a background thread-pool callback in response to a second launch attempt. Windows' foreground-activation rules mean this may only flash the taskbar button rather than actually raising the window, because the OS restricts which processes can steal foreground focus and this callback isn't running in direct response to user input. The accepted mitigation is a brief `Topmost` toggle immediately before `Activate()` (forcing the window to the top of the z-order without requesting foreground activation, so it isn't subject to the same restriction) — that's what's implemented. P/Invoking `SetForegroundWindow` was deliberately not used. If manual testing on Windows finds the `Topmost` toggle unreliable, that should be recorded here as a known limitation rather than reached for as a reason to add the P/Invoke call.

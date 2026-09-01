---
title: QuickerPlaces — Phase 1 Detailed Implementation Plan
status: ready to implement
created: 2026-09-01
parent: ai/260901_Professional Improvements Plan.md
covers: sections 4.1 to 4.6 (Persistence reliability and recovery)
last_revised: 2026-09-01 — split a failed load into damaged content and a file that could not be opened (D6)
---

# Phase 1 — Persistence reliability and recovery

## 0. How to use this document

This is the file-level and signature-level plan for Phase 1 of the [Professional Improvements Plan](260901_Professional%20Improvements%20Plan.md). The parent plan states *what* must be true; this document states *which files change, in what order, and how each requirement is proven*.

Phases 2 to 8 deliberately have no document at this level of detail yet. They are written against code that does not exist, and a detailed plan for them would be stale before it was reached. Write the next one when the phase before it lands.

Rules for this phase:

- Every behaviour listed in section 6 has a test. If a requirement cannot be tested, either the seam is wrong or the requirement is a manual-verification item — say which, do not leave it unproven.
- No user-visible feature work. Phase 1 changes how data is written, not what the application does.
- Existing `places.json` and `settings.json` files keep working, untouched, with no schema change. Phase 1 reads the schema version; Phase 2 is the first to increment it.

## 1. Why this phase is first

`PlacesService.SaveToDisk()` (`src/QuickerPlaces/Services/PlacesService.cs:380`) ends in a bare `catch { }` with a comment describing best-effort persistence. Every mutation in the service — `TryAdd`, `TryRenameAlias`, `TryEditResource`, `ToggleFavourite`, `SetFavouriteOrder`, `Remove`, `CommitImport` — calls it and returns success regardless of what happened on disk. The user is told their change was stored; there is no signal anywhere that it was not.

Three more gaps sit alongside it:

| Gap | Where | Consequence today |
|---|---|---|
| Corrupt file is not preserved | `LoadFromDisk` (`PlacesService.cs:353`) returns an empty list and sets `LoadFailed` | The user is shown one warning, then the first mutation overwrites the damaged file through the normal save path |
| Damage and unavailability are indistinguishable | the same `catch` catches `JsonException`, `IOException`, and `UnauthorizedAccessException` alike | A file held open by antivirus or a sync client for two seconds is reported to the user exactly as if their data were destroyed |
| `schemaVersion` is written but never read | `PlacesStore.SchemaVersion` (`Models/PlacesStore.cs:15`) | A store written by a newer build loads silently and is saved back down-level, dropping fields it did not know about |
| Nothing prevents two writers | `App.OnStartup` (`App.xaml.cs`) | Two instances each hold a full in-memory list and write the whole file; the second to save wins and discards the first's changes |

Phases 2 and 3 add soft-deleted records and usage counters to this same file. Any of the four gaps above turns those features into ways to lose data rather than ways to recover it.

## 2. Scope

**In scope:** the storage seam and test project, transactional saves with a visible failure state, the schema-version gate, classification and recovery for a store that will not load, the diagnostic log, and single-instance enforcement.

**Out of scope, deliberately:** any change to validation rules, the export/import file format, the grid, the bubbles, or the dialogs beyond the failure banner and the recovery prompt. `AppSettings`/`SettingsService` keep their save-on-exit, best-effort behaviour (see 5.4 for why) apart from the small version-read change in 5.3.

## 3. Target file layout

| File | Status | Purpose |
|---|---|---|
| `src/QuickerPlaces/Services/IPlacesStorage.cs` | new | The seam: read, write, quarantine, and locate the store file |
| `src/QuickerPlaces/Services/FilePlacesStorage.cs` | new | Production implementation over `%AppData%\QuickerPlaces\QuickerPlaces\places.json` |
| `src/QuickerPlaces/Services/DiagnosticLog.cs` | new | Size-capped plain-text log with one rollover |
| `src/QuickerPlaces/Services/SingleInstance.cs` | new | Named mutex plus an activation signal for the second launch |
| `src/QuickerPlaces/Models/PersistenceResult.cs` | new | Save outcome returned alongside `ValidationResult` |
| `src/QuickerPlaces/Models/StoreLoadOutcome.cs` | new | `Ok` / `NotPresent` / `Damaged` / `Unreadable` / `WrittenByNewerVersion` |
| `src/QuickerPlaces/Services/PlacesService.cs` | changed | Takes `IPlacesStorage`; mutations return a persistence result; load runs the version gate |
| `src/QuickerPlaces/ViewModels/MainViewModel.cs` | changed | Owns the unsaved-changes banner state, Retry, Show Data Folder, Show Log |
| `src/QuickerPlaces/Views/RecoveryDialog.xaml` (+ `.cs`) | new | The startup recovery prompt: a message plus two or three custom-labelled choices |
| `src/QuickerPlaces/Views/MainWindow.xaml` (+ `.cs`) | changed | Adds the banner row; removes the one-shot load-failure notice |
| `src/QuickerPlaces/App.xaml.cs` | changed | Single-instance gate, log startup, recovery flow before the window shows |
| `src/QuickerPlaces.Tests/` | new project | xUnit tests for everything in this phase |
| `src/QuickerPlaces.sln` | changed | Adds the test project |

Naming, comment density, and the "explain the WPF-specific gotcha where one exists" style of the current code are the house style. Match them; do not introduce a DI container, a logging package, or an `IDialogService` — the parent plan's technical direction is to add abstractions only where they enable reliable persistence or focused tests, and one storage interface is the only one this phase needs.

## 4. Design decisions

These are settled here so they do not get re-litigated during implementation.

**D1 — A failed save keeps the user's change in memory and marks it unsaved.** The alternative in parent 4.2 is rolling back to the last persisted state. Rolling back throws away work the user just typed, and it leaves **Retry** with nothing to retry. Keeping the proposed state means the banner's Retry re-serializes the whole in-memory store and rewrites it, which is idempotent and needs no queue of pending operations. The requirement that a mutation is "reported as successful only after its new state is safely written" is met by the banner: the change is visible but the application never claims it is stored.

**D2 — Whole-store writes stay.** Every save serializes the entire store, as today. At this data scale (hundreds of records, a file measured in kilobytes) an append log or per-record write buys nothing and complicates the version gate and Recently Deleted purge in Phase 2.

**D3 — Unresolved recovery blocks writes rather than failing them.** If the store is corrupt or was written by a newer version and the user has not yet chosen a recovery option, `PlacesService` refuses mutations up front with a clear reason instead of accepting them and failing the save. This is the one case where a mutation is rejected rather than banner-flagged, and it is what stops a damaged file from being overwritten.

**D4 — The log lives in local application data, never roaming.** `places.json` roams by design; a machine's diagnostic log must not follow the user to another machine and must not bloat a roaming profile.

**D5 — The test project targets `net10.0-windows` and tests services and models only.** It references the WPF project directly. No test constructs a `Window`, so no test needs an STA thread or a message pump.

**D6 — A load failure is classified by its exception before the user sees anything.** `JsonException`, and a document that parses but is not a usable store, mean the content is damaged. `IOException` and `UnauthorizedAccessException` mean the file could not be opened, which says nothing about its contents. The two get different messages and different options, and only the first may lead to quarantine. Classifying at the `catch` is the only place the distinction is available — once the failure is flattened to a boolean, as `LoadFailed` does today, it cannot be recovered. Anything not in either category (an unexpected exception type) is treated as unreadable, because refusing to touch the file is the safe default.

## 5. Work items

### 5.1 Storage seam and test project (parent 4.1)

Create the interface first, then the test project, then move the existing I/O behind it with no behaviour change. This step should be provably inert: same reads, same writes, same silent catch, tests green.

```csharp
namespace QuickerPlaces.Services;

/// <summary>
/// Everything PlacesService needs from the filesystem. Exists so the
/// failure paths in Phase 1 — a write that fails, a file that can't be
/// parsed, a store from a newer version — can be exercised by a test
/// without a real disk fault.
/// </summary>
public interface IPlacesStorage
{
    /// <summary>Full path to the store file, for "Show Data Folder" and log messages.</summary>
    string StoreFilePath { get; }

    /// <summary>True if a store file is present. False means first run, not failure.</summary>
    bool Exists { get; }

    /// <summary>
    /// Reads the store file's full text. Throws if the file cannot be
    /// opened or read — that failure is unreadability, not damage, and
    /// PlacesService classifies it accordingly (D6). Parsing happens above
    /// this interface, so a damaged document never surfaces as a storage
    /// exception.
    /// </summary>
    string Read();

    /// <summary>
    /// Replaces the store file's contents durably: write to a uniquely
    /// named temporary file, flush it to disk, then replace the live file
    /// keeping a backup copy. Throws if any step fails.
    /// </summary>
    void Write(string contents);

    /// <summary>
    /// Renames the current store file out of the way as
    /// places.corrupt-yyyyMMdd-HHmmss.json and returns the new path.
    /// Never deletes and never overwrites an existing quarantine file.
    /// </summary>
    string Quarantine(DateTimeOffset timestamp);
}
```

`FilePlacesStorage` takes the folder and file name in its constructor and creates the folder, exactly as `PlacesService`'s constructor does today. `PlacesService` gains a constructor taking `IPlacesStorage` (plus a parameterless one that builds the production storage, so `App.xaml.cs` is unchanged at this step).

`Write` is the only non-obvious part:

1. Temporary file in the same directory, uniquely named: `places.json.{N:32-hex-guid}.tmp`. A fixed `.tmp` name, as used today, can collide with a stale file from a previous crash or with another process.
2. Write the bytes through a `FileStream`, then `Flush(flushToDisk: true)` before closing. An atomic rename guarantees the file is never observed half-written; it does not guarantee the new contents reached the platter before a power loss. This is the flush the parent plan's 4.2 asks for.
3. If the target exists, `File.Replace(temp, target, backupPath, ignoreMetadataErrors: true)` where `backupPath` is `places.bak.json`. If it does not exist, `File.Move(temp, target)`. `File.Replace` is what leaves a recoverable copy of the outgoing file; a plain overwriting move does not.
4. On any failure, delete the temporary file (best effort) and let the exception propagate. `PlacesService` is what decides what the user sees.

Test project: `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj`, xUnit, `net10.0-windows`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>disable</ImplicitUsings>` to match the main project. Two test doubles:

- `FakePlacesStorage` — in-memory contents, with `FailNextWrite`, `FailEveryWrite`, `ReadThrows`, `ContentsToReturn`, and a `WriteCount`. Used for almost everything.
- `TempDirectory` — an `IDisposable` that creates a uniquely named directory under the system temp path and deletes it on dispose. Used by the handful of tests that must exercise the real `FilePlacesStorage` (backup file creation, quarantine naming, temp-file cleanup). No test ever touches a real AppData path.

### 5.2 Transactional, observable saves (parent 4.2)

Add a small result type rather than throwing across the service boundary — the existing code's convention is that validation never throws to the caller, and persistence should match it.

```csharp
public readonly struct PersistenceResult
{
    public bool Saved { get; }
    /// <summary>Short sentence for the banner. Null when Saved is true.</summary>
    public string? UserMessage { get; }
}
```

`PlacesService` changes:

- A single private `PersistenceResult Persist()` replaces `SaveToDisk()`. It serializes the current in-memory store, calls `IPlacesStorage.Write`, and on failure logs the exception detail (5.5) and returns a message naming the file and the reason in plain words — never a raw exception string as the only text.
- `HasUnsavedChanges { get; }` becomes true on a failed persist and false after a successful one.
- `PersistenceResult RetrySave()` is public, so the banner can re-attempt the current in-memory state.
- Every mutation returns its persistence outcome as well as its validation outcome. For the `Try*` methods that already return `ValidationResult`, add an `out PersistenceResult persistence` parameter rather than changing the return type, so the call sites' validation handling stays as it reads today. `ToggleFavourite`, `SetFavouriteOrder`, `Remove`, and `CommitImport` return `PersistenceResult` directly.
- If recovery is unresolved (D3), every mutation returns `PersistenceResult` failure with the recovery message and makes no in-memory change at all.

`MainViewModel` changes:

- `bool HasUnsavedChanges` and `string? PersistenceMessage`, raised through the existing `ObservableObject.SetProperty`.
- `RetrySaveCommand`, `ShowDataFolderCommand`, `ShowLogCommand`.
- Every command that calls a mutation refreshes those two properties from the returned result. A subsequent successful save clears the banner; nothing else does. Attempting another command while the banner is up must not dismiss it — this falls out of only ever setting the state from a real persistence result, but it is the requirement most likely to be broken by a well-meaning `PersistenceMessage = null` on some unrelated path, so it gets its own test.

`MainWindow.xaml`: one new auto-height row above the favourite bubbles, visible only when `HasUnsavedChanges`, using the existing `Theme.xaml` accent and button styles (`Button.Primary` for Retry, the plain button style for the other two). It is a banner, not a dialog — non-modal, and it must not steal focus. Bind visibility through a converter rather than a code-behind toggle, consistent with how the grid's collapsed state already works.

### 5.3 Schema version gate (parent 4.3)

`PlacesService` gains `public const int CurrentSchemaVersion = 1;`

Loading becomes: read the text, parse it with `JsonDocument` far enough to read `schemaVersion`, then decide.

| Condition | Outcome | Effect |
|---|---|---|
| No file | `NotPresent` | Empty store, normal operation, first save creates the file |
| The file cannot be opened or read (`IOException`, `UnauthorizedAccessException`, or any unexpected exception) | `Unreadable` | File untouched, mutations blocked, retry offered. The read never gets as far as the version |
| Version == current | `Ok` | Deserialize normally |
| Version < current, known | `Ok` after migration | Migrate in memory; the migrated store is not written until a save succeeds through 5.2 |
| Version > current | `WrittenByNewerVersion` | File untouched, mutations blocked, upgrade advised |
| Version absent, non-numeric, or the document does not parse | `Damaged` | Quarantine offered, per 5.4 |

Two notes on the edges. The absent-version case is deliberately damaged rather than "assume version 1": every store this application has ever written includes the field, so a store without one is not a v1 store, it is a damaged or foreign file. And the classification happens at the `catch`, per D6 — reading the file and parsing it are separate `try` blocks, because a failure to open and a failure to parse are different answers.

`settings.json` gets the version read too, but keeps a different policy — an unusable settings file falls back to defaults silently, as it does today, because it holds only window bounds and a grid toggle. That asymmetry is intentional and belongs in the user guide; it is noted here so it is not "fixed" later by someone making the two services consistent for its own sake.

### 5.4 Recovery from a store that will not load (parent 4.4)

On `Damaged`, `Unreadable`, or `WrittenByNewerVersion`, `PlacesService` starts empty, sets `RecoveryState`, and refuses mutations (D3). `App.OnStartup` resolves the state before the main window is shown. Three outcomes, three sets of options — the differences are the point of this section, not incidental wording.

This needs a new `RecoveryDialog`, not `MessageForm`. `MessageFormButtons` offers only fixed OK/Cancel/Yes/No sets (`Models/MessageFormButtons.cs`), and "Start with an empty list" must never be a button a user reaches for by muscle memory while reading "Yes". `RecoveryDialog` takes a message and an ordered list of labelled choices, returns the chosen one, and follows `MessageForm`'s existing structure and `Theme.xaml` styling so it does not read as a foreign dialog. The destructive choice is never the default button, and closing the dialog by its title bar is equivalent to Exit — never to a choice that writes.

`App.OnStartup` shows it in a loop: the reveal-file and failed-retry choices return to the dialog, and only a resolved state or Exit leaves it.

**`Damaged` — the file was read but is not a usable store.**

> QuickerPlaces couldn't read your saved places. The file appears to be damaged. *(path)*

- **Show me the file** — reveals it in Explorer with the file selected, then asks again. Does not resolve the state.
- **Start with an empty list** — quarantines the damaged file to `places.corrupt-yyyyMMdd-HHmmss.json`, logs the quarantine path, and resolves the state. If the quarantine itself fails, the state stays unresolved and the failure is shown: never proceed to a writable state having failed to preserve the original.
- **Exit** — closes without writing anything.

**`Unreadable` — the file could not be opened.**

> QuickerPlaces couldn't open your saved places. Another program may be using the file, or it may not have permission to read it. Your data is most likely fine. *(path)*

- **Try again** — re-runs the load. On success the application proceeds normally with the real data and nothing is left behind; the failure and the recovery are both logged. On failure, ask again.
- **Show me the file** — reveals it in Explorer, then asks again.
- **Exit** — closes without writing anything.

There is deliberately **no empty-store option here, and no quarantine.** Nothing about this state establishes that the file is damaged, and a sync client or antivirus holding it for a few seconds is a more likely explanation than data loss. Renaming or replacing the file in response would destroy intact data. This is the single most important asymmetry in Phase 1: it is the only path where a wrong decision loses data the user still had.

**`WrittenByNewerVersion` — the file is fine, this build is too old.**

> These saved places were written by a newer version of QuickerPlaces. Update QuickerPlaces to open them. *(path)*

- **Exit** — the recommended action, listed first.
- **Show me the file** — reveals it, then asks again.

No empty-store option and no quarantine, for the same reason: the data is intact and a newer build can read it. (This settles what was open question 2 in the first draft of this document.) A user who genuinely wants to start over can move the file themselves, having been shown exactly where it is.

All three replace the current one-shot `MessageForm` notice driven by `PlacesLoadFailed` in `MainWindow.Window_Loaded` (`Views/MainWindow.xaml.cs:27`), which should be removed rather than left alongside them.

### 5.5 Diagnostic log (parent 4.5)

`DiagnosticLog` — static, `lock`-guarded, no third-party package.

- Path: `%LocalAppData%\QuickerPlaces\QuickerPlaces\logs\quickerplaces.log`, created on first write.
- Line format: `2026-09-01T14:30:00.123Z  WARN  message`, UTC, one line per entry, exception detail appended as indented continuation lines.
- Records: startup and version, load outcome including the schema version read, every save failure with exception type and message, quarantine paths, recovery choices, single-instance activations, and clean exit.
- Never records a place's alias or destination. A save failure logs the count of records and the store path, not their contents. The one exception is a quarantine path, which is a filename the user needs.
- Cap 256 KB with a single rollover to `quickerplaces.1.log`, so an unattended failure loop cannot fill a disk. Two files, bounded, no dated files to clean up.
- Logging must never throw. A failure to log is swallowed — this is the one remaining place where a silent catch is correct, and it should carry a comment saying so, since the rest of this phase exists to remove exactly that pattern.

### 5.6 Single instance (parent 4.6)

`SingleInstance` wraps a named `Mutex` (`Local\QuickerPlaces.SingleInstance`) and a named `EventWaitHandle` (`Local\QuickerPlaces.Activate`). `Local\` rather than `Global\` is correct: two Windows users on one machine have separate AppData and should each get an instance.

- `App.OnStartup` tries to acquire the mutex before constructing any service.
- Not acquired: signal the activation event, then `Shutdown()` immediately — before creating a `PlacesService`, so the second launch never reads or writes the store.
- Acquired: register a `ThreadPool.RegisterWaitForSingleObject` wait on the activation event; when signalled, marshal to the dispatcher, un-minimize (`WindowState = Normal`), and `Activate()` the main window.
- Release the mutex on exit.

Known limitation, accepted for this phase: Windows foreground-activation rules mean `Activate()` from a background process may flash the taskbar button instead of raising the window. The standard mitigation is a brief `Topmost` toggle; use it, and if it proves unreliable in manual testing, record that rather than reaching for P/Invoke. The requirement that matters here is that the second instance never writes, and that is guaranteed by the mutex alone.

## 6. Test plan

`src/QuickerPlaces.Tests`, xUnit, one class per area. "How it fails" is the fault injection each test uses.

| # | Test | Proves | How it fails |
|---|---|---|---|
| 1 | Add, then reload from the same storage | Round trip is intact | — |
| 2 | Add with `FailNextWrite` reports not saved | A save failure is never reported as success | Fake storage throws on `Write` |
| 3 | The failed add is still in memory and `HasUnsavedChanges` is true | D1 | as above |
| 4 | `RetrySave` after clearing the fault succeeds and clears the flag | Retry is meaningful | Fault cleared between calls |
| 5 | A second unrelated command does not clear the banner | Parent 4.2's "do not dismiss" rule | `FailEveryWrite` |
| 6 | Real `FilePlacesStorage` write leaves `places.bak.json` holding the previous contents | Previous valid file survives replacement | `TempDirectory` |
| 7 | No `.tmp` file remains after a failed write | Temp cleanup | Target path held open with `FileShare.None` |
| 8 | Two writes use different temp names | Unique temp naming | Intercepted in a storage subclass |
| 9 | Unparseable JSON yields `Damaged` and leaves the file byte-identical | Damaged file is not overwritten | Garbage contents |
| 10 | Mutations are refused while recovery is unresolved | D3 | as above |
| 11 | Quarantine renames the file and its bytes match the original | Recovery preserves data | `TempDirectory` |
| 12 | Quarantine never overwrites an existing quarantine file | Same-second double recovery | Pre-created target |
| 13 | `schemaVersion` above current yields `WrittenByNewerVersion`, file untouched | Parent 4.3 | `"schemaVersion": 99` |
| 14 | Missing or non-numeric `schemaVersion` is `Damaged`, not v1 | 5.3 | Field removed |
| 15 | A migrated store is not written to disk until a save succeeds | 5.3 | `FailEveryWrite`; assert `WriteCount == 0` after load |
| 15a | An `IOException` on read yields `Unreadable`, not `Damaged` | D6 | Fake storage throws `IOException` |
| 15b | An `UnauthorizedAccessException` on read yields `Unreadable` | D6 | Fake storage throws it |
| 15c | An unexpected exception type on read yields `Unreadable`, not `Damaged` | D6's safe default | Fake storage throws `InvalidOperationException` |
| 15d | An `Unreadable` outcome never quarantines and never writes | The asymmetry in 5.4 | Real `FilePlacesStorage` over a file held with `FileShare.None`; assert the file's bytes and name are unchanged and no quarantine file exists |
| 15e | A retry after the lock is released loads the original places intact | 5.4's retry path | Release the handle between the two load calls |
| 15f | A `WrittenByNewerVersion` outcome never quarantines and never writes | 5.4 | `"schemaVersion": 99` over `TempDirectory`; assert the directory contains exactly one file afterwards |
| 16 | Existing v1 file with today's exact shape loads with every field intact | No regression for current users | Fixture file committed to the test project |
| 17 | Alias and resource duplicate rules still hold | No regression from the refactor | — |
| 18 | Favourite renumbering stays dense across remove and toggle | No regression from the refactor | — |
| 19 | Import commit persists once, not once per record | `CommitImport` batching | `WriteCount == 1` |
| 20 | A failed import commit reports not saved and keeps the candidates in memory | D1 across the batch path | `FailNextWrite` |
| 21 | The log rolls over at its cap instead of growing without limit | 5.5 | Write past 256 KB into a temp directory |
| 22 | The log never contains a written alias or resource | 5.5 privacy rule | Save failure with a known alias; assert absence |

Fixture for #16: a copy of a real `places.json` with favourites, a non-favourite, and a URL, committed as `src/QuickerPlaces.Tests/Fixtures/places.v1.json`. This is the file that catches an accidental serialization change; it should be treated as frozen once written.

Manual verification, on Windows, recorded in the phase's closing commit message:

- Deny write permission on `places.json`, add a place, confirm the banner appears, the place stays on screen, and Retry succeeds after permission is restored.
- Corrupt `places.json` by hand, launch, walk all three recovery options, confirm the quarantine file appears and the original bytes survive.
- Hold `places.json` open from another program, launch, confirm the message says the file could not be opened rather than that it is damaged, that no empty-store option is offered, and that Try Again recovers the real data once the handle is released.
- Set `schemaVersion` to 99, launch, confirm the newer-version message and that the file is unchanged afterwards.
- Launch a second instance while the first is minimized, and again while it is behind another window.
- Pull a USB drive mid-session with the store on it, if a removable path is testable — otherwise note it as untested.
- Confirm the banner does not steal focus and the window still restores correctly on a high-DPI multi-monitor setup.

## 7. Order of work

Each step should build and have green tests before the next begins. This is also the intended commit sequence.

1. **Test project and seam, no behaviour change.** `IPlacesStorage`, `FilePlacesStorage`, `PlacesService` constructor overload, test doubles, tests 1, 16, 17, 18. The silent catch is still there at the end of this commit, deliberately.
2. **Diagnostic log.** `DiagnosticLog`, wired to startup and exit only. Tests 21, 22. Doing this before the save rewrite means the save rewrite has somewhere to report to.
3. **Transactional saves.** `PersistenceResult`, `Persist`, `HasUnsavedChanges`, `RetrySave`, mutation signatures. Tests 2 to 8, 19, 20. No UI yet.
4. **Banner UI.** `MainViewModel` state and commands, `MainWindow.xaml` row, Retry / Show Data Folder / Show Log.
5. **Version gate and recovery.** `StoreLoadOutcome`, the failure classification (D6), the gate, quarantine, and the three startup recovery flows; remove the old one-shot notice. Tests 9 to 15f. Write 15a to 15f before the recovery UI, not after: they are what stops the `Unreadable` path from quietly acquiring an empty-store button later.
6. **Single instance.** `SingleInstance`, `App.OnStartup` gate and activation. Manual verification, and the phase's manual checklist recorded in the commit message.

Commit messages follow the repository's existing style: what changed and why, with the WPF- or platform-specific reason spelled out where one drove the decision.

## 8. Phase 1 definition of done

- No mutation can report success while its data is unwritten; a failed save is visible, persistent, and retryable.
- A damaged store cannot be overwritten, and choosing to start fresh preserves the original bytes under a timestamped name.
- A store that could not be opened, or that came from a newer version, is never renamed, written to, or replaced with an empty one, and its message says so.
- The schema version is read on every load, and an unknown future version is refused rather than silently downgraded.
- Only one instance per user per machine can write the store.
- Persistence failures leave a bounded diagnostic record that contains no place aliases or destinations.
- All 28 tests pass, and the manual checklist in section 6 has been walked on Windows.
- `BUILD_SUMMARY.md` records the phase, and the user guide covers the banner, the recovery prompt, and the log's location.

## 9. Open questions

These do not block starting on section 7 step 1, but they should be settled before step 5.

1. **Backup file visibility.** `places.bak.json` sits next to `places.json` in the user's data folder. Leave it visible, or move both backup and quarantine files into a `backups\` subfolder? A subfolder is tidier; a sibling file is easier to talk a user through recovering over the phone.
2. **Log retention.** 256 KB with one rollover is a guess. If the log is meant to survive long enough to diagnose an intermittent failure that happens weekly, it wants to be larger.

---
title: QuickerPlaces — Phase 3 Detailed Implementation Plan
status: implemented 2026-09-25 (steps 1–7, e250fbb to 3e77208, plus 479388b, on claude/phase-3-usage-tracking, not yet merged) — all 311 tests pass; the UI is compile-verified only, never run; the section 8 checklist has not been walked. Where the build departs from this plan: BUILD_SUMMARY.md, Phase 3
created: 2026-09-25
parent: ai/260901_Professional Improvements Plan.md
covers: sections 4.11 to 4.14 (Usage tracking and sorting)
builds_on: ai/260925_Phase 2 Handoff.md §4–§6; ai/260925_Phase 2 Detailed Plan.md D7–D22; ai/BUILD_SUMMARY.md D1–D6
last_revised: 2026-09-25 — status updated after implementation (step 8); the plan body is unchanged
---

# Phase 3 — Usage tracking and sorting

## 0. How to use this document

This is the file-level and signature-level plan for Phase 3 of the [Professional Improvements Plan](260901_Professional%20Improvements%20Plan.md) (§4.11–4.14). The roadmap states *what* must be true; this document states *which files change, in what order, and how each requirement is proven*. It was written against `main` at `1c03e59` (Phase 2 merged, 238 tests passing); line numbers cite that code and will drift once work starts.

This phase was planned with the later phases in view, not only its own four sections. Phase 3 settles what a recorded open *is*, and three later phases build on that answer: Phase 4 adds files (a second kind of existence check), Phase 5 adds opening policies (a launch that can be prompted, cancelled, or sent to a chosen application, and per-file choices "keyed by place identifier"), and Phase 9 records Explorer activity on its own terms. Decisions D23, D24, D27 and D29 exist because of those phases, and each says which one.

Rules for this phase:

- Every behaviour in section 7 has a test on the storage seam or the new shell seam, or is a named manual-verification item.
- Phase 1's and Phase 2's settled constraints are built on, not revisited: D1 (a failed save keeps the change), D2 (whole-store writes), D3 (unresolved recovery refuses every mutation), D5 (no test constructs a `Window`), D6 (classify at the `catch`), D7/D8 (one list and a flag; `Places` is a fresh active snapshot), D11 (migrate the JSON before binding; loading never writes), D12 (one injected clock), D14 (every save purges first). The [Phase 2 hand-off §5](260925_Phase%202%20Handoff.md) lists them.
- `places.v1.json` and `places.v2.json` stay byte-identical. A `places.v3.json` is added beside them and frozen once committed.

Decisions are numbered **D23 onwards**, continuing Phase 2's D7–D22.

Tooling, from `src/`:

- `dotnet test QuickerPlaces.Tests/QuickerPlaces.Tests.csproj` — the suite (238 tests today, all passing).
- On Windows, `dotnet build QuickerPlaces.sln` builds the app; on Linux, `dotnet build QuickerPlaces/QuickerPlaces.csproj -p:EnableWindowsTargeting=true` compile-checks it.
- `TZ=<zone> dotnet test …` (Linux) runs the suite under another zone. Do so after steps 2, 3 and 7 of section 10.

## 1. Where Phase 3 starts from

| Area | Today | Where | Phase 3 |
|---|---|---|---|
| Open | `MainViewModel.Open`: `Directory.Exists` for a folder, then `Process.Start(… UseShellExecute = true)`; failures shown with `MessageForm`. Nothing is recorded | `MainViewModel.cs:286` | Delegates to `PlaceLauncher` (D23), which records the open through `PlacesService.RecordOpen` (D24) |
| Open paths | Grid double-click, grid Enter, context menu, bubble click, Ctrl+1–9 (`OpenFavouriteAt`), search box Enter (top row of the *sorted, filtered* grid) | `MainWindow.xaml.cs:255`, `:277`, `:362`; `MainViewModel.cs:631` | All already reach `OpenCommand`; unchanged |
| Place identity | None. Records are found by reference; alias and destination are both editable | `Place.cs` | A stable `Guid Id` (D27) |
| Schema | 2: UTC `dateAdded`, `deletedAt` | `PlacesService.cs:114`, `PlacesStoreMigration.cs` | 3: `id`, `lastOpenedAt`, `openCount`; v2 → v3 migration chained after v1 → v2 (D28) |
| Grid columns | Alias, Type, Path / URL, Favourite, Date Added (`{0:d}`, which ignores the locale — Phase 2 hand-off §6 q5) | `MainWindow.xaml:327–355` | Alias, Type, Path / URL, Favourite, **Last Opened**, **Opens** (D31) |
| Sorting | The DataGrid's built-in header sorting on `PlacesView`; not remembered; `Places` stays in stored order (Phase 2 hand-off §5) | `MainViewModel.cs:66` | `PlaceSort` applied as the view's `CustomSort` (D29), remembered in `settings.json` (D30) |
| Settings | `AppSettings` v2: window bounds, grid expanded, global hotkey. Saved on close and by the Settings dialog | `AppSettings.cs`, `App.xaml.cs:73`, `MainViewModel.PersistToSettings` | v3: adds the sort (D30) |
| Import | Keeps alias, type, destination; stamps `DateAdded` with now; drops favourite state | `PlacesService.CommitImport` `:839` | Keeps `DateAdded`, `LastOpenedAt`, `OpenCount` and, when free, `Id` (D33). Favourite state still dropped |

One finding from reading the code that shapes the design:

**`MainViewModel` cannot be tested, so neither can today's open.** It references `System.Windows.*` and is not linked into the test project (D5). The roadmap's test plan lists "failed opens do not update usage" and "a destination that no longer exists fails its pre-launch check and does not update usage" as automated tests, and the only way to meet that is to move the decision out of the view model (D23).

## 2. Scope

**In scope:** schema v3 (`Id`, `LastOpenedAt`, `OpenCount`) and its migration; recording an accepted open; the launch seam; the Last Opened and Opens columns; removing Date Added from the grid; sort behaviour and its persistence; export and import of the new fields; the user guide.

**Out of scope, deliberately:**

- A per-open history, usage graphs, or any statistic beyond a count and a last time (D25).
- Column visibility. Date Added leaves the grid and stays stored (decided with the user, 2026-09-25).
- Keyboard access to column headers (Phase 7, §4.25).
- File places and a file existence check (Phase 4). The seam leaves room for both (D23).
- Importing favourite state (still Phase 4's §4.18 question; Phase 2 plan §12 q4).
- Anything in `activity.json` (Phase 9). Phase 3 neither reads nor writes it (D24).
- Setting `FrameworkElement.Language`. D31 makes it unnecessary for the grid; the question stays open for other dialogs (§12).

## 3. Target file layout

| File | Status | Purpose |
|---|---|---|
| `src/QuickerPlaces/Models/Place.cs` | changed | `Guid Id`, `DateTimeOffset? LastOpenedAt`, `int OpenCount` |
| `src/QuickerPlaces/Models/PlacesStore.cs` | changed | `SchemaVersion` initialiser to 3 |
| `src/QuickerPlaces/Models/AppSettings.cs` | changed | v3: `PlacesSortKey`, `PlacesSortDirection` (D30) |
| `src/QuickerPlaces/Services/PlacesStoreMigration.cs` | changed | Adds `MigrateV2ToV3` and its report (D28) |
| `src/QuickerPlaces/Services/PlacesService.cs` | changed | Version 3, the load chain, normalisation, `Id` on add, `RecordOpen`, import/export rules |
| `src/QuickerPlaces/Services/IShell.cs` | new, UI-free | The launch seam: `DirectoryExists`, `Open` (D23) |
| `src/QuickerPlaces/Services/WindowsShell.cs` | new, UI-free | Production `IShell`: `Directory.Exists`, `Process.Start` with `UseShellExecute` |
| `src/QuickerPlaces/Services/PlaceLauncher.cs` | new, UI-free | The one gateway for every launch; returns an `OpenOutcome` (D23, D24) |
| `src/QuickerPlaces/Services/PlaceSort.cs` | new, UI-free | `PlaceSortKey`, `PlaceSort`: comparer, header-click cycle, parse and format for settings (D29) |
| `src/QuickerPlaces/ViewModels/PlaceViewModel.cs` | changed | `LastOpenedAt`, `LastOpenedText`, `OpenCount`; `FormatLastOpened` (D31). `DateAdded` stays (used by no column, kept for Phase 5's tab sorts and a future column) |
| `src/QuickerPlaces/ViewModels/MainViewModel.cs` | changed | `Open` through `PlaceLauncher`; `CurrentSort`, `SortBy`; re-sort after an open (D32); `PersistToSettings` writes the sort |
| `src/QuickerPlaces/Views/MainWindow.xaml` (+ `.cs`) | changed | Columns (D31); `Sorting` handler and header arrows (D29) |
| `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj` | changed | Links `IShell.cs`, `PlaceLauncher.cs`, `PlaceSort.cs`; copies `places.v3.json` |
| `src/QuickerPlaces.Tests/Fakes/FakeShell.cs` | new | Scriptable `IShell`: which directories exist, whether `Open` throws, what was opened |
| `src/QuickerPlaces.Tests/Fixtures/places.v3.json` | new, frozen once written | The v3 shape (section 7) |
| `src/QuickerPlaces.Tests/*` | new and changed | See section 7 |
| `USERGUIDE.md` | changed | See section 9 |

`App.xaml.cs` needs no change: `MainViewModel` constructs its own `PlaceLauncher` from the `PlacesService` it is given and a `WindowsShell`, and the close handler already calls `PersistWindowState` → `PersistToSettings` before saving settings.

No DI container, no new package, no `IDialogService`, as in Phases 1 and 2.

## 4. Design decisions

Settled here so they are not re-litigated during implementation.

**D23 — Every launch goes through one UI-free gateway, `PlaceLauncher`, over an `IShell` seam.** `PlaceLauncher.Open(Place)` runs the pre-launch check, asks the shell to open the destination, and records the open only if both succeed. It returns an `OpenOutcome`; the view model only turns that into messages. Two alternatives were rejected:

- *Only a `PlacesService.RecordOpen`, called from `MainViewModel.Open` after `Process.Start`.* That is less code today, but the rule "only after Windows accepts the launch" would live in the one class the tests cannot reach, and Phase 5 is about to make it much more complicated.
- *Launching from `PlacesService`.* It would mix shell side effects into the persistence class, which is already 1,300 lines and whose tests exist to exercise storage.

The seam is shaped for the phases that extend it. Phase 4 adds `FileExists` to `IShell` and a `File` branch to the pre-check. Phase 5 adds a resolution step before the launch (policy, per-file choice, **Ask each time**), a `Cancelled` status, and an `OpenWith(executable, path)` shell call. All of that lands inside `PlaceLauncher`, and nothing that calls it changes. `PlaceLauncher` stays the only caller of `RecordOpen` (D24).

**D24 — A recorded open is defined once, in `PlaceLauncher`, and nowhere else writes usage.** An open is recorded when both of these hold:

1. The pre-launch check passed. For a folder, that is `IShell.DirectoryExists`; a URL has none (roadmap §4.12).
2. `IShell.Open` returned without throwing.

That is the roadmap's definition, and it proves only that Windows accepted the request, not that a handler opened anything. The user guide says so. `PlacesService.RecordOpen` is the only method that changes `LastOpenedAt` or `OpenCount`, and `PlaceLauncher` is its only caller. Statistics count launches QuickerPlaces made, and nothing else.

Phase 9 depends on this definition (its plan §0), and this decision answers that plan's open question 2. Phase 3's statistics live on the `Place`, in roaming `places.json`, and never read or write Phase 9's local `activity.json`. Launching a folder place opens an Explorer window, which Phase 9's tracker observes on its own terms, so neither store needs to feed the other. The two numbers mean different things ("launched by QuickerPlaces" versus "Explorer was on this folder while you were active"), and they are never added together.

**D25 — Usage is a count and a last time, nothing more.** No list of open timestamps, no per-day tallies. Roadmap §2 makes "complex usage analytics" a non-goal, and the one bounded exception is Phase 9, which keeps its time series machine-local and opt-in. A history array inside roaming `places.json` would grow without limit, roam, and be exported, which is exactly what that exception was designed to avoid. On a roaming profile signed in on two machines at once, the last writer's count wins; that is the roadmap §2 boundary, unchanged.

**D26 — Recording an open is an ordinary mutation, saved at once, and it never gets in the way of the launch.** `RecordOpen` stamps `LastOpenedAt = _time.GetUtcNow()` (D12), increments `OpenCount`, and calls `Persist()` (roadmap §4.12, "persist immediately"). The launch has already happened by then, so:

- A failed save is a forward change (D1): the usage stays in memory, the banner offers Retry, and the outcome still reports the launch as successful.
- While recovery is unresolved (D3), the launch still happens and is not recorded. The recovery banner already explains why nothing can be saved, and refusing to open a place because its *statistics* can't be stored would make the launcher useless during recovery.
- A deleted record, or one no longer in the store, is ignored with no write. The UI cannot reach one (Phase 2 hand-off §5).
- `OpenCount` saturates at `int.MaxValue` instead of wrapping to a negative.
- Nothing about favourites changes (roadmap §4.12).
- Like every save, it purges expired Recently Deleted records first (D14).

Nothing is logged for a recorded open: a log line per launch would be noise, and would be a record of the user's activity in a file they did not ask for. A failed launch logs its exception type and nothing else (never the alias or destination, per the log's privacy rule), so "why won't it open" can still be diagnosed.

**D27 — Schema v3 also gives every place a stable `Id`, for Phase 5.** Roadmap §4.19 keys Phase 5's per-file remembered applications, in machine-local `applications.local.json`, "by place identifier". There is no such identifier today, and neither natural key works: the alias can be renamed and the destination can be edited, and either change would silently detach a remembered Revit release from its model, which is the failure §4.21 exists to prevent. Every schema version costs a migration, a frozen fixture, an import-gate row, and a refusal by every older build, so the field is added in the bump Phase 3 has to make anyway, not in a second one a release later.

- `Guid Id`, written as `"id"`, first in each record. It is assigned by the v2 → v3 migration, by `TryAdd`, and by import when the incoming one is unusable (D33). It never changes after that: not on rename, edit, remove, restore, or a restore under a new alias.
- On load, an `Id` that is `Guid.Empty`, or repeats one already seen in the file (a hand-copied record), is replaced with a fresh one, and the log counts how many. The first holder keeps it.
- Nothing in Phase 3 reads it except the uniqueness rules above. It is not shown in the UI.

**D28 — v2 → v3 is a JSON-level migration, chained after v1 → v2, and loading still never writes.** `PlacesStoreMigration.MigrateV2ToV3(JsonObject root)` gives each record a fresh `id`, removes any `lastOpenedAt` or `openCount` it finds, and sets `schemaVersion` to 3. As with v1's stray `deletedAt` (Phase 2 hand-off §4 d4), v2 never gave those properties a meaning, so a hand-edited one must not start meaning something on upgrade, and an `id` in a v2 file is replaced for the same reason. Missing usage binds to the defaults (`null`, `0`), which is roadmap §4.11's "null/zero defaults".

| `schemaVersion` | Outcome |
|---|---|
| Missing, non-numeric, below 1 | `Damaged`, unchanged |
| 1 | v1 → v2 → v3 in memory; `Ok` |
| 2 | v2 → v3 in memory; `Ok` |
| 3 | Bind directly; `Ok` |
| Greater than 3 | `WrittenByNewerVersion`, unchanged |

Each migration step logs its own line, with counts only. The migrated store reaches disk with the next successful save, and that save's `places.bak.json` is the v2 file. Every build from Phase 1 on refuses a v3 file without touching it (Phase 2 hand-off §4 d9 explains why "from Phase 1 on").

On disk, `lastOpenedAt` is written only when it has a value (`WhenWritingNull`, as `deletedAt` is), so a never-opened record carries no date it doesn't have. `openCount` and `id` are always written. After binding, `LastOpenedAt` is normalised to UTC and a negative `OpenCount` becomes 0, both counted in the load log line, so a hand-edited value never survives into a write (the Phase 2 precedent for `DateAdded`).

**D29 — Sorting is a UI-free `PlaceSort` over the model, applied as the view's `CustomSort`; its keys are the ones Phase 5's tabs will use.** `PlaceSortKey` is `Alias`, `Type`, `Destination`, `Favourite`, `LastOpened`, `Opens`, `DateAdded`. That covers every grid column, and it is a superset of roadmap §4.19's tab sort modes: Recent = `LastOpened` descending, Frequent = `Opens` descending, A–Z = `Alias`, Path = `Destination`, and Date Added. Phase 5 reuses this type rather than inventing a second one. The key is `Destination` rather than `Resource` so that Phase 4's relabel doesn't leave a stale name in `settings.json`.

- **Comparison.** Text keys compare with `StringComparer.CurrentCultureIgnoreCase`. `Type` compares the label, so Phase 4's "File" sorts between "Folder" and "URL" by name. `Favourite` puts favourites first when descending. A `LastOpenedAt` of null is older than any date, so never-opened places are always at the old end: last when newest-first, first when oldest-first (roadmap §4.13, "sort consistently").
- **Ties.** Every key except `Alias` breaks ties by alias, then by destination (ordinal), so the order is total and does not shuffle on each refresh. Without this, sorting by Opens on a fresh upgrade (every count 0) would leave the order to the sort algorithm.
- **Applied to the view, never to the collection.** `MainViewModel` sets `((ListCollectionView)PlacesView).CustomSort` to an adapter over `PlaceSort.Comparer`, or to `null` for stored order. `Places` stays in stored order, which `InsertRestored` depends on (Phase 2 hand-off §5). `CustomSort` rather than `SortDescriptions` because it is faster (no reflection per comparison), and because only a comparer can express "null is oldest" and the tie-breaks.
- **Header clicks.** `MainWindow` handles `DataGrid.Sorting`, sets `e.Handled = true`, and calls `MainViewModel.SortBy(key)`. The key is the column's `SortMemberPath`, which is set to the key's name (`SortMemberPath="LastOpened"`); nothing else reads it, since the default sort is bypassed. The cycle is `PlaceSort.Next`:
  - a column not currently sorted → its *first direction*: descending for `LastOpened`, `Opens`, `Favourite` and `DateAdded` (the useful end first), ascending for the rest;
  - the same column again → the other direction;
  - a third time → no sort, which is stored order.

  The third state exists because the default is stored order (decided with the user, 2026-09-25), and without it there would be no way back to that order short of editing `settings.json`.
- **Arrows.** After every change, and once at startup, `MainWindow` sets each column's `SortDirection` from `CurrentSort`: the sorted column shows its arrow, and the rest show none.

**D30 — The sort is remembered in `settings.json` as two strings, read leniently.** `AppSettings` gains `string? PlacesSortKey` and `string? PlacesSortDirection` (`"ascending"`/`"descending"`), and `CurrentSchemaVersion` becomes 3. They are strings rather than enums because `SettingsService` deserialises enums strictly: an unknown or misspelt value would throw, and its `catch` would then reset *every* setting, hotkey included. `PlaceSort.Parse(key, direction)` is case-insensitive and returns null (stored order) for anything it doesn't recognise, so a bad value costs only the sort. Both null means stored order, which is also what a v2 settings file yields: no settings migration is needed.

The sort is machine-local presentation state, so it belongs with window chrome (roadmap §3), not in roaming `places.json`. It is copied into `AppSettings` by `PersistToSettings` and saved on close with the rest; the Settings dialog's immediate save also carries it, since it calls `PersistWindowState` first. A crash loses a sort change made in that session, as it already loses window bounds.

**D31 — Last Opened replaces Date Added; formatting moves into the view model.**

| Column | Binding | Sorts by | Width |
|---|---|---|---|
| Alias | template, as today | `Alias` | `*` |
| Type | `TypeLabel` | `Type` | 90 |
| Path / URL | `Resource` | `Destination` | `2*` |
| Favourite | `IsFavourite`, `OneWay` | `Favourite` | 90 |
| Last Opened | `LastOpenedText` | `LastOpened` | 150 |
| Opens | `OpenCount`, right-aligned | `Opens` | 70 |

`LastOpenedText` is `PlaceViewModel.FormatLastOpened(LastOpenedAt, TimeZoneInfo.Local, CultureInfo.CurrentCulture)`: "—" (an em dash) for never, otherwise local time in the culture's short date and short time (`"g"`) — date *and* time, because a launcher is used many times a day and a date alone would make every place opened today look alike (decided with the user, 2026-09-25). A static formatter with the zone and culture as parameters means the tests can pin both (Phase 2 hand-off §5: no test reads the machine's zone).

Formatting with `CultureInfo.CurrentCulture` in code, as the Recently Deleted dialog already does, rather than a XAML `StringFormat` avoids the en-US-only formatting of Phase 2 hand-off §6 q5, and it respects the user's customised regional formats, which `FrameworkElement.Language` would not. Removing Date Added removes the grid's only other date, so the grid no longer has the problem. `Place.DateAdded` stays stored and exported, and `PlaceViewModel.DateAdded` stays for `PlaceSortKey.DateAdded`.

The fixed columns grow from 290 px to 400 px. At the 700 px `MinWidth` that leaves the two star columns roughly 250 px between them. That is the manual check in section 8; if it's too tight, Last Opened narrows before anything else does.

**D32 — A recorded open re-sorts the grid immediately when the sort depends on usage.** After a launch that was recorded, `MainViewModel` calls `place.Refresh()` so the row shows its new values, and, if `CurrentSort.Key` is `LastOpened` or `Opens`, it also calls `PlacesView.Refresh()` so the row moves to its new position at once (decided with the user, 2026-09-25). No other sort can change because of an open, so no other sort pays for a view reset. The DataGrid keeps the selected item across the reset; section 8 checks that keyboard focus stays on it, and if it does not, `MainWindow` re-focuses the selected row with its existing `FocusGridRow`. The search box's Enter already opens `PlacesGrid.Items[0]`, the top of the *sorted* view, so under Last Opened newest-first, typing a few letters and pressing Enter opens the most recently used match. That is a free consequence of this design, and the user guide says so.

**D33 — Import keeps `DateAdded`, `LastOpenedAt`, `OpenCount` and, when it is free, `Id`.** Roadmap §4.18 says "for this release, preserve exported metadata to keep export/import round trips lossless". That section belongs to Phase 4, but Phase 3 is what puts usage into exports, so the question arrives now (Phase 2 hand-off §6 q4). `CommitImport` still builds a fresh `Place` rather than admitting the candidate instance, and copies these fields from the candidate:

- `DateAdded`, `LastOpenedAt` (both as UTC) and `OpenCount` (clamped to 0 or more) are copied as they are. A v1 or v2 export yields the migration defaults, and those are copied the same way.
- `Id` is kept if it is non-empty and not already held by *any* record in the store, active or deleted, including one added earlier in the same batch. Otherwise the place gets a fresh `Id`. Restoring your own backup onto a machine therefore reconnects it to that machine's Phase 5 per-file choices, while importing someone else's export can never attach one of their places to one of yours.
- `IsFavourite` and `FavouriteOrder` are still not imported, and `DeletedAt` records are still never offered (D17). Both are unchanged.

Export writes `schemaVersion: 3` and all three new fields. It is still filtered to active records (D16). Import accepts versions 1 to 3, runs whatever migrations the version needs (the same chain as the store), and refuses anything newer with the existing message.

## 5. Work items

### 5.1 Schema v3 and its migration (roadmap §4.11; D27, D28)

`Place`, with `Id` declared first so it serialises first:

```csharp
/// <summary>
/// Stable identity for this place (D27). Assigned once — by the v2 → v3 migration,
/// TryAdd, or import — and never changed by rename, edit, remove or restore. Phase 5
/// keys per-file application choices by it; nothing in Phase 3 displays it.
/// </summary>
public Guid Id { get; set; }

/// <summary>
/// When QuickerPlaces last launched this place successfully (D24), in UTC; null if never.
/// Written only by PlacesService.RecordOpen. Not written to JSON while null.
/// </summary>
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public DateTimeOffset? LastOpenedAt { get; set; }

/// <summary>How many times QuickerPlaces has launched this place successfully (D24). Saturates at int.MaxValue.</summary>
public int OpenCount { get; set; }
```

`PlacesStoreMigration`:

```csharp
/// <summary>
/// Rewrites a schemaVersion 2 document in place as schemaVersion 3 (D28): a fresh id per
/// record, any stray lastOpenedAt/openCount removed. Throws JsonException for a shape it
/// cannot migrate, as MigrateV1ToV2 does, so D6's catch classifies the store Damaged.
/// </summary>
public static MigrationV3Report MigrateV2ToV3(JsonObject root);

/// <summary>What the v2 → v3 migration did, for the log line (counts only).</summary>
public readonly record struct MigrationV3Report(int Records, int StrayFieldsRemoved);
```

It follows `MigrateV1ToV2`'s structure exactly: null `places` is left for the loader's own check, a null entry is skipped, a non-object entry or a non-array `places` throws `JsonException`.

`PlacesService`:

- `CurrentSchemaVersion = 3`; `PlacesStore.SchemaVersion`'s initialiser to 3.
- `LoadFromDisk` gains the D28 table: version 1 runs `MigrateV1ToV2` then `MigrateV2ToV3`; version 2 runs `MigrateV2ToV3`. Each step keeps or gains its own log line. The two `try` blocks (read, then parse) are unchanged (D6).
- The post-bind normalisation loop (today `DateAdded`, `DeletedAt` to UTC) adds `LastOpenedAt` to UTC, `OpenCount` clamped to 0 or more, and the `Id` repair (D27). The "Loaded …" log line gains "; normalised {n} usage value(s) and {m} id(s)" when either is non-zero.
- `TryAdd` sets `Id = Guid.NewGuid()`.

### 5.2 Recording an open (roadmap §4.12; D24, D26)

`PlacesService`:

```csharp
/// <summary>
/// Records one launch that Windows accepted (D24): LastOpenedAt from the clock, OpenCount
/// plus one (saturating), then saved at once. Called only by PlaceLauncher. Does nothing,
/// and writes nothing, for a place in Recently Deleted or no longer in the store. Refused
/// while recovery is unresolved (D3); the launch has already happened either way (D26).
/// </summary>
public PersistenceResult RecordOpen(Place place)
```

Order: the D3 guard (return the blocked result), then `Ok()` without a write if the place is not in `_places` or has `DeletedAt`, then stamp, increment, and `return Persist();`.

### 5.3 The launch seam (D23, D24)

```csharp
/// <summary>The shell operations a launch needs, behind a seam so PlaceLauncher can be tested (D23).</summary>
public interface IShell
{
    bool DirectoryExists(string path);

    /// <summary>Asks Windows to open <paramref name="target"/> with its default handler. Throws if Windows refuses it.</summary>
    void Open(string target);
}

public sealed class WindowsShell : IShell
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    // UseShellExecute lets Windows pick the handler: Explorer for a folder, the default
    // browser for a URL. The returned Process is null for both, and is not needed.
    public void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}

public enum OpenStatus
{
    /// <summary>Windows accepted the launch (D24). Persistence says whether the usage was saved.</summary>
    Launched,
    /// <summary>The pre-launch check failed: the folder no longer exists. Nothing was launched or recorded.</summary>
    Missing,
    /// <summary>Windows refused the launch. Nothing was recorded.</summary>
    Failed
}

/// <param name="ErrorMessage">For Failed: the shell's message, for the user. Null otherwise.</param>
/// <param name="Persistence">For Launched: RecordOpen's result. PersistenceResult.Ok() otherwise.</param>
public sealed record OpenOutcome(OpenStatus Status, string? ErrorMessage, PersistenceResult Persistence);

/// <summary>The one gateway for every launch (D23): pre-check, open, then record (D24).</summary>
public sealed class PlaceLauncher
{
    public PlaceLauncher(PlacesService placesService, IShell shell);

    public OpenOutcome Open(Place place);
}
```

`Open` checks `DirectoryExists` for a `Folder` and returns `Missing` without calling the shell if it fails. It then calls `IShell.Open` inside a `try`: any exception becomes `Failed` with `ex.Message`, plus a `DiagnosticLog.Warn` naming the place type and the exception type only (D26). On success it returns `Launched` with `RecordOpen`'s result.

`MainViewModel`:

- A `PlaceLauncher _launcher` field, built in the constructor as `new PlaceLauncher(placesService, new WindowsShell())`.
- `Open(PlaceViewModel?)` becomes: call `_launcher.Open(place.Model)`, then act on the status. `Missing` shows today's "This folder no longer exists" message, and `Failed` today's "Couldn't open …" message, with the same icons. For `Launched`, call `RefreshPersistenceState(outcome.Persistence)`, `place.Refresh()`, and the D32 re-sort.

### 5.4 Sorting and its persistence (roadmap §4.13–4.14; D29, D30)

```csharp
public enum PlaceSortKey { Alias, Type, Destination, Favourite, LastOpened, Opens, DateAdded }

/// <summary>An active sort of the places grid (D29). "No sort" (stored order) is a null PlaceSort?.</summary>
public readonly record struct PlaceSort(PlaceSortKey Key, ListSortDirection Direction)
{
    /// <summary>Descending for LastOpened, Opens, Favourite and DateAdded; ascending for the rest.</summary>
    public static ListSortDirection FirstDirection(PlaceSortKey key);

    /// <summary>The header-click cycle: another column → its first direction; same column → the other direction; a third time → null.</summary>
    public static PlaceSort? Next(PlaceSort? current, PlaceSortKey clicked);

    /// <summary>Compares places by Key in Direction, then by alias, then by destination (ordinal). Null LastOpenedAt is oldest.</summary>
    public IComparer<Place> Comparer { get; }

    /// <summary>Reads the two settings.json strings case-insensitively; null for missing or unrecognised (D30).</summary>
    public static PlaceSort? Parse(string? key, string? direction);

    /// <summary>The two strings Parse reads: the key's name and "ascending"/"descending".</summary>
    public (string Key, string Direction) Format();
}
```

`ListSortDirection` is in `System.ComponentModel`, not WPF, so the file links into the tests.

`AppSettings`: `CurrentSchemaVersion = 3`, with the version comment extended ("3: added PlacesSortKey and PlacesSortDirection; a version-2 file lacks them and loads unsorted"). Adds `public string? PlacesSortKey { get; set; }` and `public string? PlacesSortDirection { get; set; }`.

`MainViewModel`:

- `public PlaceSort? CurrentSort { get; private set; }` (raises `PropertyChanged`), initialised from `PlaceSort.Parse(settings.PlacesSortKey, settings.PlacesSortDirection)` and applied in the constructor, after `PlacesView` exists.
- `public void SortBy(PlaceSortKey key)`: `CurrentSort = PlaceSort.Next(CurrentSort, key)`, then `ApplySort()`.
- `private void ApplySort()`: sets `CustomSort` to `Comparer<object>.Create((a, b) => comparer.Compare(((PlaceViewModel)a).Model, ((PlaceViewModel)b).Model))`, or `null` for stored order. Setting `CustomSort` refreshes the view itself.
- `PersistToSettings` writes `CurrentSort?.Format()` into the two settings fields, or nulls for stored order.

`MainWindow`:

- `Sorting="PlacesGrid_Sorting"` on the DataGrid. The handler sets `e.Handled = true`, parses `e.Column.SortMemberPath` with `Enum.TryParse<PlaceSortKey>`, calls `SortBy`, then `UpdateSortArrows()`.
- `UpdateSortArrows()` sets each column's `SortDirection` to `CurrentSort`'s direction for the column whose key matches, and `null` for the others. It is called once after the window loads.

### 5.5 The grid (roadmap §4.13; D31, D32)

`PlaceViewModel` adds:

```csharp
public DateTimeOffset? LastOpenedAt => Model.LastOpenedAt;
public int OpenCount => Model.OpenCount;

/// <summary>"—" if never opened, otherwise local short date and time (D31).</summary>
public string LastOpenedText => FormatLastOpened(Model.LastOpenedAt, TimeZoneInfo.Local, CultureInfo.CurrentCulture);

public static string FormatLastOpened(DateTimeOffset? value, TimeZoneInfo zone, CultureInfo culture);
```

`MainWindow.xaml`: the Date Added column is removed; the Last Opened and Opens columns are added with D31's bindings, widths and `SortMemberPath`s, and every existing column gets its `SortMemberPath` set to its key name (`Alias`, `Type`, `Destination`, `Favourite`). Opens is right-aligned through an `ElementStyle` setting `TextAlignment="Right"`.

### 5.6 Export and import (D33)

- `Export`: unchanged apart from `CurrentSchemaVersion` now being 3; the new fields serialise with the record.
- `GetImportCandidates`: the version table accepts 1 to 3 and chains the migrations as the store does; anything above `CurrentSchemaVersion` is refused as today. The `catch` and the self-duplicate filter are unchanged.
- `CommitImport`: copies `DateAdded`, `LastOpenedAt` and `OpenCount` from the candidate, normalised as in D33, and resolves `Id` against a `HashSet<Guid>` built from every record in `_places`, added to as the batch proceeds.

## 6. Constraints this plan checks itself against

| Constraint (source) | Honoured by |
|---|---|
| D1 — a failed save keeps the change | D26; test 17 |
| D3 — unresolved recovery refuses mutations | `RecordOpen`'s first line; tests 16, 27 |
| D5 — linked files stay free of `System.Windows.*` | `IShell`, `PlaceLauncher`, `PlaceSort` are UI-free; `ListSortDirection` is `System.ComponentModel` |
| D6 — classify at the catch | `MigrateV2ToV3` throws `JsonException` only |
| D7 — every enumeration decides active or deleted | `RecordOpen` ignores deleted (test 14); the grid, and so the sort, sees only active records; `CommitImport`'s id set deliberately covers both (D33) |
| D8 — `Places` is a fresh snapshot | Nothing new reads it in a loop |
| D11 — migrate before binding; loading never writes | D28; tests 6, 7 |
| D12 — one injected clock | `RecordOpen` stamps from `_time`; tests use `ManualTimeProvider` |
| D14 — every save purges first | Test 20 |
| D16/D17 — export active only; import gates on version | D33; tests 28, 31 |
| `Places` stays in stored order (Phase 2 hand-off §5) | D29 sets only the view's `CustomSort`; nothing in this phase reorders `Places` or touches `InsertRestored`; section 8's Undo-while-sorted item |
| The banner reads only `HasUnsavedChanges` | `Open` calls `RefreshPersistenceState(outcome.Persistence)` |
| UTC `DateTimeOffset` for every persisted timestamp (§3) | `LastOpenedAt` |
| The log never records aliases or destinations | D26; test 40 |
| No test reads the machine's zone | `FormatLastOpened` takes the zone; test 39 |
| `places.v1.json`, `places.v2.json` frozen | Their tests change assertions, never the files |

## 7. Test plan

xUnit on `FakePlacesStorage` unless the set-up says `TempDirectory`. New fake: `FakeShell : IShell`, with `HashSet<string> ExistingDirectories`, `Exception? ThrowOnOpen`, and `List<string> Opened`.

New classes: `PlacesStoreMigrationV3Tests` (or a section in `PlacesStoreMigrationTests`), `PlacesServiceSchemaV3Tests`, `PlacesServiceRecordOpenTests`, `PlaceLauncherTests`, `PlaceSortTests`. Import and export cases extend `PlacesServiceTests`' existing section; the settings case extends `SettingsServiceTests`; the formatter test extends a `PlaceViewModel` test class; the log test joins `DiagnosticLogTests`' collection.

| # | Test | Proves | Set-up |
|---|---|---|---|
| 1 | `MigrateV2ToV3` gives every record a distinct non-empty `id`, replaces a stray one, removes stray `lastOpenedAt`/`openCount`, skips null entries, sets version 3, and reports the counts; a non-object entry or non-array `places` throws `JsonException` | D28 | Inline `JsonObject`s |
| 2 | `places.v1.json` loads through both migrations: every Phase 2 assertion still holds, each record has a distinct `Id`, `LastOpenedAt` null, `OpenCount` 0; the fixture is byte-identical | Chain | Existing fixture test, extended |
| 3 | `places.v2.json` loads through v2 → v3 with the same extensions; deleted records keep their `DeletedAt` | Chain | Existing fixture test, extended |
| 4 | `places.v3.json` loads with every field intact, including ids, usage on an active and on a deleted record, and a never-opened record; hash pinned | The v3 shape | New fixture |
| 5 | Version 3 binds without migrating; 4 is `WrittenByNewerVersion` and the file is untouched | D28 gate | Replaces Phase 2 test 16's "3 is newer" |
| 6 | Loading a v1 or v2 store writes nothing | D11 | `WriteCount == 0` |
| 7 | Ids are assigned once: load v2, save, reload with a new service, and every `Id` is unchanged | D27, D28 | `TempDirectory` |
| 8 | A v3 store with a negative `openCount`, an offset `lastOpenedAt`, an empty `id` and a repeated `id` loads with 0, UTC, and fresh unique ids for the empty one and the *second* holder only; nothing is written | D27, D28 normalisation | Inline v3 |
| 9 | JSON shape: `id` first and `openCount` always present; `lastOpenedAt` absent when null and present as UTC otherwise | D28 | `LastWritten` |
| 10 | `TryAdd` gives a new place a fresh `Id`, `OpenCount` 0, `LastOpenedAt` null | D27 | — |
| 11 | Rename, edit destination, remove, restore, and restore under a new alias leave `Id` unchanged | D27 | One place through each |
| 12 | `RecordOpen` sets `LastOpenedAt` to the clock's now in UTC, increments `OpenCount`, and writes once | D26 | `ManualTimeProvider`; `LastWritten` |
| 13 | Two opens an hour apart: count 2, `LastOpenedAt` the second instant | D26 | `Advance` |
| 14 | `RecordOpen` on a deleted place changes nothing and writes nothing | D7, D26 | Seeded deleted record |
| 15 | `RecordOpen` on a place not in the store changes nothing and writes nothing | D26 | A stray `Place` |
| 16 | `RecordOpen` is refused while recovery is unresolved, with nothing changed | D3 | Damaged store |
| 17 | A failed save keeps the new usage in memory with `HasUnsavedChanges`; `RetrySave` writes it | D1 | `FailNextWrite` |
| 18 | `RecordOpen` leaves `IsFavourite`, `FavouriteOrder`, every other favourite, and `DateAdded` unchanged | §4.12 | Three favourites |
| 19 | `OpenCount` at `int.MaxValue` stays there | D26 | Seeded value |
| 20 | `RecordOpen` purges an expired Recently Deleted record in the same write | D14 | Advance past expiry |
| 21 | Usage survives remove and restore, and a restart | D7 | `TempDirectory` |
| 22 | Launcher, existing folder: `Launched`, the shell opened it, usage recorded | D24 | `FakeShell` |
| 23 | Launcher, missing folder: `Missing`, the shell was never called, usage unchanged, nothing written | §4.12, roadmap §5 | `FakeShell` |
| 24 | Launcher, shell throws: `Failed` with the exception's message, usage unchanged, nothing written | §4.12 | `ThrowOnOpen` |
| 25 | Launcher, URL: no existence check, `Launched`, recorded | D24 | Empty `ExistingDirectories` |
| 26 | Launcher, launch succeeds and save fails: `Launched`, `Persistence.Saved` false, usage in memory | D26 | `FailNextWrite` |
| 27 | Launcher, recovery unresolved: the shell still opens, usage unchanged | D26 | Damaged store |
| 28 | Export writes `schemaVersion` 3 with `id`, `openCount`, and `lastOpenedAt` when set; no deleted record | D33, D16 | `TempDirectory` |
| 29 | Export then import into an empty store keeps `Id`, `DateAdded`, `LastOpenedAt`, `OpenCount` | D33 lossless | Extends `Export_then_import_into_empty_store_round_trips` |
| 30 | A v2 export imports with fresh ids, usage defaults, and its original `DateAdded`; a v1 export still imports | D33, D28 | Inline documents, `TestPaths` |
| 31 | An import file with `schemaVersion` 4 is refused with the newer-version message | D33 | Replaces Phase 2 test 37's "3" |
| 32 | An incoming `Id` held by an active record, a deleted record, or an earlier record in the same batch gets a fresh one; a free one is kept | D33 | Three set-ups |
| 33 | An imported negative `openCount` becomes 0, and an offset `lastOpenedAt` becomes UTC | D33 | Inline v3 |
| 34 | `PlaceSort.Comparer`: each key ascending and descending on a small set, ties broken by alias then destination | D29 | Pure |
| 35 | Never-opened places: last under `LastOpened` descending, first ascending, and ordered by alias among themselves | D29 | Pure |
| 36 | `Next`: new key → first direction (descending for LastOpened, Opens, Favourite, DateAdded), same key → other direction, then null; a different key mid-cycle starts that key's cycle | D29 | Pure |
| 37 | `Parse` round-trips every key and direction through `Format`, case-insensitively; null, empty, unknown key, or unknown direction → null | D30 | Pure |
| 38 | `SettingsService` round-trips the sort fields; a version-2 file loads with both null and keeps its hotkey and bounds; an unrecognised key value leaves the other settings intact | D30 | `TempDirectory` |
| 39 | `FormatLastOpened`: null → "—"; a fixed instant formats as local `"g"` in `TestZones.PlusTen` with `en-GB`, and with `en-US` | D31 | Pure, no machine zone |
| 40 | A failed launch logs the place type and exception type, never the alias or destination; a recorded open logs nothing | D26 | `DiagnosticLogTests` collection |

`places.v3.json` has five records, all with Windows paths (loading never validates destinations) and `+00:00` dates:

- **Downloads** — a folder favourite at slot 0, opened 12 times, last opened `2026-09-24T21:15:00+00:00`.
- **QuickerPlaces Repo** — a URL favourite at slot 1, opened once.
- **Reports** — a folder, deleted, with usage (opened 3 times), proving that usage persists through Recently Deleted.
- **reports** — a folder, never opened, so it has no `lastOpenedAt` key and `openCount` 0.
- **Timesheets** — a folder, opened 2 times, whose `lastOpenedAt` equals Downloads' date to the minute but not the second, for a sort that needs full precision.

The five `id`s are fixed, readable GUIDs (`00000000-0000-0000-0000-00000000000{1..5}`). The fixture is frozen once committed.

Existing tests changed by this phase, and why:

- `PlacesStoreFixtureTests`: both fixture tests gain the v3 assertions (tests 2, 3). The files and their hashes do not change.
- `PlacesServiceSchemaV2Tests`: the version gate's "3 is newer" case moves to 4 (test 5), and any assertion that a write says `schemaVersion` 2 now says 3.
- `PlacesServiceTests`: the import cases that expect a newer-version refusal at 3 move to 4 (test 31); the round trip gains its D33 assertions (test 29). The export test reads version 3.
- `SettingsServiceTests`: any expectation of version 2 becomes 3.

## 8. Manual verification (Windows)

Recorded in `BUILD_SUMMARY.md` against each item. Items that cannot be tested are recorded as untested, not skipped.

- [ ] **Upgrade a real file.** Put a `places.json` written by the Phase 2 build in place. Launch: every place, favourite and Recently Deleted entry is intact; Last Opened shows "—" and Opens 0 for all. The file is still `schemaVersion` 2 until the first change. After one open it is 3, every record has an `id`, and `places.bak.json` is the v2 file. The log has a v2 → v3 migration line with counts and no alias.
- [ ] **Downgrade refusal.** Start the Phase 2 build (`main` at `1c03e59`) against the v3 file: the newer-version prompt appears, and the file is byte-identical afterwards.
- [ ] **Every open path counts once.** Double-click, Enter on a row, the context menu's Open, a bubble click, Ctrl+1, and Enter in the search box: each raises that place's Opens by exactly 1 and sets Last Opened to now, as date and time in your regional format.
- [ ] **Missing folder.** Rename a saved folder in Explorer, then open its place: the "no longer exists" message appears, and Opens and Last Opened are unchanged.
- [ ] **Failed launch.** A URL whose scheme has no handler (`nosuch://x`, entered by hand-editing `places.json`, since the Add dialog validates URLs): the "Couldn't open" message, no count. If Windows instead offers to find an app, record what happened, and whether it counted.
- [ ] **Sorting.** Click Last Opened: newest first, never-opened at the bottom. Click it again: oldest first, never-opened at the top. A third click returns the stored order with no arrow. Opens starts with the highest. Alias starts A–Z. Places with equal counts are in alias order.
- [ ] **Re-sort after an open.** Sorted by Last Opened, newest first: open a place lower down. It moves to the top at once and stays selected, and the arrow keys still move from it. Sorted by Alias: opening does not move anything.
- [ ] **Undo while sorted.** Sorted by Opens, remove a place, then Ctrl+Z: it comes back in its sorted position. Return to stored order: it is back in its original slot.
- [ ] **Remembered.** Set a sort, close, reopen: the same sort, with its arrow. Return to stored order, close, reopen: stored order. Put `"placesSortKey": "Nonsense"` in `settings.json`: the grid opens unsorted, and the hotkey and window position are intact.
- [ ] **Search with a usage sort.** Sorted by Last Opened: type a word two places share and press Enter. The more recently opened one opens.
- [ ] **Width.** At the 700 px minimum width, and on a high-DPI display, every column is readable, and Date Added is gone.
- [ ] **Locale.** With Windows set to a non-US region (for example English (Australia), or a custom short-date format), Last Opened follows it.
- [ ] **Save failure.** Deny write permission on `places.json`, then open a place: it launches, the banner appears, and Retry clears it once permission returns, with the count kept.
- [ ] **Export and import.** Export, then import the file into a fresh profile: Last Opened and Opens match the originals.

## 9. User guide changes (`USERGUIDE.md`)

- **The window, at a glance:** the grid's columns are now Alias, Type, Path / URL, Favourite, Last Opened and Opens; Date Added is still stored but no longer shown.
- **New section "Last Opened and Opens",** after Favourites. What they count, precisely: launches made by QuickerPlaces that Windows accepted. Opening the same folder from Explorer, or from another program, doesn't count, and a launch Windows accepted can still fail inside the program that received it. A missing folder or a refused launch doesn't count. "—" means never opened. Nothing is ever reordered or favourited automatically because of these numbers.
- **Sorting:** click a header to sort. Last Opened and Opens start with the most recent or most used; the others start A–Z. Click again to reverse, and a third time to return to the order you added them in. The sort is remembered on this computer. With Last Opened sorted newest first, typing a few letters and pressing Enter opens the most recently used match.
- **Exporting and importing:** exports now carry Last Opened, Opens and each place's original Date Added, and importing keeps them. Exports from older versions still import; files from a newer version are refused.
- **Where your data lives:** the first save after upgrading converts `places.json` to the new format. Older versions of QuickerPlaces refuse to open it and cannot damage it, and right after that save, `places.bak.json` is the pre-upgrade copy. Usage statistics are in `places.json`, so they follow a roaming profile; the sort is in `settings.json`, so it does not.

## 10. Order of work

Each step builds and leaves the suite green before the next begins. This is the intended commit sequence.

1. **The v2 → v3 migration, pure and not yet called.** `MigrateV2ToV3`, `MigrationV3Report`. Test 1. Nothing loads through it yet, so the step is provably inert.
2. **Schema v3.** `Place`'s three fields, `PlacesStore`, `CurrentSchemaVersion = 3`, the load chain and table, normalisation, `Id` on `TryAdd`, `places.v3.json` and its csproj copy item. Tests 2–11 and the adapted existing tests. Run the suite under two `TZ` values.
3. **`RecordOpen`.** Tests 12–21.
4. **The launch seam.** `IShell`, `WindowsShell`, `PlaceLauncher`, `OpenOutcome`, `FakeShell`; `MainViewModel.Open` rewired, with `place.Refresh()` after a launch. Tests 22–27 and 40. From this commit on, every open is counted.
5. **Export and import.** 5.6. Tests 28–33.
6. **`PlaceSort` and the settings.** `PlaceSort.cs`, `AppSettings` v3. Tests 34–38. Not wired to the UI yet.
7. **The UI.** Columns (D31), `LastOpenedText` and its formatter (test 39), `CurrentSort`/`SortBy`/`ApplySort`, the `Sorting` handler and arrows, the D32 re-sort, `PersistToSettings`. Compile-checked; section 8 on Windows. Run the suite under two `TZ` values.
8. **Documentation.** `USERGUIDE.md` (section 9); Phase 3 in `BUILD_SUMMARY.md` with the section 8 results and any departures from this plan; this plan's status line; the roadmap's status and "Detailed plans" lines; `ai/README.md`; a dated Phase 3 hand-off. The Phase 4 detailed plan follows once section 8 has been walked.

## 11. Phase 3 definition of done

- Every open path counts a launch exactly once, and only when the pre-launch check passed and Windows accepted the launch; a missing, refused, or (in Phase 5, cancelled) launch never changes the statistics.
- Usage is saved at once through the reliable save path; a failed save keeps it in memory behind the banner, and recovery never stops a place from opening.
- An existing v1 or v2 `places.json` migrates to v3 without loss, every place gains a stable `Id` exactly once, and nothing is written until a save succeeds. Phase 1 and 2 builds refuse the v3 file without touching it.
- The grid shows Last Opened (local date and time, "—" for never) and Opens, sorts on every column with never-opened places consistently at the old end, and remembers the sort, including "no sort", across restarts.
- Export and import round-trip `Id`, `DateAdded`, `LastOpenedAt` and `OpenCount`.
- All tests in section 7 pass alongside the existing suite, under at least two time zones; the section 8 checklist has been walked on Windows; the user guide and `BUILD_SUMMARY.md` are updated.

## 12. Open questions and notes for later phases

1. **Usage on the favourite bubbles.** The bubbles are the main surface when the grid is collapsed, and their tooltip could add "Opened 12 times, last 25/09/2026 14:32". Recommendation: leave it for Phase 7 (retrieval polish), where tooltips are already being reworked (§4.25).
2. **A slow pre-launch check.** `Directory.Exists` on an unreachable network path can block the UI thread for many seconds. This predates Phase 3, and Phase 3 does not make it worse. Phase 4 adds `File.Exists` beside it; its plan should decide whether the check moves off the UI thread.
3. **For Phase 5 — what counts when a launch is indirect.** Roadmap §4.21 may launch a chosen Revit release and tell the user to open the model from inside it. Is that a recorded open of the model's place? Recommendation: yes. QuickerPlaces launched on that place's behalf, and Windows accepted the launch, which is D24's test. But the Phase 5 plan should decide it explicitly, and a **Cancelled** status from **Ask each time** must never count.
4. **For Phase 5 — cleaning up per-file choices.** `applications.local.json` keys by `Id` (D27). When a place is permanently deleted or purged, its entry is orphaned. Phase 5 should prune orphans on load rather than hook every deletion path.
5. **For Phase 9 — naming.** Phase 9's Activity window has a "last opened" column over a different definition (D24). Recommendation: call it **Last visited** there, so the two numbers are never mistaken for each other.
6. **`FrameworkElement.Language`.** D31 removes the grid's only `StringFormat` date. The Recently Deleted dialog already formats in code. Nothing else formats a date, so setting the language stays unneeded unless a later phase adds a XAML `StringFormat`; if one does, format in the view model instead, as D31 does.
7. **Resetting usage.** A "Reset Opens" command is not in the roadmap and is not planned. Removing and re-adding a place is the workaround. Revisit only if someone asks.

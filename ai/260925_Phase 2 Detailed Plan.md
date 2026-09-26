---
title: QuickerPlaces — Phase 2 Detailed Implementation Plan
status: implemented 2026-09-25 (steps 1–8, ab43db7 to cbde92f, on claude/roadmap-status-4tv9nf, not yet merged) — all 237 tests pass; the UI is compile-verified only, never run; the section 8 checklist has not been walked. Implemented at the user's request ahead of the Phase 1 checklist that the 2026-09-21 hand-off's decision 4 said should gate it. Where the build departs from this plan: BUILD_SUMMARY.md, Phase 2
created: 2026-09-25
parent: ai/260901_Professional Improvements Plan.md
covers: sections 4.7 to 4.10 (Seven-day Recently Deleted)
builds_on: ai/260925_Handoff.md; ai/260921_Handoff.md §5–§6; ai/BUILD_SUMMARY.md D1–D6 and "Merging Phase 1 with the feature work"
last_revised: 2026-09-25 — status updated after implementation (step 9); the plan body is unchanged
---

# Phase 2 — Seven-day Recently Deleted

## 0. How to use this document

This is the file-level and signature-level plan for Phase 2 of the [Professional Improvements Plan](260901_Professional%20Improvements%20Plan.md) (§4.7–4.10). The roadmap states *what* must be true; this document states *which files change, in what order, and how each requirement is proven*. It was written against `main` as of 2026-09-25 (Phase 1 merged with the feature work, 125 tests passing); line numbers cite that code and will drift once work starts.

Rules for this phase:

- Every behaviour in section 7 has a test on the storage seam, or is a named manual-verification item. Nothing is left unproven by omission.
- Phase 1's settled constraints are built on, not revisited: D1 (a failed save keeps the change in memory), D2 (whole-store writes), D3 (unresolved recovery refuses every mutation), D5 (no test constructs a `Window`), D6 (classify at the `catch`), the asymmetry between `Damaged` and the other two failure outcomes, and the rule that the unsaved banner reads only `PlacesService.HasUnsavedChanges`. The hand-offs list them ([2026-09-21 §5–§6](260921_Handoff.md), [2026-09-25 §5](260925_Handoff.md)); they are cited here, not restated.
- This is the first phase to increment the places schema. `src/QuickerPlaces.Tests/Fixtures/places.v1.json` stays byte-identical; `AppSettings.CurrentSchemaVersion` stays at 2 and is not touched.

Decisions are numbered **D7 onwards**, continuing Phase 1's D1–D6, because this plan cites those constantly and a second "D1" would be ambiguous. (Phase 9's plan has its own, separate D-numbers.)

Tooling, on Linux or Windows, from `src/`:

- `dotnet test QuickerPlaces.Tests/QuickerPlaces.Tests.csproj` — the suite (125 tests today, all passing).
- `dotnet build QuickerPlaces/QuickerPlaces.csproj -p:EnableWindowsTargeting=true` — compile-checks the WPF app, XAML included. It cannot be run on Linux; UI behaviour is the section 8 checklist.

## 1. Where Phase 2 starts from

The feature work already built half of §4.8. The plan starts from that code, not from the roadmap alone.

| Area | Today | Where | Phase 2 |
|---|---|---|---|
| Remove | Hard delete: `_places.RemoveAt(index)`, returns a `RemovedPlace(Place, Index, FavouriteOrder)` | `PlacesService.Remove`, `PlacesService.cs:308`; record at `:882` | Soft delete: stamp `DeletedAt` on the same record, in the same slot (D7) |
| Undo | Session-only `Stack<RemovedPlace>`; `TryRestore` re-inserts the record at its old index and bubble slot | `MainViewModel.cs:41`, `:364`; `PlacesService.TryRestore` `:340` | Un-deletes the same record (clears `DeletedAt`); `RemovedPlace` retired (D10) |
| Confirmation | `MessageForm` Yes/No, "You can undo this with Ctrl+Z while QuickerPlaces stays open" | `MainViewModel.Remove`, `:341` | Dropped (roadmap §4.8, settled 2026-09-21); tooltip added |
| Status bar | 8 s `DispatcherTimer`, Undo and dismiss buttons; nothing in it calls `Focus()` | `MainViewModel.cs:45`, `MainWindow.xaml:371` | 10 s when it offers Undo, paused under the pointer or focus (D19) |
| Schema | `CurrentSchemaVersion = 1`; an empty "migration" branch with a log line | `PlacesService.cs:83`, `:619` | 2, with a real v1 → v2 migration (D11) |
| `DateAdded` | `DateTime`, initialised to `DateTime.Now` | `Place.cs:28`; `TryAdd` `:222`; `CommitImport` `:523` | UTC `DateTimeOffset`, converted once (§3, §4.7) |
| Clock | `DateTime.Now` / `DateTimeOffset.Now` called directly | `:222`, `:523`, `:714` | One injected `TimeProvider` (D12) |
| Import | Deserialises straight into `PlacesStore`; never reads `schemaVersion` | `GetImportCandidates`, `:447` | Version-gated, migrated, deleted records skipped (D17) |

Two findings from reading the code that change how §4.7 must be implemented:

1. **Most existing `dateAdded` values already carry an offset.** §4.7 says values written before the conversion "carry no offset". The committed fixture's values don't, but the application never wrote that shape: `DateTime.Now` has `DateTimeKind.Local`, and System.Text.Json serialises a local `DateTime` with the machine's offset (`"2026-09-26T06:43:23.3199061+10:00"`, checked on .NET 10). Offset-less values exist only where someone hand-edited the file, and in the fixture. So the migration converts an offset-bearing value *exactly*, and applies the "local time on the migrating machine" assumption only to offset-less ones. The log counts both (D11).
2. **Changing the property type alone would do the conversion silently and untestably.** Deserialising an offset-less string into `DateTimeOffset` makes System.Text.Json assume the *process's* local offset, which a test cannot control and which applies a different rule from the one §4.7 asks to be recorded. The migration therefore runs on the JSON before it is bound to `Place` (D11).

## 2. Scope

**In scope:** schema v2 (`DeletedAt`, UTC `DateAdded`) and its migration; soft delete; Undo as un-delete; the Recently Deleted dialog and its entry point; seven-day expiry and its purge points; restore conflicts with an edit-before-restore flow; how export and import treat deleted records and the new version; dropping the Remove confirmation; the user guide.

**Out of scope, deliberately:**

- Preserving `DateAdded` and favourite state through import. `CommitImport` creates fresh records stamped "now" and never favourited, today and after this phase. The 2026-09-21 hand-off's decision 6 records "import keeps usage metadata" as already decided, but that is roadmap §4.18, which is Phase 4; the code does not do it yet. Phase 2 only ensures a deleted state can never arrive through import (D17).
- Phase 7's keyboard audit and the header's layout rework (Phase 4's single **Add...** control). The Recently Deleted entry point is placed so either can move it later (D20).
- Any change to `settings.json`, `AppSettings`, `SettingsService`, the recovery dialog, the single-instance code, or `DiagnosticLog` itself.

## 3. Target file layout

| File | Status | Purpose |
|---|---|---|
| `src/QuickerPlaces/Models/Place.cs` | changed | `DateAdded` becomes a UTC `DateTimeOffset`; adds `DateTimeOffset? DeletedAt`; documents what a deleted record's favourite fields mean (D9) |
| `src/QuickerPlaces/Models/PlacesStore.cs` | changed | `SchemaVersion` initialiser to 2 |
| `src/QuickerPlaces/Services/PlacesStoreMigration.cs` | new, UI-free | The v1 → v2 transform on a `JsonObject`, plus a report of what it did (D11) |
| `src/QuickerPlaces/Services/RecentlyDeletedPolicy.cs` | new, UI-free | Retention period, expiry test, days-remaining number and text (D13) |
| `src/QuickerPlaces/Services/PlacesService.cs` | changed | Clock, version gate at 2, active/deleted split, soft delete, un-delete, restore conflicts, permanent delete, purge; `RemovedPlace` removed, `RestoreConflict` added |
| `src/QuickerPlaces/Models/PlaceFormMode.cs` | changed | Adds `Restore` |
| `src/QuickerPlaces/Views/PlaceFormDialog.xaml` (+ `.cs`) | changed | `ShowRestore`: explanation line, both fields prefilled, **Restore** button (D15) |
| `src/QuickerPlaces/Views/MessageForm.xaml.cs` | changed | `ShowDestructiveConfirm`: Cancel is the default button (D18) |
| `src/QuickerPlaces/Views/RecentlyDeletedDialog.xaml` (+ `.cs`) | new | The §4.9 dialog |
| `src/QuickerPlaces/ViewModels/RecentlyDeletedRowViewModel.cs` | new | One dialog row; its texts come from `RecentlyDeletedPolicy` |
| `src/QuickerPlaces/ViewModels/PlaceViewModel.cs` | changed | `DateAdded` shown in local time |
| `src/QuickerPlaces/ViewModels/MainViewModel.cs` | changed | Undo stack of `Place`; no Remove confirmation; conflict-aware Undo; `ShowRecentlyDeletedCommand`; status timings |
| `src/QuickerPlaces/Views/MainWindow.xaml` (+ `.cs`) | changed | Header button, Remove tooltip, status-bar hover/focus pause, focus after Delete |
| `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj` | changed | Links the two new UI-free files; copies `places.v2.json` |
| `src/QuickerPlaces.Tests/Fakes/ManualTimeProvider.cs`, `Fakes/TestZones.cs` | new | Settable clock and deterministic time zones |
| `src/QuickerPlaces.Tests/Fixtures/places.v2.json` | new, frozen once written | The v2 shape, including deleted records |
| `src/QuickerPlaces.Tests/*` | new and changed | See section 7 |
| `USERGUIDE.md` | changed | See section 9 |

`App.xaml.cs` needs no change: startup purge happens inside `PlacesService`'s constructor and `Reload()`, which the existing recovery loop already calls. Leave the loop alone (hand-off 2026-09-21 §6).

No DI container, no new NuGet package, no `IDialogService`, as in Phase 1. `System.TimeProvider` is in the base class library.

## 4. Design decisions

Settled here so they are not re-litigated during implementation.

**D7 — One list and a flag, not two lists.** A removed place stays in `_places`, in the same slot, with `DeletedAt` set. The alternative, moving it to a separate `_deleted` list, would leave every existing `_places` query correct by default, but it loses the record's list position the moment the file is saved. Restoring it after a restart, or from the dialog, could then only append it at the end. With the flag, "restore the exact place" is simply "clear the flag": the list position survives restarts because the file order *is* the list order, and D2's whole-store write already carries it. The price is that every enumeration of `_places` has to decide whether it means active records. Section 5.3 lists all of them, and a private `Active` sequence makes the common answer one word.

**D8 — `Places` keeps meaning "active places"; `RecentlyDeleted` is a separate accessor.** `PlacesService.Places` returns only records with `DeletedAt == null`, so `MainViewModel`'s constructor, `App`'s close log, and most of the tests keep working unchanged. It becomes a fresh list per call (a filtered snapshot, which its doc comment already promises), so callers must not index it inside a loop over the service. Deleted records are reached only through `RecentlyDeleted`, which is ordered newest deletion first.

**D9 — A deleted record keeps its favourite fields as a memory of where it was.** On soft delete, `IsFavourite` and `FavouriteOrder` are left as they were; only active records take part in favourite numbering, so the remaining bubbles are renumbered dense and the deleted record's `FavouriteOrder` is now just a remembered slot. On restore, that slot is treated as an index into the *current* bubble row, clamped to its end. Later bubbles shift right, then the row is renumbered dense. This is exactly today's `TryRestore` rule, now persisted, so it works after a restart and from the dialog, not only through in-session Undo. It defines §4.8's "ordering where possible": the old slot if the row is unchanged, otherwise the same index in the row as it is now. A favourite always comes back as a favourite. Nothing else ever reads a deleted record's favourite fields.

**D10 — `RemovedPlace` is retired; the Undo stack holds `Place`.** Its `Index` and `FavouriteOrder` are now carried by the record itself (D7, D9), and a copy of them in the stack could only go stale. `Remove` changes from `out RemovedPlace? removed` to `out bool removed`. The existing tests that name the type are adapted in step 4 of section 10.

**D11 — The v1 → v2 migration runs on the JSON, before binding, and converts each value exactly once.** `PlacesStoreMigration.MigrateV1ToV2` rewrites each record's `dateAdded` string to a UTC ISO value and sets `schemaVersion` to 2. Only then is the document deserialised into v2 types.

- A value with an offset or `Z` is converted exactly; the migrating machine's zone plays no part.
- A value without one is interpreted as local time in the injected zone (D12). A time that falls in a spring-forward gap, or twice in a fall-back hour, takes the zone's *standard* offset, which is what `TimeZoneInfo.GetUtcOffset` returns for both. So the conversion never throws on a real calendar.
- A missing `dateAdded` becomes "now" from the injected clock. That is what the v1 loader effectively did through `Place`'s `DateTime.Now` initialiser.
- A present value that is not a date string throws `JsonException`, so D6's existing catch classifies the store `Damaged`, and D3 and the recovery prompt take over. This is what "a store that failed to migrate" means in §4.10.

*Exactly once* holds because nothing ever migrates a v2 document, and because loading never writes (§4.3, Phase 1 test 15). If the first save fails, the file on disk is still the untouched v1 original, and the next launch migrates it from the same source values. The log records the assumption on every migration: the record count, how many values carried an offset, how many were interpreted as local time and in which zone (`TimeZoneInfo.Id`), and how many were missing. It never records an alias or a destination. The code carries the same assumption as a comment on the offset-less branch, as §4.7 asks.

**D12 — One clock: `System.TimeProvider`, injected through the constructor.** `PlacesService(IPlacesStorage storage, TimeProvider? timeProvider = null)` defaults to `TimeProvider.System`. It supplies `DateAdded` and `DeletedAt` (`GetUtcNow()`), purge decisions, the migration's local zone (`LocalTimeZone`), the quarantine timestamp (`GetLocalNow()`), and the dialog's countdown through `PlacesService.UtcNow`, so the countdown a user reads and the purge that follows it use the same clock. Tests use `ManualTimeProvider`, a small subclass overriding `GetUtcNow()` and `LocalTimeZone`. `Microsoft.Extensions.TimeProvider.Testing` would do the same, but it is a package for fifteen lines.

**D13 — "Seven full days" is 168 hours of elapsed UTC time.** A record deleted at instant *D* is expired from *D* + 7 × 24 h exactly (`now >= DeletedAt + RetentionPeriod`). It is removed at the first purge point at or after that instant (D14), so seven days is a guaranteed minimum, not an exact deadline. Calendar-day counting was rejected because it would keep a place for anything from six to seven days depending on when it was removed, which fails "full". Counting in UTC makes daylight-saving days irrelevant. A clock set backwards only lengthens the wait; a clock set forwards shortens it, which is accepted. **Days remaining** is the ceiling of the time left, clamped to 0–7: "7 days" just after removal, "1 day" within the last 24 hours, and "Expiring" once it is past expiry but not yet purged. A place in that last state is still restorable. A `DeletedAt` in the future (another machine's skewed clock, arriving through roaming) displays as 7 days and simply waits.

**D14 — Purge at two points only, both behind the existing gates.**

1. **After a successful load.** In the constructor and in `Reload()`, and only when the outcome is `Ok`, after any migration has succeeded. This purge is in memory only; it reaches disk with the next successful save, like the migration it follows. Loading never writes.
2. **Inside `Persist()`, before serialising.** So every normal save, `RetrySave` included, writes a store without expired records.

`Persist()` is unreachable while D3 blocks mutations, and a store that failed to load or migrate is empty in memory. So "never purge from a store that failed to load or migrate" holds by construction. `PurgeExpired` also returns early when `IsRecoveryUnresolved`, so that a later caller cannot break the rule by accident. The purge logs how many records it removed and at which point, never which ones. On a roaming profile used on two machines, a purge on either removes the record for both. That is the same last-writer-wins boundary the roadmap already declares a non-goal (§2).

**D15 — Deleted records are invisible to duplicate checks, so restore conflicts are normal, and there is one conflict flow.** §4.10 says deleted records do not block active ones, so `ValidateAlias` and `ValidateResource` look at active records only. A user can therefore remove "Docs", add a new "Docs", and later try to restore the old one. Every restore path (Ctrl+Z, the status bar's Undo, and the dialog's **Restore selected**) handles a conflict the same way. `GetRestoreConflict` names the active place holding the alias and/or the destination. `PlaceFormDialog.ShowRestore` shows that explanation above both fields, prefilled and editable with the usual live validation, and commits through `TryRestore(place, alias, resource, …)`. **Cancel** leaves the record in Recently Deleted, never discarded. This replaces today's Undo behaviour, where a conflict showed a message and dropped the entry. That was acceptable when the record then existed nowhere; now it still exists, and §4.10 asks for the edit.

**D16 — Export never includes deleted records, and never writes `deletedAt`.** Export is a list of places the user chose to keep or share; Recently Deleted is a seven-day safety net, not a list anyone chose. `Export` filters deleted records out even if a caller passes them in. `Place.DeletedAt` is written only when non-null (`[JsonIgnore(Condition = WhenWritingNull)]`), which also keeps an active record's on-disk shape as close to v1 as possible. Exports carry `schemaVersion: 2`. A v2 export also imports into a v1 build, because that build's import never reads the version and the dates parse into its `DateTime`. That is compatibility nobody has to maintain.

**D17 — Import brings in active records only; old exports keep working; newer exports are refused.** `GetImportCandidates` reads `schemaVersion` first.

| Version in the file | Treatment |
|---|---|
| Missing | Treated as 1 — unchanged leniency |
| 1 | Run through the same `PlacesStoreMigration` as the store, so its dates follow the same rule |
| 2 | Read as is |
| Greater than 2 | Refused: "That file was exported by a newer version of QuickerPlaces. Update QuickerPlaces to import it." No candidates |
| Non-numeric | Refused as not a QuickerPlaces export |

Records carrying `deletedAt` are not offered, whether they come from a hand-edited file or a copied `places.json`: importing a place into Recently Deleted is meaningless, and importing it as active would resurrect something the user deleted on the other machine. Collision checks run against active records only (D15), so an incoming place whose alias matches only something in Recently Deleted is offered, and importing it simply makes a later restore of the deleted one a conflict. `CommitImport` still builds fresh records with `DeletedAt = null`, so no deleted state can enter by this path.

Import stays lenient about a missing version where the store is strict (Phase 1 5.3) because the two carry different risks. The store's strictness exists to stop a foreign file being loaded and then overwritten. Import is additive, reviewed row by row, and never writes the source file.

**D18 — The Remove confirmation goes; the two irreversible confirmations cannot be accepted with Enter.** Settled by the roadmap (§4.8, 2026-09-21); this decision covers how. `MessageForm`'s `YesNo` set makes **Yes** the default button, so Enter confirms, which is the reflex Phase 1 5.4 refused to allow for "Start with an empty list". A new `MessageForm.ShowDestructiveConfirm(message, title, confirmLabel, owner)` shows **Cancel** as both the default and the cancel button, and the confirm button labelled for what it does ("Delete permanently", "Empty Recently Deleted") in the plain style. It returns `bool`. **Delete selected permanently** and **Empty Recently Deleted** use it; nothing else does.

**D19 — The Undo notification stays for 10 seconds, and never takes focus.** It stays up 10 seconds when it offers Undo, restarting on each new message; other status messages keep today's 8 seconds. It is paused while the pointer is over the bar or keyboard focus is inside it, so reaching for **Undo** never races the timer. It never calls `Focus()` and never takes focus when it appears, the same rule as the banner. The bar is a reminder, not the only way back: Ctrl+Z works after it hides, for every removal this session, most recent first, and Recently Deleted keeps the place for seven days after that. That is why a short timeout is safe. The Undo stack stays session-only; after a restart, Recently Deleted is the way back.

**D20 — The entry point is an icon button in the header's icon group.** §4.9 says "the main window's secondary menu", but the main window has no menu: the header has two icon-only buttons (Settings and Open data folder), then Import, Export, Add Folder, Add URL (`MainWindow.xaml:181–194`), and it already runs out of room near the 700 px `MinWidth`. A third icon button — the Segoe trash glyph `U+E74D`, tooltip "Recently Deleted — places you removed in the last 7 days", `AutomationProperties.Name="Recently Deleted"` — sits with the other two secondary actions and costs about 40 px. Folding all three into a "More" menu is the natural move when Phase 4 replaces the two Add buttons with one **Add...** control; it is open question 1.

**D21 — The dialog is a thin view; its logic is in `PlacesService` and two linked UI-free helpers.** Everything the dialog decides (what is deleted, what has expired, how many days remain, what a restore conflicts with, what a batch restore did) is a `PlacesService` member or a static helper in `RecentlyDeletedPolicy`. `PlacesService.cs` is already linked into the test project, and the policy file is added to the link list. The window binds rows and calls methods, so D5 holds with no new seam.

**D22 — Batch operations persist once, and a batch restore goes newest deletion first.** `RestoreSelected`, `DeletePermanently` and `EmptyRecentlyDeleted` each write the store once, as `CommitImport` does (Phase 1 test 19). They write nothing if nothing changed. When two selected records share an alias, the most recently deleted one is restored and the older one comes back as a conflict for the D15 flow. The newer copy is the likelier one to be wanted.

## 5. Work items

### 5.1 Schema v2 and the migration (roadmap §4.7)

`Place`:

```csharp
/// <summary>When this place was first added, in UTC. Converted from a local DateTime by the v1 → v2 migration (PlacesStoreMigration).</summary>
public DateTimeOffset DateAdded { get; set; } = DateTimeOffset.UtcNow;

/// <summary>
/// Null for an active place. Set (UTC) when the place is removed: it is then in
/// Recently Deleted until restored, permanently deleted, or purged seven full days
/// later (RecentlyDeletedPolicy). Set and cleared only by PlacesService. While set,
/// IsFavourite/FavouriteOrder are a remembered bubble slot, not a live position (D9).
/// </summary>
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public DateTimeOffset? DeletedAt { get; set; }
```

The `DateAdded` initialiser only matters for a v2 record hand-edited to lose the field; every code path that creates a place sets it from the clock. `PlacesStore.SchemaVersion`'s initialiser becomes 2, and `Persist` and `Export` also set it explicitly from `PlacesService.CurrentSchemaVersion`, so the model default is never what decides the version on disk.

`PlacesStoreMigration` (static, UI-free, linked into the tests):

```csharp
public static class PlacesStoreMigration
{
    /// <summary>
    /// Rewrites a schemaVersion 1 document in place as schemaVersion 2 (D11). Throws
    /// JsonException for any shape it cannot migrate, so PlacesService's existing catch
    /// classifies the store as Damaged (D6) rather than a new exception type escaping
    /// the constructor.
    /// </summary>
    public static MigrationReport MigrateV1ToV2(JsonObject root, TimeZoneInfo localZone, DateTimeOffset utcNow);
}

/// <summary>What a migration did, for the log line (counts only: never aliases or destinations).</summary>
public readonly record struct MigrationReport(int Records, int ExactOffsets, int InterpretedAsLocal, int MissingDates, string ZoneId);
```

The per-value rule, with the parse that tells the cases apart (both `TryGetValue` calls go through System.Text.Json's own ISO parser):

```csharp
var node = record["dateAdded"];                      // absent or JSON null: missing
if (node is null)
{
    record["dateAdded"] = utcNow;                     // counted in MissingDates
    continue;
}
if (node is not JsonValue value || !value.TryGetValue<DateTime>(out var parsed))
    throw new JsonException("dateAdded is not a date.");

DateTimeOffset utc;
if (parsed.Kind == DateTimeKind.Unspecified)
{
    // No offset in the file: interpreted as local time on the migrating machine
    // (roadmap §4.7). Standard offset for a gap or an ambiguous hour — never throws.
    utc = new DateTimeOffset(parsed, localZone.GetUtcOffset(parsed)).ToUniversalTime();
}
else
{
    value.TryGetValue<DateTimeOffset>(out var exact); // keeps the file's own offset
    utc = exact.ToUniversalTime();
}
```

`PlacesService` load path (`LoadFromDisk`, `PlacesService.cs:547`):

- `CurrentSchemaVersion = 2`.
- Parse with `JsonNode.Parse` instead of `JsonDocument.Parse`, so a v1 document can be migrated and then bound with `node.Deserialize<PlacesStore>(JsonOptions)`. The two separate `try` blocks (read, then parse) stay exactly as they are (D6).
- The version table becomes:

| `schemaVersion` | Outcome |
|---|---|
| Missing or non-numeric | `Damaged`, unchanged |
| Less than 1 | `Damaged`. New: an unknown *lower* version is not a known migration (§4.3), and today's `< Current` branch would have accepted 0 |
| 1 | Migrate in memory; `Ok` |
| 2 | Bind directly; `Ok` |
| Greater than 2 | `WrittenByNewerVersion`, unchanged: file untouched, mutations blocked |

- After binding, `DateAdded` and `DeletedAt` are normalised with `ToUniversalTime()`, so a hand-edited offset never survives into a write.
- Log lines: the migration line from D11; and "Loaded {active} place(s) and {deleted} in Recently Deleted from {path} (schemaVersion {n})".

Every build before this phase refuses the v2 file as `WrittenByNewerVersion`, without touching it. That is the gate working, and the user guide says so (section 9). Right after the first v2 save, `places.bak.json` holds the v1 file, because `File.Replace` keeps the outgoing file; it is overwritten by the save after that. Open question 2 asks whether that is enough.

`PlaceViewModel.DateAdded` returns `Model.DateAdded.LocalDateTime`, so the grid's `{0:d}` column keeps showing the local date. Binding the UTC value directly would show tomorrow's date for an evening add east of Greenwich.

### 5.2 The clock (D12)

```csharp
public PlacesService() : this(FilePlacesStorage.ForDefaultLocation(), TimeProvider.System) { }

public PlacesService(IPlacesStorage storage, TimeProvider? timeProvider = null)

/// <summary>The clock purge decisions use — the Recently Deleted dialog's countdown reads it too, so the two agree.</summary>
public DateTimeOffset UtcNow => _time.GetUtcNow();
```

`TryAdd` and `CommitImport` stamp `DateAdded = _time.GetUtcNow()`, `Remove` stamps `DeletedAt` the same way, and `QuarantineAndStartEmpty` names its file from `_time.GetLocalNow()`. Apart from the type change in 5.1, none of this alters what production does; it makes each timestamp testable.

### 5.3 Every enumeration of places, and its treatment

This is the list D7 makes necessary. "Active" means `DeletedAt == null`; the private helper is `private IEnumerable<Place> Active => _places.Where(p => p.DeletedAt is null);`.

| # | Location (today) | What it does | Phase 2 treatment |
|---|---|---|---|
| 1 | `PlacesService.Places` (`:99`) | Public list, read by the view model, `App`, tests | **Active only**, fresh list per call (D8) |
| 2 | new `PlacesService.RecentlyDeleted` | — | Deleted only, newest `DeletedAt` first |
| 3 | `ValidateAlias` (`:113`) | Duplicate alias check | **Active only** (§4.10, D15) |
| 4 | `ValidateResource` (`:142`) | Duplicate path/URL check | **Active only** |
| 5 | `TryAdd` (`:225`) | Appends | Unchanged; `DeletedAt` null |
| 6 | `TryRenameAlias`, `TryEditResource` (`:232`, `:249`) | Edit one record | Refuse a deleted record: `ValidationResult.Fail("\"X\" is in Recently Deleted. Restore it first.")`. Only a bug could reach this, but it must not edit a record the user cannot see |
| 7 | `ToggleFavourite` count (`:281`) | Next favourite slot | **Active favourites only**, otherwise a deleted favourite's remembered slot leaves a gap. No-op (`PersistenceResult.Ok()`, nothing written) for a deleted record |
| 8 | `SetFavouriteOrder` (`:291`) | Numbers the list it is given | Skips any deleted record in the list; the caller passes bubbles, which are active |
| 9 | `Remove` (`:308`) | Hard delete | Soft delete (5.4) |
| 10 | `TryRestore` (`:340`) | Re-insert and shift favourites | Un-delete; the shift loop runs over **active favourites only** (5.4) |
| 11 | `RenumberFavourites` (`:380`) | Dense 0..n−1 | **Active only**; deleted records keep their remembered slot (D9) |
| 12 | `Export` (`:392`) | Writes the given list | Filters out deleted records (D16) |
| 13 | `GetImportCandidates` (`:447`) | Reads a file, drops collisions | Skips records with `deletedAt`; collisions against active only (D17) |
| 14 | `CommitImport` (`:502`) | Re-validates and appends | Validation active only; new records active |
| 15 | `LoadFromDisk` (`:635`) | Drops null entries | Keeps deleted records; the purge follows in the caller (D14) |
| 16 | `Reload` / `QuarantineAndStartEmpty` (`:673`, `:717`) | Replace or clear the list | Unchanged; `Reload` purges on `Ok` |
| 17 | `Persist` (`:752`, `:776`) | Serialises everything; logs a count | Purges first (D14), then serialises **all** records (this is how Recently Deleted persists); the failure log counts active and deleted separately |
| 18 | `App` close handler (`App.xaml.cs:78`) | Logs `Places.Count` | Unchanged (active count) |
| 19 | `MainViewModel` constructor (`:55`) | Builds the grid's `ObservableCollection` | Unchanged: `Places` is active. Grid, search, header counts, empty-state text and `ExportCommand.CanExecute` all derive from this collection, so none of them can see a deleted record |
| 20 | `PlacesView` filter / `PlaceSearch.Matches` (`:62`) | Search | Unchanged; it filters the active collection only |
| 21 | `RebuildFavourites` (`:515`), `OpenFavouriteAt` (`:485`), `MoveFavourite` (`:536`) | Bubbles, Ctrl+1–9, drag reorder | Unchanged; built from the active collection |
| 22 | `MainViewModel.Export` (`:438`) | Passes the grid's places to `ExportDialog` | Unchanged; active, and `Export` filters again anyway |
| 23 | `MainViewModel.UndoRemove` (`:388`) | Inserts the restored row at the old index | Inserts at `_placesService.Places.IndexOf(place)`, the record's position among active places, since the stored index is gone (D10) |
| 24 | `EmptyGridMessage` (`:162`) | "No places yet…" | When the grid is empty but Recently Deleted is not, add "Places you removed are in Recently Deleted." |

Nothing in `MainWindow.xaml.cs`, `ExportDialog`, `ImportDialog` or `PlaceFormDialog` enumerates the store; they act on lists or places handed to them.

### 5.4 Soft delete and Undo (roadmap §4.8)

`PlacesService`:

```csharp
/// <summary>Moves <paramref name="place"/> to Recently Deleted. removed is false when blocked (D3), not in the store, or already deleted.</summary>
public PersistenceResult Remove(Place place, out bool removed)

/// <summary>Un-deletes <paramref name="place"/> exactly as it was: same record, same list slot, same bubble slot where possible (D9).</summary>
public ValidationResult TryRestore(Place place, out PersistenceResult persistence)
```

`Remove`: D3 guard, then `removed = false` and `Ok` if the place is not in `_places` or is already deleted. Otherwise set `DeletedAt = _time.GetUtcNow()`, renumber the active favourites, and `Persist()`. Under D1, a failed save leaves the place deleted in memory, with the banner up.

`TryRestore`, in order:

1. D3 guard.
2. Not in `_places` (purged, or permanently deleted): fail with "\"X\" is no longer in Recently Deleted."
3. `DeletedAt` is null: fail with today's "already back in the list" message.
4. Alias or destination held by an active record: fail with today's two messages, which the existing tests match on "alias" and "path/URL". The record stays deleted.
5. Clear `DeletedAt`. If `IsFavourite`, shift active favourites at or after the remembered slot right by one, then `RenumberFavourites()`.
6. `Persist()`. This is a forward change, not a rollback (2026-09-25 hand-off §5).

The step 5 shift runs over active favourites only. Unlike today, the restored record is already in the list, so it is excluded by reference, as today's loop already does.

`MainViewModel`:

- `Stack<Place> _undoStack` replaces `_removedPlaces`; `UndoRemoveCommand` is unchanged in name and binding.
- `Remove`: no `MessageForm`. Call `Remove(place.Model, out var removed)`, then `RefreshPersistenceState(persistence)`. If removed, remove the view model, `RebuildFavourites()`, push, and `ShowStatus($"Moved \"{alias}\" to Recently Deleted.", offerUndo: true)`.
- `UndoRemove`: pop, then `TryRestore`. On success, insert via `InsertRestored(place)` (5.3 #23), refresh the favourite view models, `ClearSearchIfHidden`, and show the status as today. On failure, if `GetRestoreConflict(place)` is not null, run the D15 flow (`PlaceFormDialog.ShowRestore`) and insert on success. If the dialog is cancelled or there is no conflict (purged, or already restored), show the reason and say that the place remains in Recently Deleted when it does. The entry is dropped in every case, for today's reason: keeping it would jam Ctrl+Z.
- `ShowStatus(string? message, bool offerUndo = false)`: interval 10 s when `offerUndo`, else 8 s. New `PauseStatusTimer()` and `ResumeStatusTimer()`, called from `MainWindow.xaml.cs` on the status border's `MouseEnter`/`MouseLeave` and `IsKeyboardFocusWithinChanged`. Resuming restarts the full interval.
- `PruneUndoStack()`: rebuilds the stack keeping only places that are still in `RecentlyDeleted`. It runs after the Recently Deleted dialog closes, so Ctrl+Z never tries to restore something the dialog already restored or deleted.

`MainWindow.xaml`: the grid context menu's Remove item gains `InputGestureText="Delete"` and `ToolTip="Moves this place to Recently Deleted. Ctrl+Z puts it back, or restore it from Recently Deleted within 7 days."`. The label stays **Remove** (§4.8). The Ctrl+Z `KeyBinding` is unchanged.

`MainWindow.xaml.cs`: after the Delete key removes a row in `PlacesGrid_PreviewKeyDown`, call `FocusGridRow(previousIndex)`. Without the confirmation dialog, nothing hands focus back, and the next Delete or arrow key would otherwise go nowhere.

The confirmation dialog and the tooltip change land together with the Recently Deleted dialog (section 10, step 8), not with the soft delete itself. Until then the confirmation's premise ("you can undo…") is still true, and dropping it before a user can reach Recently Deleted would remove a guard before its replacement exists.

### 5.5 Recently Deleted dialog (roadmap §4.9)

`RecentlyDeletedDialog`, modal, owned by the main window, styled like `ExportDialog` and `ImportDialog`:

```csharp
/// <summary>Shows the dialog. Returns the places restored (for MainViewModel to insert), in restore order.</summary>
public static IReadOnlyList<Place> Show(PlacesService placesService)
```

- **Grid:** `SelectionMode="Extended"`, read-only. Columns: **Alias** (with the type glyph, as in the main grid), **Type**, **Path / URL**, **Deleted** (`DeletedAt.ToLocalTime()`, format `g`), **Days remaining** (`RecentlyDeletedPolicy.DaysRemainingText`, tooltip giving the exact expiry in local time). Default sort is Deleted, descending.
- Rows are `RecentlyDeletedRowViewModel(Place place, DateTimeOffset now)`, rebuilt from `placesService.RecentlyDeleted` after every action, with `now = placesService.UtcNow`.
- **Empty state:** "Nothing here. Places you remove stay here for 7 days." The action buttons are disabled.
- **Buttons**, with access keys: **_Restore selected**, **_Delete selected permanently**, **_Empty Recently Deleted**, and **Close** (`IsDefault` and `IsCancel`). No row action is bound to Enter or Delete, so nothing irreversible is one keystroke away.
- **Restore selected** calls `RestoreSelected`. Then, for each returned conflict in order, it runs `PlaceFormDialog.ShowRestore(conflict.Place, conflict.Explanation, placesService)`.
- **Delete selected permanently** asks `ShowDestructiveConfirm("Permanently delete {n} place(s)? This can't be undone.", …, "Delete permanently")`, then calls `DeletePermanently`.
- **Empty Recently Deleted** asks `ShowDestructiveConfirm("Permanently delete all {n} place(s) in Recently Deleted? This can't be undone.", …, "Empty Recently Deleted")`, then calls `EmptyRecentlyDeleted`.
- A failed save shows its `UserMessage` in the dialog's own error line, so the user is not left guessing behind a modal. The banner is still driven only by `HasUnsavedChanges`: `MainViewModel` calls `RefreshPersistenceState()` after the dialog closes, as it already does after the Import dialog.

`PlacesService` additions:

```csharp
public IReadOnlyList<Place> RecentlyDeleted { get; }

/// <summary>Restores the non-conflicting places, newest deletion first (D22), in one save. Conflicts are returned for the D15 flow, not skipped.</summary>
public (List<Place> restored, List<RestoreConflict> conflicts, PersistenceResult persistence) RestoreSelected(IEnumerable<Place> selected)

/// <summary>Removes the given places from the store for good. Ignores any that are not in Recently Deleted. One save, or none if nothing changed.</summary>
public PersistenceResult DeletePermanently(IEnumerable<Place> selected)

/// <summary>Removes every place in Recently Deleted for good. One save, or none if it was already empty.</summary>
public PersistenceResult EmptyRecentlyDeleted()
```

All three check the D3 guard first and make no change while it blocks. None of them has a `Try*` name, because none is a single validated edit; they follow `CommitImport`'s tuple shape. The single edited restore is a `Try*` and follows the standard pattern:

```csharp
/// <summary>Restores <paramref name="place"/> under a new alias and/or destination — the D15 conflict flow's commit. Validation as for Add; a failure changes nothing and leaves it deleted.</summary>
public ValidationResult TryRestore(Place place, string alias, string resource, out PersistenceResult persistence)

/// <summary>What stops <paramref name="place"/> being restored as it is, or null if nothing does.</summary>
public RestoreConflict? GetRestoreConflict(Place place)
```

```csharp
/// <summary>A deleted place whose alias and/or destination is now used by an active place (§4.10).</summary>
public sealed record RestoreConflict(Place Place, Place? AliasHeldBy, Place? ResourceHeldBy)
{
    /// <summary>
    /// One or two sentences for the restore dialog, e.g. "\"Docs\" can't be restored as it was:
    /// another place is now called \"docs\", and \"Projects\" now uses its folder path. Change
    /// them below, then restore." Shown to the user; never logged.
    /// </summary>
    public string Explanation { get; }
}
```

`PlaceFormDialog` gains `PlaceFormMode.Restore` and a static `ShowRestore(Place deleted, string explanation, PlacesService placesService)`, returning `bool`. It is titled "Restore Place", shows an explanation `TextBlock` (collapsed in every other mode) above the alias and destination fields, both prefilled. **Browse...** is shown for a folder, the OK button reads **Restore**, and it commits through `TryRestore(place, alias, resource, out _)`, discarding the persistence result as the other modes do.

`MainViewModel` gains `ShowRecentlyDeletedCommand`. It shows the dialog, inserts each returned place with `InsertRestored`, then calls `RebuildFavourites()`, `RefreshPersistenceState()` and `PruneUndoStack()`. `MainWindow.xaml` adds the header icon button (D20) bound to it.

### 5.6 Expiry and purge (roadmap §4.10)

```csharp
public static class RecentlyDeletedPolicy
{
    public static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(7);
    public static DateTimeOffset ExpiresAt(DateTimeOffset deletedAt) => deletedAt + RetentionPeriod;
    public static bool IsExpired(DateTimeOffset deletedAt, DateTimeOffset now) => now >= ExpiresAt(deletedAt);

    /// <summary>Ceiling of the whole days left, clamped to 0..7 (D13).</summary>
    public static int DaysRemaining(DateTimeOffset deletedAt, DateTimeOffset now);

    /// <summary>"7 days" … "2 days", "1 day", or "Expiring" once past expiry and awaiting the next purge.</summary>
    public static string DaysRemainingText(DateTimeOffset deletedAt, DateTimeOffset now);
}
```

`PlacesService.PurgeExpired(string trigger)` is private and returns the count removed. It returns 0 without looking when `IsRecoveryUnresolved`. Otherwise it removes every record whose `DeletedAt` is expired at `_time.GetUtcNow()`, and logs "Purged {n} place(s) from Recently Deleted after seven days ({trigger})." when `n > 0`. It is called from the constructor and from `Reload()` when the outcome is `Ok` (trigger "after load"), and at the top of `Persist()` (trigger "before save"). Nowhere else (D14).

### 5.7 Export and import (roadmap §4.7, §4.10; D16, D17)

- `Export(IEnumerable<Place> places, string filePath)` filters to active and writes `schemaVersion = CurrentSchemaVersion`. The existing atomic write is unchanged.
- `GetImportCandidates(string filePath)` becomes: read the text, then `JsonNode.Parse`. Read `schemaVersion` per D17's table, migrating version 1 with `PlacesStoreMigration` (zone and "now" from the clock). Deserialise, drop null entries and records with `DeletedAt`, then run today's validation and self-duplicate filtering. The existing broad `catch` still turns any failure into "Couldn't read that file: …".
- `CommitImport` is unchanged apart from the clock (5.2) and active-only validation.

## 6. Constraints this plan checks itself against

Each one maps to where the plan honours it; none is restated.

| Constraint (source) | Honoured by |
|---|---|
| D1 — a failed save keeps the change (BUILD_SUMMARY) | 5.4 `Remove`/`TryRestore`, 5.5 batch operations; tests 31, 54 |
| D2 — whole-store writes | D7, D14, D22 |
| D3 — unresolved recovery refuses mutations | Every new mutation's first line; `PurgeExpired`'s early return; tests 32, 44, 53 |
| D5 — no test constructs a `Window`; linked files stay free of `System.Windows.*` | D21; only `PlacesStoreMigration.cs` and `RecentlyDeletedPolicy.cs` are newly linked |
| D6 — classify at the catch | D11's `JsonException` contract |
| The banner reads only `HasUnsavedChanges` (hand-off 2026-09-21 §5) | 5.5's dialog error line is separate; the view model refreshes from the service after the dialog |
| `Try*` returns `ValidationResult` with `out PersistenceResult` | Both `TryRestore` overloads |
| `TryRestore` is a forward change (hand-off 2026-09-25 §5) | 5.4 step 6 |
| Migration written only after a successful save (§4.3) | D11, D14; tests 13, 15 |
| `places.v1.json` frozen | Test 11 changes its assertions, not the file |
| UTC `DateTimeOffset` for every new persisted timestamp (§3) | `DateAdded`, `DeletedAt` |
| The log never records aliases or destinations | D11 and D14 log counts only; tests 21, 46 |
| `AppSettings.CurrentSchemaVersion` stays at least 2 | Untouched |

## 7. Test plan

`src/QuickerPlaces.Tests`, xUnit, on `FakePlacesStorage` unless the "Set-up" column says `TempDirectory`. New fakes:

- `ManualTimeProvider : TimeProvider`, with a settable `UtcNow`, `Advance(TimeSpan)`, and a settable `LocalTimeZone` (default `TestZones.PlusTen`).
- `TestZones`, with `PlusTen` and `MinusFive` (fixed offsets, built with `TimeZoneInfo.CreateCustomTimeZone`) and `CentralEuropean`, a custom zone at +01:00 with a +1 h adjustment rule from the last Sunday of March 02:00 to the last Sunday of October 03:00. Built rather than looked up, so no test depends on the host's tz database.

Folder paths in inline seeds and import files go through `TestPaths.Folder`, because `C:\…` is not a fully qualified path on Linux and validation would reject it. The two fixtures keep their Windows paths, because loading the store never validates destinations, and that is why the import tests (34, 35) use inline documents rather than the fixture files.

New classes: `PlacesStoreMigrationTests`, `PlacesServiceSchemaV2Tests`, `PlacesServiceSoftDeleteTests`, `PlacesServiceRecentlyDeletedTests`, `PlacesServicePurgeTests`, `RecentlyDeletedPolicyTests`. Export and import cases extend `PlacesServiceTests`' existing section. The two log tests join `DiagnosticLogTests`' non-parallel collection and use its own-folder pattern.

| # | Test | Proves | Set-up |
|---|---|---|---|
| 1 | The quarantine file is named from the injected clock's local time | D12, and proves the seam is wired | Damaged store, `QuarantineAndStartEmpty`, `FakePlacesStorage.QuarantinedPath` checked against a fixed instant in `PlusTen` |
| 2 | Offset-less `dateAdded` is read as local time in the given zone | §4.7 assumption | `"2026-01-15T09:30:00"`, `PlusTen` → `2026-01-14T23:30:00Z` |
| 3 | An offset-bearing value converts exactly, whatever the zone | Finding 1, section 1 | `"2026-01-15T09:30:00.1234567+10:00"` under `MinusFive` → `…23:30:00.1234567Z` |
| 4 | A `Z` value keeps its instant | D11 | `"…Z"` under `PlusTen` |
| 5 | A time in the spring-forward gap uses the standard offset and does not throw | D11 | `CentralEuropean`, `2026-03-29T02:30:00` → `01:30Z` |
| 6 | A time in the ambiguous fall-back hour uses the standard offset | D11 | `CentralEuropean`, `2026-10-25T02:30:00` → `01:30Z` |
| 7 | A missing `dateAdded` becomes the clock's now | D11 | Record without the property |
| 8 | A non-date `dateAdded` throws `JsonException` | D11 → D6 | `5`, `"yesterday"` |
| 9 | Output is `schemaVersion` 2, has no `deletedAt`, and skips null entries | D11 | `[null, {…}]` |
| 10 | The report counts exact, interpreted, and missing values, and names the zone | D11 log content | One of each |
| 11 | `places.v1.json` loads migrated, every field intact, `DateAdded` in UTC; the fixture file is byte-identical | No regression for v1 users | Existing fixture test, re-asserted under `PlusTen` (e.g. Downloads → `2026-01-14T23:30:00Z`) |
| 12 | `places.v2.json` loads with every field intact: 3 active, 2 deleted, `DeletedAt` and remembered favourite slots kept; `Places` excludes deleted; `RecentlyDeleted` is newest first | The v2 shape | New frozen fixture; clock 2026-09-25, before any expiry |
| 13 | Loading a v1 store writes nothing, and the stored text is unchanged | §4.3 | `FailEveryWrite`; `WriteCount == 0` |
| 14 | Migration happens once: save under `PlusTen`, reload under `MinusFive`, and every `DateAdded` is unchanged | §5 "converts once" | Load v1, `TryAdd`, new service on the same storage with a different zone |
| 15 | A failed first save leaves v1 on disk, and the next launch migrates the same values again | D11 | `FailEveryWrite`, then a new service |
| 16 | Version 2 loads without migrating; 3 is `WrittenByNewerVersion` and untouched; 0 and −1 are `Damaged` | Gate at 2 | Inline JSON; `TempDirectory` for "untouched" |
| 17 | A v1 store with an unmigratable date is `Damaged`, never written, never quarantined | "Failed to migrate" (§4.10) | `TempDirectory`, `"dateAdded": 5` |
| 18 | Deleted records do not block Add, Rename, Edit or Import on alias or destination | §4.10 | Seed a v2 store with a deleted "Docs" |
| 19 | Favouriting appends after *active* favourites only | 5.3 #7 | Deleted favourite remembered at slot 0 |
| 20 | Rename and Edit refuse a deleted record; toggling it writes nothing | 5.3 #6–7 | Seeded deleted record |
| 21 | The migration log records the assumption and the zone id, never an alias or destination | §4.7, log privacy | `DiagnosticLogTests` collection; v1 store with a known alias and an offset-less date |
| 22 | `TryAdd` stamps `DateAdded` from the injected clock; `DeletedAt` and `DateAdded` round-trip as UTC; `deletedAt` is absent from JSON for active records | D12, D16 | `ManualTimeProvider` at a fixed instant; `LastWritten` inspected |
| 23 | Remove sets `DeletedAt` to the clock's now, hides the place from `Places`, lists it in `RecentlyDeleted`, and persists | §4.8 | Reload on the same `TempDirectory` |
| 24 | Removing a favourite renumbers the active ones dense and keeps the removed one's slot | D9 | Three favourites, remove the middle |
| 25 | Undo returns the *same instance* to the same list slot | §4.8 | Adapted `Restore_puts_a_removed_place_back_where_it_was_and_persists` |
| 26 | Restore after a restart returns to the same list slot and bubble slot | D7, D9 | `TempDirectory`; remove, new service, restore |
| 27 | The four existing favourite-restore tests pass unchanged apart from the `RemovedPlace` → `Place` swap | D9 matches today's rule | Adapted `Restore_*` tests |
| 28 | A conflicting restore is refused with the alias or path/URL message, and the place stays in Recently Deleted | D15 | Adapted `Restore_refuses_when_the_alias_or_resource_has_been_reused`, plus `RecentlyDeleted` asserted |
| 29 | A second restore is refused; restoring a purged or permanently deleted place says it is no longer in Recently Deleted | 5.4 steps 2–3 | Adapted `Restore_twice_…`, plus `DeletePermanently` first |
| 30 | Removing a place that is not in the store, or is already deleted, reports `removed == false` and writes nothing | 5.4 | Adapted `Removing_a_place_not_in_the_store_returns_null` |
| 31 | D1: a failed save after Remove leaves it deleted and unsaved, and Retry writes it; a failed save after Restore leaves it restored | D1 | Adapted `FailedRemove_…`, plus `FailNextWrite` on restore |
| 32 | Remove and both `TryRestore` overloads are refused while recovery is unresolved | D3 | Extend `MutationsAreRefused_WhileRecoveryIsUnresolved` |
| 33 | Export leaves out deleted records even when given them, writes `schemaVersion` 2, and has no `deletedAt` key | D16 | `TempDirectory`; pass `Places` plus `RecentlyDeleted` |
| 34 | A v1 export offers every place with UTC dates, and they commit | Old exports keep importing | Inline v1 document in `places.v1.json`'s exact shape, offset-less dates, folders from `TestPaths` |
| 35 | Records with `deletedAt` in an import file are not offered | D17 | Inline v2 document shaped like `places.v2.json`, folders from `TestPaths`: only the three active records are candidates |
| 36 | A candidate that collides only with a deleted record is offered and commits | §4.10, D17 | Deleted "Docs" in the store, active "Docs" in the file |
| 37 | An import file with `schemaVersion` 3 is refused with the newer-version message and no candidates | D17 | Inline file |
| 38 | An import file without `schemaVersion` still imports | D17 leniency | Inline file |
| 39 | The existing export-then-import round trip still passes, and the export reads as version 2 | No regression | `Export_then_import_into_empty_store_round_trips` plus one assertion |
| 40 | `IsExpired` is false at *D* + 7 d − 1 tick and true at exactly *D* + 7 d; days remaining are 7, 7, 1, 0 at *D*, *D* + 1 tick, *D* + 6 d + 1 tick, *D* + 7 d; a future `DeletedAt` clamps to 7; texts read "7 days", "1 day", "Expiring" | D13 | Pure, no service |
| 41 | Startup purge: a record deleted exactly 7 d before now is gone; one deleted 7 d − 1 s before now remains; nothing is written | D14 point 1 | Seeded v2 store; `WriteCount == 0` |
| 42 | Purge before a save: advance the clock past expiry, then `TryAdd`, and the written JSON lacks the expired record | D14 point 2 | `LastWritten` |
| 43 | `RetrySave` purges too | D14 | `FailNextWrite` on the add, advance, `RetrySave` |
| 44 | No purge from a store that failed to load: `Damaged`, `Unreadable` and `WrittenByNewerVersion` files with expired records stay byte-identical, with no writes | §4.10 | `TempDirectory`, one per outcome (`FileShare.None` for `Unreadable`) |
| 45 | A successful `Reload()` purges like a startup | D14 | Read throws `IOException`, then clears, then `Reload()` |
| 46 | The purge log records a count, never an alias or destination | Log privacy | `DiagnosticLogTests` collection |
| 47 | `GetRestoreConflict` is null when free, and otherwise names the alias holder, the destination holder, or both; `Explanation` mentions each holder | D15 | Three set-ups |
| 48 | `TryRestore` with an edited alias and destination restores the record with the new values, in one write | D15 commit | Conflicting "Docs" restored as "Docs (old)" |
| 49 | `TryRestore` with edits that still conflict, or a malformed path, fails validation and changes nothing, leaving the record deleted with no write | D15 | Two cases |
| 50 | `RestoreSelected` restores the non-conflicting records in one write and returns the conflicts; with two deleted "Docs", the newer deletion wins | D22 | Clock advanced between the two removals |
| 51 | `DeletePermanently` removes only the deleted records it is given, ignores active ones, writes once, and they are gone after a reload | §4.9 | `TempDirectory` |
| 52 | `EmptyRecentlyDeleted` removes every deleted record and keeps the active ones; it writes nothing when already empty | §4.9 | `WriteCount` |
| 53 | `RestoreSelected`, `DeletePermanently`, `EmptyRecentlyDeleted` and the edited `TryRestore` are refused while recovery is unresolved | D3 | Damaged store |
| 54 | A failed save after `DeletePermanently` leaves the records gone from memory and the store unsaved; `RetrySave` writes | D1 | `FailNextWrite` |

`places.v2.json` has five records. Active: "Downloads" (favourite, slot 0), "QuickerPlaces Repo" (URL, favourite, slot 1), and "reports" (folder `D:\Reports`, not a favourite). Deleted: "Old Wiki" (URL, favourite, remembered slot 1, deleted 2026-09-20T08:00:00+00:00) and "Reports" (folder, deleted 2026-09-24T17:45:00+00:00). The deleted "Reports" shares its alias, case-insensitively, with the active "reports". That proves a v2 file may legitimately hold such a pair, and it gives the conflict tests a realistic seed. All `dateAdded` values end in `+00:00`, as this build writes them. The fixture is frozen once committed, like its v1 sibling.

Existing tests changed by this phase, and why:

- `PlacesStoreFixtureTests.FixtureFile_LoadsWithEveryFieldIntact`: its `DateTime` assertions become UTC `DateTimeOffset` under an injected zone (test 11). The fixture file does not change.
- `PlacesServiceTests`: `RemoveForUndo` returns the `Place` (D10), and the `Restore_*` and `Removing_*` tests adapt (tests 25–30).
- `PlacesServicePersistenceTests`: `FailedRemove_…` and `MutationsAreRefused_…` extend (tests 31, 32). Their `Remove(…, out _)` calls compile unchanged.
- `PlacesServiceLoadOutcomeTests.LoadingAStore_NeverWritesToDiskUntilASaveSucceeds`: its comment ("nothing to migrate") is corrected. The migrating case is test 13.

## 8. Manual verification (Windows)

Recorded in the phase's closing commit message, as for Phase 1. Items that cannot be tested are recorded as untested, not skipped.

- [ ] **Upgrade a real file.** Copy a `places.json` written by a pre-Phase-2 build into place. Launch: places, favourites and the Date Added column (local date) are intact. The file is still `schemaVersion` 1 on disk until the first change. After one change it is 2, with `+00:00` dates, and `places.bak.json` is the v1 file. The log has the migration line with counts and a zone id, and no alias.
- [ ] **Downgrade refusal.** Start a pre-Phase-2 build against the v2 file: the newer-version prompt appears, and the file is byte-identical afterwards.
- [ ] **Remove without asking.** From the context menu and with Delete: no dialog, the row and its bubble disappear, and the status bar says "Moved … to Recently Deleted." with **Undo**. The Remove item shows its tooltip and "Delete" as its gesture.
- [ ] **Focus.** After Delete, the next row is selected and focused, and Delete again removes it. The status bar never takes focus when it appears.
- [ ] **Timing.** The bar stays about 10 seconds; hovering over it pauses the countdown, and leaving restarts it; tabbing into it pauses it. Ctrl+Z after the bar has gone still restores.
- [ ] **Favourite slot.** Undo of a removed favourite puts its bubble back in the same position. The same holds after closing and reopening the app and restoring from Recently Deleted.
- [ ] **Conflict.** Remove "Docs", add a new "Docs", press Ctrl+Z: the Restore Place dialog explains the conflict. Edit the alias and restore. Repeat and cancel: the old one is still in Recently Deleted.
- [ ] **Dialog.** Open it from the header icon: the columns, a local deleted date, and days remaining. Multi-select, **Restore selected** (including a conflict), **Delete selected permanently**, and **Empty Recently Deleted**. Both confirmations default to Cancel, so pressing Enter cancels. The empty-state text shows. The whole dialog works from the keyboard alone.
- [ ] **Expiry.** With the app closed, edit a `deletedAt` in `places.json` to eight days ago, then launch: that record is not in the dialog, the log records one purge "after load", and the file still holds it until the next change.
- [ ] **Failures.** Deny write permission on `places.json`. A Remove shows the banner; so does an Undo; Retry clears it once permission returns. A permanent delete in the dialog shows the error line in the dialog, and the banner after closing it.
- [ ] **Header fit.** At the 700 px minimum width, and on a high-DPI display, the header's new icon button fits, and a screen reader announces "Recently Deleted".

## 9. User guide changes (`USERGUIDE.md`)

- **The window, at a glance:** "six buttons" becomes seven, adding the Recently Deleted (bin) icon beside the gear and folder icons.
- **Working with a place in the grid:** rewrite the **Remove** bullet. There is no question first; the place moves to Recently Deleted for seven days; the bar's **Undo** (about 10 seconds) and Ctrl+Z (all session) put it back exactly where it was, bubble slot included; if its alias or path/URL has been reused since, you are shown why and can change them before restoring.
- **New section "Recently Deleted"**, after Favourites. What it holds and for how long, precisely: at least seven full days (7 × 24 hours from the moment you removed it), then removed the next time QuickerPlaces starts or saves. The columns, the three actions, and which two cannot be undone and ask first. Restore conflicts. That removed places never block an alias or path/URL you want to reuse.
- **Keyboard shortcuts:** the Ctrl+Z row reads "Undo the last Remove (repeat for earlier ones, this session)"; the Delete row reads "Remove (moves it to Recently Deleted)", no longer "(asks first)".
- **The unsaved-changes banner:** add restoring and permanently deleting to the list of changes it covers.
- **Exporting places:** removed places are never exported.
- **Importing places:** removed places in a file are skipped; a place whose name matches only something in your Recently Deleted is still offered; exports from older versions still import; files from a newer version are refused, with a message.
- **Where your data lives:** `places.json` also holds Recently Deleted. The first save after upgrading converts it to the new format. Older versions of QuickerPlaces then refuse to open it, and cannot damage it. Right after that first save, `places.bak.json` is the pre-upgrade copy.
- **If something goes wrong:** add "Removed something by mistake: Ctrl+Z, or Recently Deleted for seven days."

## 10. Order of work

Each step builds (`-p:EnableWindowsTargeting=true` for the app) and leaves the suite green before the next begins. This is the intended commit sequence.

1. **Inject the clock; no behaviour change.** `TimeProvider` constructor parameter and `UtcNow`; the quarantine timestamp from `_time.GetLocalNow()`. `ManualTimeProvider`, `TestZones`. Test 1. `DateAdded` keeps `DateTime.Now` until step 3 changes its type: stamping a `DateTime` from the clock here would need `GetLocalNow().DateTime`, whose `Unspecified` kind would quietly start writing offset-less dates.
2. **The migration, pure and not yet called.** `PlacesStoreMigration`, `MigrationReport`, linked into the test project. Tests 2–10. Nothing loads through it yet, so the step is provably inert.
3. **Schema v2: UTC dates, `DeletedAt`, and an active/deleted split on the read side.** `Place` and `PlacesStore` changes, `TryAdd` and `CommitImport` stamping `DateAdded` from `_time.GetUtcNow()`, `CurrentSchemaVersion = 2`, the load wiring and version table, normalisation, `Places` as active, `RecentlyDeleted`, `PlaceViewModel.DateAdded`, and rows 3, 4, 6, 7, 8, 11, 15, 17 of 5.3. `places.v2.json` and its csproj copy item. Tests 11–22. `Remove` still hard-deletes at the end of this step, deliberately, so the schema change is proven on its own.
4. **Soft delete and un-delete.** `Remove(…, out bool)`, `TryRestore` rewritten, `RemovedPlace` removed, `MainViewModel`'s Undo stack and insert-by-active-index, and the conflict branch of Undo showing today's message (the edit flow arrives in step 7). Tests 23–32, and the adapted existing tests. The Remove confirmation is still in place.
5. **Export and import rules.** 5.7. Tests 33–39.
6. **Expiry.** `RecentlyDeletedPolicy`, `PurgeExpired`, and its three call sites. Tests 40–46. Write 44 before 41–43: it is what keeps a purge from ever reaching a file that did not load, and it should exist before any purge code can.
7. **Restore conflicts and permanent deletion, in the service.** `RestoreConflict`, `GetRestoreConflict`, the edited `TryRestore`, `RestoreSelected`, `DeletePermanently`, `EmptyRecentlyDeleted`. Tests 47–54.
8. **The UI.** `RecentlyDeletedDialog` and its row view model; `PlaceFormDialog`'s Restore mode, which Undo's conflict branch now uses; `MessageForm.ShowDestructiveConfirm`; the header button; dropping the Remove confirmation, with the tooltip and gesture text; status timings and pause; focus after Delete; the empty-grid hint; `PruneUndoStack`. Compile-checked on Linux; manual checklist on Windows.
9. **Documentation.** `USERGUIDE.md` (section 9). Append Phase 2 to `BUILD_SUMMARY.md`, recording the manual-checklist results. Update the status line of this plan, the roadmap's status and "Detailed plans" lines, and `ai/README.md`. The Phase 3 detailed plan follows once the section 8 checklist has been walked, since until then Phase 2 has not landed (`ai/README.md`, "Working on a phase" step 4).

Commit messages follow the repository's style: what changed and why, with the platform-specific reason spelled out where one drove the decision (for example, why the migration parses the JSON before binding).

## 11. Phase 2 definition of done

- A removed place leaves the grid and the bubbles at once, with no confirmation, and can be brought back exactly (same record, list slot, and bubble slot where possible) by Undo in the session and from Recently Deleted for at least seven full days, including across restarts.
- A restore that conflicts with an active place is explained and editable, never silently skipped or discarded.
- Permanent deletion is the only irreversible step; it always asks, and Enter never confirms it.
- Expired records are purged only after a successful load or before a save, never from a store that failed to load or migrate, and a purge never writes on its own.
- An existing v1 `places.json` migrates to v2 without loss, `DateAdded` is converted to UTC exactly once, the assumption is logged without aliases or destinations, and nothing is written until a save succeeds. Older builds refuse the v2 file without touching it.
- Old exports still import; deleted records are never exported and never imported.
- All tests in section 7 pass alongside the existing suite, the section 8 checklist has been walked on Windows, and the user guide and `BUILD_SUMMARY.md` are updated.

## 12. Open questions

1. **Entry point.** D20 adds a third icon button because the "secondary menu" §4.9 names does not exist. When Phase 4 replaces Add Folder and Add URL with one **Add...** control, should Settings, Open data folder, Recently Deleted, Import and Export move into a single "More" menu? Recommendation: decide in the Phase 4 plan, with the header's width at 700 px in front of you.
2. **A lasting pre-migration copy.** `places.bak.json` is the v1 file only until the second save after upgrading. Should the first v2 save also keep a one-time `places.schema1.json` that nothing ever overwrites, as a downgrade path? It would need a new `IPlacesStorage` operation, and a file the user never asked for. Recommendation: no, unless someone actually needs to go back to a pre-Phase-2 build; the refusal prompt already protects the data.
3. **Showing how many places are in Recently Deleted.** A count on the icon's tooltip ("Recently Deleted (3)") would aid discovery at the cost of a property to keep in step. Recommendation: not in this phase; the Remove status message and the empty-grid hint already point there.
4. **Import metadata (for Phase 4).** The 2026-09-21 hand-off's decision 6 records that import keeps metadata, but `CommitImport` still stamps `DateAdded` with now and drops favourite state. Phase 2 leaves that alone (section 2). The Phase 4 plan should say whether a restored-from-export place keeps its original `DateAdded`, now that it is a UTC value that survives the round trip.

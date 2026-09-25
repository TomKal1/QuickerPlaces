---
title: QuickerPlaces — Hand-off, 2026-09-25 (Phase 2)
status: current as of 2026-09-25 — supersede with a new dated hand-off rather than editing this one
created: 2026-09-25
parent: ai/260901_Professional Improvements Plan.md
supersedes: ai/260925_Handoff.md
---

# Hand-off — where to pick up after Phase 2

## 1. Start here

**Work on `claude/roadmap-status-4tv9nf`.** It is `main` (`ca0ac72`, the Phase 1 merge) plus eleven commits:

- the Phase 2 plan (`19dfdb6`);
- Phase 2's steps 1 to 8 (`ab43db7` to `cbde92f`);
- the step 9 documentation commit that added this file.

**It is not merged to `main`, and no pull request has been opened.** The documentation commit was made locally, and pushing is left to the user. Check `git status` against the remote before relying on either.

This supersedes the earlier 2026-09-25 hand-off's "work from `main`". That hand-off's §4 decisions and §5 constraints still apply, and so do the 2026-09-21 hand-off's §5 and §6.

## 2. State of the project

- **Phase 2 (seven-day Recently Deleted) is implemented**, following [its plan](260925_Phase%202%20Detailed%20Plan.md). `BUILD_SUMMARY.md` § "Phase 2" records what was built, every departure from the plan, and the bugs found along the way.
- **Tests:** all 237 pass on Linux, up from 125.
- **The WPF app:** compiles on Linux with 0 warnings, but **has not been run**. Nothing added in step 8 has been seen on screen.
- **It was implemented ahead of the Phase 1 manual checklist, at the user's explicit request.** The 2026-09-21 hand-off's decision 4 said that checklist should gate Phase 2, because Phase 2 builds on save, recovery and banner paths that only the checklist proves in the running app. That risk now applies to both phases at once.
- **Neither manual checklist has been walked on Windows.** Phase 1's is in `BUILD_SUMMARY.md` § "Phase 1"; Phase 2's is in § "Phase 2", and merges the plan's section 8 with the UI checks listed by step 8.
- **Phases 3 to 8 are unstarted.** Phase 7 is partly covered by the feature work (see the earlier 2026-09-25 hand-off, §2). Phase 9 is designed, not started, and deliberately last.
- **The Phase 3 detailed plan was deliberately not written.** Phase 2 has not landed until its checklist is walked (`ai/README.md`, "Working on a phase", step 4), and a plan written on top of an unverified phase might have to change with it.

## 3. Next steps, in order

1. **Walk the Phase 1 manual checklist, then Phase 2's, on Windows.** Record each result against its item in `BUILD_SUMMARY.md`. Anything that cannot be tested is recorded as untested, not skipped. Two items need a specific answer, not just a pass:
   - whether a plain letter fires an access key in the Recently Deleted dialog (§6, question 6);
   - whether Enter closes that dialog when its grid has focus.
2. **Fix anything they find**, with a test wherever the fix is below the UI.
3. **Merge to `main`** through a pull request.
4. **Write the Phase 3 detailed plan** (roadmap §4.11–4.14, usage tracking and sorting) against the code as it then is. §5 below lists what it inherits.
5. **Implement Phase 3, then 4 → 6 → 7**, each preceded by its own detailed plan, **then Phase 8's** clean-machine verification, which completes Release 1.

## 4. Decisions made on 2026-09-25, beyond the Phase 2 plan

The full list, with the commit behind each, is the table in `BUILD_SUMMARY.md` § "Phase 2", "Where the build departs from the plan". These are the ones a successor is most likely to trip over.

| # | Decision | Reasoning |
|---|---|---|
| 1 | **Phase 2 was implemented before the Phase 1 checklist**, overriding the 2026-09-21 hand-off's decision 4 for this phase. | The user asked for it explicitly. The gate's reasoning still holds, which is why step 1 above walks Phase 1's checklist first. |
| 2 | **The dialog's logic lives in `RecentlyDeletedViewModel`,** which was not in the plan. It is linked into the tests with `RecentlyDeletedRowViewModel`, `PlaceViewModel` and `ObservableObject`. | Every decision the dialog makes is then tested without a `Window` (D5, D21). The cost is that those files, `PlaceViewModel` included, must stay free of `System.Windows.*`. |
| 3 | **Duplicate JSON property names are refused at parse time**, for the store (`Damaged`) and for import (refused). | `JsonObject` throws `ArgumentException` on a duplicate key, which D6's catch does not classify, so such a file would crash the launch. |
| 4 | **The migration reads dates through a `JsonElement`, and clamps an offset-less date at the calendar's edge.** A stray `deletedAt` in a v1 record is dropped. | `JsonValue.TryGetValue<DateTime>` refuses a string node built in code. `new DateTimeOffset(...)` throws past D6 for `0001-01-01` east of Greenwich. v1 never gave `deletedAt` a meaning. |
| 5 | **Import also refuses a version below 1, and a non-object root,** as "not a QuickerPlaces export". | Beyond D17's table: no build wrote such a version, and the store's gate already treats it as unknown. |
| 6 | **`ShowRestore` takes the `RestoreConflict`**, starts in the field that is in the way with its text selected, and can be owned by Recently Deleted. Restore selected re-reads each conflict just before its form opens. | It needs to know *which* field conflicts, and an earlier edit in the same batch can change the answer. |
| 7 | **Cancelling Undo's conflict flow reports through the status bar**, not a second modal. | The reason was on screen a moment before. |
| 8 | **The theme's two button templates recognise access keys, app-wide.** | Needed for "_Restore selected". From now on an underscore in any button label becomes an access key. |
| 9 | **Plan 5.1's "every build before this phase refuses the v2 file" is corrected**: only builds with Phase 1's version gate (`main` from `ca0ac72`) refuse it. | Earlier builds never read `schemaVersion`. They would show deleted places as ordinary ones and write the file back as v1 without the flag. The user guide warns against going back to one. Nothing in Phase 2 can change an old build. |

## 5. Constraints and traps Phase 3 inherits

Everything in the earlier hand-offs' constraint and trap sections still holds. Phase 2 adds:

- **`PlacesService.Places` is the active places only, and a fresh snapshot per call (D8).** Read it once. Never index it inside a loop over the service, and never expect two calls to return the same list instance.
- **Every enumeration of `_places` must decide: active or deleted (D7).** Deleted records stay in the list, in their slots, with `DeletedAt` set. The private `Active` sequence is the usual answer, and the plan's table 5.3 is the precedent. Phase 3's "record an open" must refuse or ignore a deleted record, and any new sort or count must not see one. A deleted record's `IsFavourite` and `FavouriteOrder` are a remembered bubble slot (D9); nothing else may read them.
- **`Persist()` purges before it writes (D14).** Any save can remove expired records from memory and from disk, including a save made only to record an open. Tests that save after moving the clock forward must expect that.
- **`TimeProvider` for every timestamp (D12).** `LastOpenedAt` must come from `_time.GetUtcNow()`, never `DateTime.Now` or `DateTimeOffset.UtcNow`, and be a UTC `DateTimeOffset` (roadmap §3). Tests use `ManualTimeProvider` and `TestZones`. No test may read the machine's zone: run the suite under a couple of `TZ` values after changing anything date-related.
- **The places schema is now 2.** Phase 3's `LastOpenedAt` and `OpenCount` make it 3, as a v2 → v3 migration on the same JSON-level path as `PlacesStoreMigration.MigrateV1ToV2`, before binding (D11). The load path must then chain v1 → v2 → v3. Import's version gate (D17) must also accept 2 and migrate it, and export must write 3. Loading must still never write, and the migrated store reaches disk with the next successful save. `AppSettings.CurrentSchemaVersion` (settings.json, also 2) is a separate number.
- **`places.v1.json` and `places.v2.json` are frozen and pinned by content hash** (`PlacesStoreFixtureTests`). Add a `places.v3.json` beside them. Never edit either one to make a test pass.
- **`MainViewModel.Places` must stay in stored order.** `InsertRestored` puts a restored row back by counting the rows before it in `PlacesService.Places`, which only works while the collection mirrors the stored active order. Phase 3's "remember sorting" (§4.14) must sort the view (`PlacesView`, an `ICollectionView`), never the collection.
- **The unsaved banner still reads only `HasUnsavedChanges`.** The Recently Deleted dialog has its own error line and does not set the banner; `MainViewModel` refreshes it after the dialog closes. A new dialog that saves should do the same.
- **Dates in the grid ignore the locale** (§6, question 5). A Last Opened column bound with a `StringFormat` will show en-US dates too, until `FrameworkElement.Language` is set.

## 6. Open questions

1. **Entry point** (Phase 2 plan §12, question 1): should Settings, Open data folder, Recently Deleted, Import and Export fold into one "More" menu when Phase 4 replaces the two Add buttons? Decide in the Phase 4 plan, at 700 px.
2. **A lasting pre-migration copy** (§12, question 2). `places.bak.json` holds the v1 file only until the second save after upgrading. The correction in §4, decision 9 makes this slightly weightier: a user who goes back to a pre-Phase-1 build is not protected by the refusal prompt. The recommendation is still no, unless someone needs it.
3. **A count on the bin icon's tooltip** (§12, question 3). Recommended against for now.
4. **Import metadata** (§12, question 4, for Phase 4): whether a place imported from an export keeps its original `DateAdded`, now that it is a UTC value that survives the round trip. Phase 3 adds `LastOpenedAt` and `OpenCount` to the same question (roadmap §4.18).
5. **Locale date formatting.** Nothing sets `FrameworkElement.Language`, so the main grid's `{0:d}` formats as en-US whatever the user's region, while the Recently Deleted dialog formats with `CultureInfo.CurrentCulture`. This predates Phase 2. The fix is small: set the language once at startup from the current culture. It belongs in whichever phase touches the grid's columns next, which is Phase 3.
6. **Plain-letter access keys.** WPF can fire an access key on a plain letter, without Alt, when the focused element doesn't take text input. With the Recently Deleted grid focused, R might restore the selection, which is reversible. D and E would only open a confirmation that defaults to Cancel. The checklist asks for the answer. If it happens, decide whether to keep it or to require Alt.

## 7. Environment notes

- **Cloud sessions (Linux):** the repository is at `/home/user/QuickerPlaces`. The .NET 10 SDK installs from Ubuntu's archive with `apt-get install dotnet-sdk-10.0` (10.0.112 on 2026-09-25). Microsoft's `dotnet-install` host is blocked by the network policy, so the script route does not work there. From `src/`:
  - `dotnet test QuickerPlaces.Tests/QuickerPlaces.Tests.csproj` runs the suite.
  - `dotnet build QuickerPlaces/QuickerPlaces.csproj -p:EnableWindowsTargeting=true` compile-checks the WPF app, XAML included. It cannot run it.
  - `TZ=<zone> dotnet test …` runs the suite under another zone.
- **Windows:** build and test from `src\` with `dotnet build QuickerPlaces.sln` and `dotnet test QuickerPlaces.sln`. The manual checklists need a person at a Windows desktop.
- Commits end with the attribution lines the session's instructions give.

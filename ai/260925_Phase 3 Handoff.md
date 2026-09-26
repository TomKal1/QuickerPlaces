---
title: QuickerPlaces — Hand-off, 2026-09-25 (Phase 3)
status: current as of 2026-09-25 — supersede with a new dated hand-off rather than editing this one
created: 2026-09-25
parent: ai/260901_Professional Improvements Plan.md
supersedes: ai/260925_Phase 2 Handoff.md
---

# Hand-off — where to pick up after Phase 3

## 1. Start here

**Work on `claude/phase-3-usage-tracking`.** It is `main` at `1c03e59` (Phase 2 merged, PR #6) plus these commits:

- the Phase 3 plan (`ecc090c`);
- steps 1 to 7 (`e250fbb` to `3e77208`);
- one fix from a self-review (`479388b`);
- the step 8 documentation commit that added this file.

**It has not been pushed, and no pull request exists.** Check `git status` against the remote before relying on either.

The earlier hand-offs' constraint and trap sections still apply: 2026-09-21 §5–§6, the first 2026-09-25 hand-off's §5, and the Phase 2 hand-off's §5, extended by §5 below.

## 2. State of the project

- **Phase 3 (usage tracking and sorting) is implemented**, following [its plan](260925_Phase%203%20Detailed%20Plan.md). `BUILD_SUMMARY.md` § "Phase 3" records what was built and every departure from the plan.
- **Tests:** all 311 pass on Windows, up from 238. The app builds with 0 warnings.
- **The WPF app has not been run.** Nothing added in step 7 has been seen on screen: the Last Opened and Opens columns, the sorting, the arrows, and the re-sort after an open. It wasn't launched on purpose. `places.json` is at a fixed `%AppData%` path with no override, and the first recorded open would have migrated the developer's real store to v3.
- **The manual checklist has not been walked.** It is the plan's section 8; results go in `BUILD_SUMMARY.md` § "Phase 3".
- **Phases 4 to 8 are unstarted**, and Phase 7 is partly covered by the earlier feature work. Phase 9 is designed and deliberately last. Its dependency on Phase 3 ("what a recorded open means") is now met by D24.

## 3. Next steps, in order

1. **Back up the real store, then walk the Phase 3 checklist on Windows.** Copy `%AppData%\QuickerPlaces\QuickerPlaces\places.json` somewhere first: the upgrade is one-way for every earlier build, which refuses a v3 file. Record each result against its item.
2. **Fix what it finds**, with a test wherever the fix is below the UI.
3. **Push, and merge to `main` through a pull request.**
4. **Write the Phase 4 detailed plan** (roadmap §4.15–4.18, general file support) against the code as it then is. §5 below lists what it inherits, and the Phase 3 plan's §12 questions 2 and 5 are addressed to it.
5. **Implement Phase 4, then 6 → 7**, each preceded by its own detailed plan, **then Phase 8's** clean-machine verification, which completes Release 1.

## 4. Decisions made on 2026-09-25, beyond the Phase 3 plan

The full list, with the commit behind each, is the table in `BUILD_SUMMARY.md` § "Phase 3", "Where the build departs from the plan". These are the ones a successor is most likely to trip over.

| # | Decision | Reasoning |
|---|---|---|
| 1 | **The migration writes `id` as the Guid's text**, not a Guid-typed `JsonValue`. | A `JsonValue` built in code from a `Guid` refuses `GetValue<string>`, which a parsed one accepts. The migrated tree should look exactly like a parsed v3 file. This is the second time this class of quirk has come up (Phase 2 hit it with dates). |
| 2 | **`PlaceType.Label()` is the one source of "Folder"/"URL".** | The type sort must compare exactly what the column shows, and a service shouldn't depend on a view model. Phase 4's `File` goes there. |
| 3 | **Settings sort keys are matched as exact names**, not through `Enum.TryParse`. | `TryParse` accepts numbers, padding and flag syntax. Only what `Format` writes should count. |
| 4 | **The `TZ` runs were not done.** | The suite ran on Windows, where .NET ignores `TZ`. Every Phase 3 date test injects its zone, but the plan asked for the pass, so run it in the next Linux session. |

## 5. Constraints and traps Phase 4 inherits

Everything in the earlier hand-offs' constraint sections still holds. Phase 3 adds:

- **`PlaceLauncher` is the only gateway for a launch, and the only caller of `RecordOpen` (D23, D24).** Phase 4's file places add `FileExists` to `IShell` and a `File` branch to `PlaceLauncher.Open`'s pre-check. Never launch from the view model, and never call `RecordOpen` from anywhere else. A launch that doesn't go through the launcher is a launch that isn't counted, or is counted without the pre-check.
- **`IShell` has two implementations to keep in step:** `WindowsShell` in the app, and `Fakes/FakeShell` in the tests.
- **The places schema is now 3, and `places.v1.json`, `places.v2.json` and `places.v3.json` are frozen and pinned by hash.** Phase 4 may not need a bump at all: a `file` type value is new data, not a new field. But a v3 build that meets `"type": "file"` fails to bind the enum and classifies the store `Damaged`. That is the *wrong* outcome for a file written by a newer build. Before adding `File`, decide whether this means a bump to 4, so older builds refuse the file cleanly as `WrittenByNewerVersion`. The recommendation is yes, bump.
- **`PlaceSortKey` names are persisted in `settings.json` (D30).** Renaming a member silently drops a user's remembered sort. Phase 4's relabel of "Path / URL" to "Destination" is safe because the key is already `Destination`.
- **Each grid column's `SortMemberPath` is a `PlaceSortKey` name**, read only by `MainWindow.PlacesGrid_Sorting`. A new column needs a key (or `CanUserSort="False"`), or clicking its header does nothing.
- **`MainViewModel.Places` must still stay in stored order.** Sorting lives in `PlacesView.CustomSort`; `InsertRestored` depends on the collection's order.
- **Import keeps `Id`, `DateAdded` and usage (D33).** The roadmap's §4.18 question is settled for those fields. Favourite state is the only part of it still open for Phase 4.
- **Format dates in the view model with `CultureInfo.CurrentCulture`, never with a XAML `StringFormat`** (D31). `FrameworkElement.Language` is still unset, so a `StringFormat` date shows en-US.

## 6. Open questions

The Phase 3 plan's §12 lists seven, with recommendations. Two are addressed to Phase 4:

1. **A slow pre-launch check** (§12 q2). `Directory.Exists`, and soon `File.Exists`, on an unreachable network path can block the UI thread for many seconds. Decide in the Phase 4 plan whether the check moves off the UI thread.
2. **Importing favourite state** (§12 q5, carried from Phase 2). Everything else about an imported place now round-trips.

The rest are for later phases: usage on the bubble tooltips (Phase 7), what counts as an open when a launch is indirect or cancelled (Phase 5), pruning orphaned per-file choices (Phase 5), calling Phase 9's column **Last visited**, and resetting usage (not planned).

## 7. Environment notes

As in the Phase 2 hand-off §7, plus:

- **On Windows, the Python in Git Bash receives `\\` as `\` inside a quoted heredoc**, so a Python string meant to write a C# `\n` writes a real line break. Use the editor tool for any edit that contains a backslash.
- **The app has no override for its data location.** `Environment.GetFolderPath` ignores the `APPDATA` variable on Windows, so running the app always uses the real `places.json`. Back it up first.

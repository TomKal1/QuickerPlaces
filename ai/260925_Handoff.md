---
title: QuickerPlaces — Hand-off, 2026-09-25
status: current as of 2026-09-25 — supersede with a new dated hand-off rather than editing this one
created: 2026-09-25
parent: ai/260901_Professional Improvements Plan.md
supersedes: ai/260921_Handoff.md
---

# Hand-off — where to pick up

## 1. Start here

**Work from `main`.** This supersedes the 2026-09-21 hand-off's "continue on `Tom-features-1`, do not start from `main`".

Since that hand-off, two lines of work that had grown apart were brought together:

| Line | Where it was | What it carried |
|---|---|---|
| Feature work | `claude/continue-work-next-steps-r8bvdw`, merged to `main` as PR #3 | Search, type icons, keyboard shortcuts, the global hotkey and Settings dialog, undo for Remove, Copy Path/URL, a status bar, the Open data folder button, and service-layer fixes (fully-qualified folder paths, import self-duplicates, lost settings when closing maximized) |
| Phase 1 and the roadmap | `claude/root-folder-tracking-periods-6wddrq` (PR #2), a fast-forward of `Tom-features-1`'s planning commits | The roadmap and the Phase 1 and Phase 9 plans, plus the Phase 1 implementation |

The second line was merged into the first on the same branch name, restarted from `main` after PR #3. How each overlap was resolved, and why, is in `BUILD_SUMMARY.md` § "Merging Phase 1 with the feature work". Once that merge reaches `main`, PR #2 shows as merged, and PR #1 (planning documents only, all of which are included) can be closed. `Tom-features-1` on the remote still only holds the early planning commits; it has nothing that isn't on `main`.

## 2. State of the project

- **Phase 1 is implemented and merged with the feature work.** It builds with 0 warnings, and all 125 tests pass on Linux (2026-09-25). Its manual checklist (`BUILD_SUMMARY.md`, two items added for the merge) **still has not been walked on Windows**.
- **The feature work has had a hands-on pass on Windows** (by the user, 2026-09-25, before the merge). The merge changed how it saves (every edit now reports through the banner), so the checklist's last two items re-check that.
- **Phases 2 to 8 are unstarted as phases**, but the feature work already covers parts of two of them. Their detailed plans should start from the code, not from scratch:
  - **Phase 2 (§4.8):** immediate Undo exists. It's a session-only stack of `RemovedPlace` records restored through `PlacesService.TryRestore`, with a non-modal status bar that never takes focus, and it restores favourite ordering. Recently Deleted (§4.7, §4.9–4.10) and dropping the Remove confirmation (settled 2026-09-21) are not done: Remove still asks, and its message mentions Ctrl+Z.
  - **Phase 7 (§4.24–4.25):** the search box, Copy Path/URL (the roadmap calls it Copy Destination) and the keyboard shortcuts exist. Still missing: search matching the type label, a tooltip for truncated grid text, and an audit that every mouse-only action has a keyboard route. Search text is not persisted, as §4.24 requires.
- **The global hotkey and Settings dialog aren't in the roadmap.** They were added from the "better path launcher" goal. Nothing in §2's non-goals rules them out.
- **Phase 9** is still designed, not started, and deliberately last.

## 3. Next steps, in order

1. **Walk the Phase 1 manual checklist on Windows** and record the results in `BUILD_SUMMARY.md`. As on 2026-09-21, this gates *implementing* Phase 2, not writing its plan.
2. **Write the Phase 2 detailed plan** against the code as it now is. In particular, `Remove`/`TryRestore` and the status bar are the starting point for §4.8, and the v1 → v2 migration (`DeletedAt`, `DateAdded` to UTC `DateTimeOffset`) is still the first migration to touch a record. Note that `AppSettings` is already at schema 2; that's settings.json, separate from the places store, which is still at 1.
3. **Implement Phase 2**, then 3 → 4 → 6 → 7 (much of 7 is done, see above), then Phase 8's clean-machine check.

## 4. Decisions made on 2026-09-25

| # | Decision | Reasoning |
|---|---|---|
| 1 | **Phase 1's persistence design wins wherever it overlaps the feature branch.** The feature branch's copy-aside-and-start-empty and one-time save warning are gone. | Phase 1 separates a damaged file from a locked one or a newer version, and refuses to write in the latter two. The feature branch could overwrite intact data. |
| 2 | **The close prompt is kept.** On close with unsaved changes, `App` retries once and asks before discarding. | Phase 1 had no guard here: closing with the banner up silently lost the change. |
| 3 | **Single instance uses one named event and `AllowSetForegroundWindow`,** not Phase 1's mutex and `Topmost` toggle. | Atomic, nothing to abandon, and the documented foreground hand-off from the process the user just launched. It isn't the `SetForegroundWindow` workaround Phase 1's plan ruled out. Phase 1's exception guard and logging were kept. |
| 4 | **The test project targets plain `net10.0` and links the UI-free files** instead of referencing the WPF project. D5 ("no test constructs a Window") stands. | The suite runs on Windows and Linux alike, so cloud sessions and any future CI can run it. A file the tests link must stay free of `System.Windows.*`, which D5 already required in spirit. |
| 5 | **Tests never write the real diagnostic log.** A module initializer redirects it for the whole run. | Found during the merge: every test run appended to the developer's real `quickerplaces.log`. |

## 5. Constraints — unchanged from 2026-09-21, plus two

Everything in the 2026-09-21 hand-off's §5 (constraints on Phase 2) and §6 (traps) still holds, apart from its statement that the test project has `UseWPF` on (see decision 4). In addition:

- **`TryRestore` is a forward change, not a rollback (D1).** A failed save after an undo leaves the place restored in memory with the banner up, like any other edit. Phase 2's Undo must keep that property.
- **`AppSettings.CurrentSchemaVersion` must stay at least 2.** `SettingsService.Load` silently resets a newer file to defaults, and `main` has already written 2 to users' machines.

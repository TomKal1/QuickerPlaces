---
title: QuickerPlaces — Hand-off, 2026-09-21
status: current as of 2026-09-21 — supersede with a new dated hand-off rather than editing this one
created: 2026-09-21
parent: ai/260901_Professional Improvements Plan.md
---

# Hand-off — where to pick up

## 1. Start here

**Continue on `Tom-features-1`.** On 2026-09-21 it was fast-forwarded to the furthest-along work (`e8172a2`, the tip of `claude/root-folder-tracking-periods-6wddrq`), then took the status-correction commit on top. It is **ahead of `origin/Tom-features-1` and has not been pushed**; the push is a plain fast-forward.

| Branch | Relationship |
|---|---|
| `claude/root-folder-tracking-periods-6wddrq` | Left untouched at `e8172a2`. Contains everything `Tom-features-1` had before the 2026-09-21 commits. The name is misleading: it is the Phase 9 *planning* branch, but it carries the whole Phase 1 implementation. |
| `claude/phase-1-implementation-dzwzf9` | Ancestor. Nothing unique. |
| `claude/development-plans-discussion-01zvfg` | Not checked in full; its top commit is in the history. |

Do not start new work from `main` — it has no plans and no code beyond the initial upload. Stop adding to the `claude/…` branches; they are now history.

## 2. State of the project

- **Phase 1 (persistence reliability and recovery) is implemented** — the `IPlacesStorage` seam, result-based saves with a persistent unsaved-changes banner, the `Damaged` / `Unreadable` / `WrittenByNewerVersion` load classification with a recovery dialog, the bounded diagnostic log, and one instance per user.
- **Phases 2 to 8 are unstarted.** The roadmap ([`260901_Professional Improvements Plan.md`](260901_Professional%20Improvements%20Plan.md)) is the source of truth for what and why. Only Phase 1 and Phase 9 have detailed plans.
- **Phase 9 (folder activity tracking) is designed, not started, and deliberately last.** Its plan says not to begin until Phases 1 to 3 have landed.
- Release split, from the roadmap §1.1: **Release 1 = Phases 1–4, 6, 7** (with Phase 8's publish profiles and clean-machine check); **Release 2 = Phase 5**; **Release 3 candidate = Phase 9**.

## 3. Verified on 2026-09-21, and what is still not

Until `a64f2bf`, `BUILD_SUMMARY.md` and `README.md` said Phase 1 had never been compiled or tested, because the environment that wrote it had no .NET SDK. This machine has one (`dotnet --list-sdks`: 6.0.422 and 10.0.401; the projects target `net10.0-windows`).

| Check | Result |
|---|---|
| `dotnet build src\QuickerPlaces.sln` | Succeeded, 0 warnings, 0 errors |
| `dotnet test src\QuickerPlaces.sln --no-build` | 41 passed, 0 failed, 0 skipped (2 s) |
| Manual checklist — 9 items in `BUILD_SUMMARY.md` under "Manual verification checklist" | **Not walked.** Needs a person at a Windows desktop |

So Phase 1 is *compiler- and test-verified*, not *done*. The Phase 1 plan's definition of done requires the manual checklist and says its results go in the closing commit message. The items most likely to surprise: the `Topmost` toggle for second-instance activation (the plan says to record a failure as a known limitation, **not** to reach for `SetForegroundWindow`), and the "hold `places.json` open from another program" case, which is the only one that exercises the `Unreadable` path against a real lock.

## 4. Next steps, in order

1. ~~Correct the stale status text.~~ **Done in `a64f2bf`.** The Phase 1 plan still says "28 tests"; there are 41 (its 28 numbered cases plus extras), so do not treat 28 as the expected count.
2. **Walk the Phase 1 manual checklist on Windows** and write the results down. Anything that cannot be tested (the USB pull-out case, for one) is recorded as untested, not skipped. **This gates *implementing* Phase 2, not writing its plan** — see decision 4 below.
3. **Write the Phase 2 detailed plan** (roadmap §4.7–4.10: seven-day Recently Deleted), following `ai/README.md` § "Working on a phase": file-level, signature-level, an order of work that is also the commit sequence, tests named per behaviour. Write it against the code as it now exists, not against the roadmap alone. Save as `ai/YYMMDD_Phase 2 Detailed Plan.md` and add it to the `ai/README.md` index.
4. **Implement Phase 2**, then Phase 3 → 4 → 6 → 7, each preceded by its own detailed plan. Then Phase 8's clean-machine verification, which completes Release 1.

## 4a. Decisions made on 2026-09-21

| # | Decision | Reasoning |
|---|---|---|
| 1 | **Work continues on `Tom-features-1`**, fast-forwarded to the furthest-along work. The `claude/…` branches are left as they were. Not pushed. | It is the user's own branch and had no unique commits, so a fast-forward loses nothing and needs no merge. Keeping the `claude/…` branches untouched preserves an untouched reference point. Pushing is outward-facing, so it waits for the user. |
| 2 | **Phase 2 keeps the label "Remove" and drops the "can't be undone" dialog**; Undo and Recently Deleted replace it, and confirmation moves to the two irreversible actions. Recorded in roadmap §4.8. | A confirmation on a reversible action is friction that teaches click-through, and it would sit in front of the most frequent destructive action. "Remove" also matches the user guide and the neighbouring **Remove from Favourites**; a tooltip carries the new meaning. |
| 3 | **Phase 9's four open questions stay open**, with no action. Q2 (do launches through QuickerPlaces reach the activity store on their own?) is answered by testing on Windows when Phase 9 is reached, before writing any code for it. | The plan already recommends deciding once real data exists, and none blocks Phases 2 to 8. Deciding now would be guessing about behaviour nobody has observed. |
| 4 | **The Phase 1 manual checklist gates implementing Phase 2**, but not writing its plan. | Phase 2 rewrites `Remove` and adds the first migration, on top of the save, recovery and banner paths that only the checklist proves in the running app. A defect there would be built on. Writing the plan reads code and needs no running app, so the two can proceed in parallel. |
| 5 | **The phase order stands:** 2 → 3 → 4 → 6 → 7, then Phase 8's verification, then Phase 5, then Phase 9. Each phase gets its own detailed plan first. | The roadmap's reasoning holds: Release 1 carries no third-party dependency, and Phase 5 carries the vendor risk. |
| 6 | **Import keeps usage metadata** (roadmap §4.18) — already decided; no change. | Keeps export/import round trips lossless. |

## 5. Constraints Phase 1 places on Phase 2

These are settled; the Phase 2 plan should build on them, not revisit them. Sources: `BUILD_SUMMARY.md` D1–D6 and the Phase 1 plan.

- **D2 — every save is a whole-store write.** The Recently Deleted purge and the migration ride on this; do not introduce per-record writes.
- **D1 — a failed save does not roll back memory.** The change stays, `HasUnsavedChanges` goes true, and Retry just re-serialises. Soft delete and Undo must behave the same way. Roadmap §4.2 says "restore the previous in-memory state or keep the proposed state clearly marked as unsaved"; Phase 1 chose the second, so Undo must not assume it can roll back.
- **D3 — unresolved recovery refuses all mutations.** Purging (§4.10 says "never purge from a store that failed to load or migrate correctly") must sit behind the same gate.
- **Every `Try*` returns `ValidationResult` and takes `out PersistenceResult`.** Keep the two answers separate. The unsaved banner is driven only by `PlacesService.HasUnsavedChanges` via `MainViewModel.RefreshPersistenceState`; never set it from a returned result, or a rejected duplicate alias clears a real failure banner. There is a test for this.
- **Schema v1 → v2 is the first migration that touches a record.** §3 and §4.7 require converting `DateAdded` from local `DateTime` to UTC `DateTimeOffset` in that migration, exactly once, with the "interpreted as local time on the migrating machine" assumption recorded in the log. The migrated store is written back only after a save succeeds (§4.3). `Tests/Fixtures/places.v1.json` is frozen and must keep loading; add a `places.v2.json` beside it rather than editing it.
- **`PlacesStoreFixtureTests`** exists to catch accidental changes to the v1 shape. A deliberate v2 shape needs its own fixture and a migration test.

## 6. Traps — things a well-meant tidy-up would break

- **Do not merge the three load-failure paths.** `Damaged` may quarantine and start empty on explicit user choice; `Unreadable` and `WrittenByNewerVersion` must never rename, write, or offer an empty store. `QuarantineAndStartEmpty()` is called from exactly one place in `App.xaml.cs`, gated to `Damaged`.
- `DiagnosticLog` never records aliases or destinations, only counts and paths, and never throws. Its swallowed `catch` is deliberate, as are the two others named in `BUILD_SUMMARY.md`; do not "fix" them.
- Tests never construct a `Window` and never touch real AppData. Real-filesystem tests use `TempDirectory`.
- The test project has `UseWPF` on only to pull in framework references — that is not a licence to test WPF types.
- `DateTimeOffset` UTC for every new persisted timestamp (roadmap §3). Do not add another local `DateTime`.
- Persisted state split (roadmap §3, §4.19): portable data in roaming `places.json`; machine-local things (paths, versions, activity, the diagnostic log) in local AppData.

## 7. Open questions that remain open

- Phase 9 plan §11 (four questions: an `Exact` rollup floor, whether launches through QuickerPlaces feed the activity store, per-root vs global tray/startup, the 62-day retention default). Deferred on purpose — see decision 3; none blocks Phases 2–8.
- Roadmap §4.18 on whether imports retain usage statistics: decided for this release (preserve metadata), but revisit if the product later distinguishes backup from share.
- Phase 2 details the roadmap leaves to its detailed plan: how long the Undo notification stays up, and how Undo restores favourite ordering "where possible" (§4.8).

## 8. Environment notes

- Repo root `C:\QuickerPlaces`; solution `src\QuickerPlaces.sln`; app `src\QuickerPlaces`; tests `src\QuickerPlaces.Tests`.
- Build and test from `src\`: `dotnet build QuickerPlaces.sln`, then `dotnet test QuickerPlaces.sln --no-build`.
- Commits end with the `Co-Authored-By` line the session's attribution instructions give.
- Nothing has been pushed.

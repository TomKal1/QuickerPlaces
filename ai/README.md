# `ai/` — planning and hand-off documents

Working documents for QuickerPlaces: the original brief, the specification built from it, the build record, and the implementation plans. Code lives in `src/`; nothing in this folder is compiled.

## Contents

| Document | What it is | Status |
|---|---|---|
| [`260831_Raw brief for SI.txt`](260831_Raw%20brief%20for%20SI.txt) | The original request, verbatim, before any specification work | Historical — do not edit |
| [`260831_Initial SI brief.md`](260831_Initial%20SI%20brief.md) | System Instructions: the requirements handoff the first build was written against | Historical — superseded by the plans below where they conflict |
| [`BUILD_SUMMARY.md`](BUILD_SUMMARY.md) | How the build came together: the decisions made resolving the SI against the template, bugs found and fixed, the feature work, Phase 1, how the two were merged, and Phases 2 and 3 with their manual checklists | Living — append as work lands |
| [`260901_Professional Improvements Plan.md`](260901_Professional%20Improvements%20Plan.md) | The roadmap: nine phases in a plain order of work (§1.1), with non-goals, a test plan, and a definition of done | Living — the source of truth for *what* and *why* |
| [`260901_Phase 1 Detailed Plan.md`](260901_Phase%201%20Detailed%20Plan.md) | File-level and signature-level plan for Phase 1 (persistence reliability) | Implemented and merged with the feature work; builds, and the suite passes (125 tests at the merge, 238 since Phase 2); manual checklist closed on 2026-09-25 (3 passed; 8 untested, accepted by the user) |
| [`260914_Folder Activity Tracking Plan.md`](260914_Folder%20Activity%20Tracking%20Plan.md) | File-level plan for Phase 9 (opt-in root folder activity tracking), design decisions, and the Windows performance record | Implemented and merged to `main` (2026-09-27); 547 tests pass, build has zero warnings. Depth 2 and the Recents redesign passed in use; the remaining live checks are not done, judged not needed by the user (its §9 step 8) |
| [`260921_Handoff.md`](260921_Handoff.md) | Where to continue as of 2026-09-21, the decisions made that day, and the Phase 1 constraints and traps Phase 2 must build on | Superseded for *where to work* and *what next*; its §5 and §6 still apply, as extended by the Phase 2 hand-off |
| [`260925_Handoff.md`](260925_Handoff.md) | Phase 1 merged with the feature work: work from `main`, what each overlap kept and why, which roadmap items the features already cover, and the next steps | Superseded by the Phase 2 hand-off the same day for *where to work* and *what next*; its §4 decisions and §5 constraints still apply |
| [`260925_Phase 2 Detailed Plan.md`](260925_Phase%202%20Detailed%20Plan.md) | File-level and signature-level plan for Phase 2 (seven-day Recently Deleted), written against `main` after the Phase 1 merge: schema v2 and its migration, soft delete and Undo, the Recently Deleted dialog, expiry, and restore conflicts | Implemented and merged (PR #6); manual checklist closed on 2026-09-25 (19 passed, including 2 findings fixed and re-checked; 5 untested, accepted by the user) |
| [`260925_Phase 2 Handoff.md`](260925_Phase%202%20Handoff.md) | Phase 2 implemented (since merged to `main` as PR #6), the checklists to walk before anything else, the decisions made beyond the Phase 2 plan, the constraints and traps Phase 3 inherits, and cloud-session environment notes | Superseded by the Phase 3 hand-off for *where to work* and *what next*; its §4 decisions, §5 constraints and §7 environment notes still apply |
| [`260925_Phase 3 Detailed Plan.md`](260925_Phase%203%20Detailed%20Plan.md) | File-level and signature-level plan for Phase 3 (usage tracking and sorting), written against `main` at `1c03e59` with Phases 4, 5 and 9 in view: schema v3 with a stable place `Id`, one launch gateway that defines a recorded open, remembered sorting | Implemented and merged (PR #7); all 329 tests pass; the section 8 checklist is closed (part passed in use, the rest not done and judged not needed by the user) |
| [`260925_Phase 3 Handoff.md`](260925_Phase%203%20Handoff.md) | Phase 3 implemented: work on `claude/phase-3-usage-tracking` (not pushed), back up `places.json` and walk the checklist first, the decisions made beyond the Phase 3 plan, the constraints and traps Phase 4 inherits (including an enum-binding trap for the new `File` type) | Current as of 2026-09-25 for its constraints (§5), which Phase 9 inherits too. Superseded for *what next*: Phase 3 is merged (PR #7), and Phase 9 now comes before Phase 4 (roadmap §1.1) |
| [`260925_Phase 9 Handoff.md`](260925_Phase%209%20Handoff.md) | Start Phase 9: the branch, what to read, exactly what step 1 builds and tests, and the conventions and traps (paths on Linux, backslashes in shell edits, never running against real data) | Superseded by the step 2 hand-off for *where to work* and *what next*; its §3 conventions and traps still apply |
| [`260925_Phase 9 Step 2 Handoff.md`](260925_Phase%209%20Step%202%20Handoff.md) | Phase 9 step 1 done: what the tracker and matcher are, what the store in step 2 must respect from their output (sub-second durations, case-insensitive folder keys, visits counted once), what step 3's host inherits, and the one open question for the user | Superseded by the step 3 hand-off for *where to work* and *what next*; its §3 and §4 still apply |
| [`260925_Phase 9 Step 3 Handoff.md`](260925_Phase%209%20Step%203%20Handoff.md) | Phase 9 steps 1 and 2 done: the store's surface, and what step 3 (COM probe, host, performance gate) must agree with the user before any code, and wire | Superseded by the step 3b hand-off for *what next*; its wiring notes still apply |
| [`260925_Phase 9 Step 3b Handoff.md`](260925_Phase%209%20Step%203b%20Handoff.md) | Step 3 half done (the loop and clock); the user's agreement to a short VS session instead of a working day | Superseded by the current Phase 9 hand-off |
| [`260926_Phase 9 Handoff.md`](260926_Phase%209%20Handoff.md) | Steps 1–7 implemented and tested; the short live application checklist, performance result summary, and constraints | Closed 2026-09-27: Phase 9 merged; its constraints still apply. UI labels in it predate the Recents redesign |
| [`260926_Phase 9 Depth Tracking Investigation.md`](260926_Phase%209%20Depth%20Tracking%20Investigation.md) | What was tried for the initially missing `C:\X\2024\240015` and `240011` activity rows, the later passed check, and its limits | Historical investigation; Depth 2 manual check passed |
| [`260928_PDF Project Sessions Plan.md`](260928_PDF%20Project%20Sessions%20Plan.md) | Project sessions (PDF, Word and Excel), opt-in Recent Files, and the Library, requested 2026-09-28: how open files are found and the limits, the shared document code and store loader, `sessions.json` and `recent-files.json`, decisions D1–D20, tests, and the Windows checklist | Implemented on `ccr-8d834d76-kqbdun`; 745 tests pass, 0 warnings; **not yet run on Windows** — its §9 checklist is open |
| [`260926_File Activity Future Note.md`](260926_File%20Activity%20Future%20Note.md) | A possible later opt-in **Recent files** view from Windows Recent Items, kept separate from folder dwell time, and why file opens can't be inferred from Explorer or file changes | Built as Recent Files on 2026-09-28 (see the sessions plan §5); its adapter ideas for time spent remain open |

## Conventions

**Naming.** `YYMMDD_Title.md`, dated when the document was created, not when it was last touched. `BUILD_SUMMARY.md` and this index are the exceptions: they have no meaningful creation date because they are continuously updated.

**Two levels, deliberately.** The roadmap says what must be true and why. A detailed plan says which files change, in what order, and how each requirement is proven. Only the phase being implemented next gets a detailed plan — one written against code that does not exist yet is stale before it is read.

Phase 9's plan is the deliberate exception, and says so in its own section 0: the Windows research behind it — which APIs exist, what each one actually provides, and what the tracker must never do — was done when the question was raised, and deriving it a second time later would be the larger waste. Its design decisions are settled; its file layout expects a pass against the code as it exists when the phase is reached.

**Every document carries a status.** A reader should be able to tell in one line whether it describes the product's intent, a decision already made, or history kept for context. When a document is superseded, mark it here rather than deleting it.

**Cross-reference rather than restate.** A detailed plan cites the roadmap section it implements; the roadmap points to the detailed plan that expands it. Two copies of a requirement will drift.

**Decisions are recorded where they were made.** A choice between two workable designs goes in the plan that made it, numbered, with its rationale — so the next person can tell a deliberate decision from an accident. Open questions go at the end of the document that raised them.

## Working on a phase

1. Read the roadmap phase, then its detailed plan.
2. Follow the detailed plan's order-of-work section; it is also the intended commit sequence.
3. Keep tests alongside the change that needs them, not batched at the end.
4. When the phase lands, append to `BUILD_SUMMARY.md`, update the user guide, and write the next phase's detailed plan.

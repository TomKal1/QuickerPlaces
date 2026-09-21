# `ai/` — planning and hand-off documents

Working documents for QuickerPlaces: the original brief, the specification built from it, the build record, and the implementation plans. Code lives in `src/`; nothing in this folder is compiled.

## Contents

| Document | What it is | Status |
|---|---|---|
| [`260831_Raw brief for SI.txt`](260831_Raw%20brief%20for%20SI.txt) | The original request, verbatim, before any specification work | Historical — do not edit |
| [`260831_Initial SI brief.md`](260831_Initial%20SI%20brief.md) | System Instructions: the requirements handoff the first build was written against | Historical — superseded by the plans below where they conflict |
| [`BUILD_SUMMARY.md`](BUILD_SUMMARY.md) | How the first build came together, the decisions made resolving the SI against the template, and the bugs found afterwards | Living — append as work lands |
| [`260901_Professional Improvements Plan.md`](260901_Professional%20Improvements%20Plan.md) | The roadmap: eight phases across two releases, with non-goals, a test plan, and a definition of done | Living — the source of truth for *what* and *why* |
| [`260901_Phase 1 Detailed Plan.md`](260901_Phase%201%20Detailed%20Plan.md) | File-level and signature-level plan for Phase 1 (persistence reliability) | Implemented; builds and 41 tests pass; manual checklist outstanding |
| [`260914_Folder Activity Tracking Plan.md`](260914_Folder%20Activity%20Tracking%20Plan.md) | File-level plan for Phase 9 (opt-in root folder activity tracking): what Windows can and cannot tell you about folders a user has opened, and the design that follows from it | Design — implement after Phases 1 and 3 |

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

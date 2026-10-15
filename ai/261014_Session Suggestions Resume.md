# Session suggestions resume checkpoint

Recorded on 2026-10-14 while moving work from Claude to Codex.

## Current state after continuation

Latest navigation change: **Settings → Tracking and Revit → Session suggestions**,
beside Revit, now opens the existing Suggestions window over Settings. Closing
it returns to Settings; its settings save independently of the main dialog's
Save/Cancel buttons. The Sessions header/context-menu shortcut has been removed.
Earlier entry-point descriptions below are historical.

Follow-up after the M1 screenshot: Thomas requested a separate Suggestions
window and explicit documentation that it follows tracked files/folders.
Both Sessions entry points now open/activate an owned `SuggestionsWindow`;
the workspace panel and standalone side pane are replaced. Old saved panel
entries are retained but omitted before packing, and are no longer addable.
Both rules now filter retained opens/spans by current Recent Files kinds,
scope and enabled tracked-root snapshots (including equivalent prefixes).
Tracking-setting/root changes invalidate the cache and reject in-flight results.
This is documented in the window, shared settings, user guide and design G13.
The description of the earlier M1 panel below is historical.

Follow-up validation: the solution builds with 0 warnings/errors; all 2,239
tests pass (8 more cases than M1). The synthetic WPF check opened the new
window twice and confirmed that only one owned window existed, then rendered
it without binding warnings. No commit or push was made.

Thomas authorized rebuilding T5 and continuing. T5, T7 and T8 are now
implemented in the local working tree. Nothing was committed or pushed.
The original missing T5 source/review was not recovered; this is a new
implementation checked with regressions, mutation checks and synthetic WPF
renders. The historical discovery notes below explain the starting point.

- T5: background calculation with short cache locks; a cache-only badge;
  generation and input checks reject stale results, including the returned
  refresh list; missing files are filtered before grouping; hashes remember
  dismissed groups and subsets before ranking; quick New/Add and guarded Undo.
- T7: UI-free card and shared-settings view models, consent checks,
  persistence messages and a suggestion-aware editor without an initial scan.
- T8: the Sessions gear and count, Suggestions panel beside Sessions through
  ShowBeside, New/Add/Review/Dismiss/Undo, shared sensitivity controls in both
  settings surfaces, and recorded-open/settings/session-change refresh wiring.
- The standalone Sessions window's side pane was also wired so its always
  visible gear has a working destination. This covers the pane portion of T9;
  card/file drag-and-drop remains unimplemented.

Validation for this continuation:

- Windows solution build: 0 warnings, 0 errors (.NET SDK 10.0.401).
- Full suite: 2,231 passed, 0 failed, 0 skipped; 38 tests added across the
  source, view models, ranking and workspace integration.
- Three deliberately broken versions were caught by assertions: stale refresh
  return, Undo after an edit, and dismissal filtering after ranking. Each
  mutation was restored before final validation.
- An isolated WPF harness used in-memory stores and fake files, rendering
  populated/off/settings states in Dark and Light and the Sessions gear/count.
  No binding warnings were reported. Harness and images are under the ignored
  `artifacts/suggestions-smoke/` folder. This is not the real-data checklist.
- One intermediate full-suite run hit an existing Revit diagnostic-log sharing
  failure; the subsequent full-suite run passed. No Revit code was changed.

M1 is ready for the live user check specified in implementation plan §8/§9.
Next work after that milestone decision: T9 drag/drop, T11b's live sampler,
M2, T12 documentation and T13 real-use checks/measurements. T10a/T10b and
T11a are already committed. The open-together setting and algorithm exist,
but no five-minute sampling host is connected yet. No performance claim has
been made. The full user guide update remains T12.

Suggested M1 check: open the gear on Sessions; turn on Recent Files and
suggestions; adjust sensitivity in either settings surface; try New/Add,
Review, Dismiss and Undo; confirm that editing/deleting the target makes
Undo refuse with a message; close/reopen Suggestions and check its placement.

## Verified repository state

Working branch: `ccr-6f869762-m72lq5`, HEAD `e3f309b`.
The checkout was clean before this checkpoint was added.

Completed tasks found in commit history and source:

| Task | Commit | Work |
|---|---|---|
| T1 | `47a8da9` | Sensitivity, options, records and keys |
| T2 | `fccf824` | Used-together rule |
| T3 | `8b85592` | Suggestions store |
| T4 | `57b3bc0` | Grouping, session matching and names |
| T6 | `ed74583` | Panel type and ShowBeside |
| T10a | `e0c3f06` | Open-span recorder |
| T10b | `ec52a79` | Open-together rule |
| T11a | `48db54d` | Shared document window listing |

T0's original environment baseline was not recovered. A fresh Windows baseline
was run against the checkout above with .NET SDK 10.0.401:

- `dotnet build C:\QuickerPlaces\src\QuickerPlaces.sln --no-restore --verbosity minimal`: 0 warnings, 0 errors.
- `dotnet test C:\QuickerPlaces\src\QuickerPlaces.sln --no-restore --verbosity minimal`
  from `C:\QuickerPlaces\src`: 2,193 passed, 0 failed, 0 skipped.
- The three Revit failures mentioned in the Claude handoff did not reproduce.

## T5: reported progress, source not recovered

Thomas supplied Claude's latest status. It reports an approved T5 implementation
with 38 tests and successful deliberately broken variants, followed by one final
small fix round before commit. The key remaining finding was that a late refresh
could return an older list still containing a dismissed card.

The reported T5 behavior:

- Calculate suggestions in the background without holding a lock.
- The gear badge reads the last result without triggering a calculation.
- Discard results when inputs change during calculation.
- Filter dismissed cards before ranking so they cannot hide valid cards.
- Refuse Undo New session after the session is edited or deleted, with a message.
- Regression tests cover those behaviors and were checked against broken versions.

These are handoff claims, not independently verified code. Neither
`SessionSuggestionSource.cs` nor its tests exists in this checkout. GitHub's
contents API also returned 404 for that source on the working branch. No copy
was found in the local Claude project/task/session records searched.
The design's per-file dismissal hashes are documented in HEAD but are not yet
implemented by the existing store's key-only dismissal API.

## Resume order

1. Recover Claude's uncommitted T5 source, tests and final reviewer findings,
   or rebuild T5 from the design and the handoff if recovery is unavailable.
2. Finish the final T5 fix round. Add a deterministic regression that holds a
   refresh in flight, dismisses a card, then releases the refresh and checks
   both its returned list and the cached badge/list. Recheck stale results,
   dismissal-before-ranking, and Undo after edits, deletion and save failure.
3. Run the build and full Windows suite. Keep the measured baseline above
   distinct from Claude's reported test counts.
4. Continue T7 (view models), then T8 (WPF wiring), and reach M1.
5. At M1, follow the implementation plan's milestone decision before continuing
   to T9, T11b and the remaining documentation and Windows checks. T10a/T10b
   and T11a are already committed.

At discovery the design and implementation plan still said "not started".
Their status lines and the index have now been updated for the local M1 work.

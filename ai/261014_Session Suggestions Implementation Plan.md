---
title: QuickerPlaces — Session suggestions implementation plan (overseer, writer, reviewer)
status: proposed (2026-10-14); not started. Needs Thomas's go-ahead to run (§9)
created: 2026-10-14
parent: ai/261014_Session Suggestions Plan.md
---

# Session suggestions: implementation plan

The design plan (`261014_Session Suggestions Plan.md`, "the design") says **what** to build and **why**: decisions G1–G16, the algorithm, the UI, the data file, the tests and the Windows checklist. This document says **who builds it, in what order, and how each piece is checked** before it lands. Where the two disagree, the design wins; a change to the design is made there first, by the overseer, with Thomas's agreement.

## 1. Roles

On 2026-10-14 Thomas asked for *"an implementation plan where opus 5.5 medium is the reviewer and sonnet 5.5 medium is the code writer and opus is the overseer"*.

| Role | Model and effort | Does | Never does |
|---|---|---|---|
| **Overseer** | Opus 5.5 (the main session) | Owns this plan and the design. Writes each task brief (§5). Starts the writer and reviewer. Decides review disputes. Runs the gate (§6) and commits. Talks to Thomas. Keeps `BUILD_SUMMARY.md` and the design's status current. | Writes feature code itself, except to integrate or to finish a task after the third failed round (§4). |
| **Writer** | Sonnet 5.5, medium effort | Implements one brief, test first. Runs the build and the tests. Reports what it did and anything it couldn't do (§5.2). | Changes files outside its brief's list, edits the design or this plan, commits or pushes, or decides a question the brief leaves open (it asks in its report instead). |
| **Reviewer** | Opus 5.5, medium effort | Reads the brief, the design's relevant sections and the writer's diff. Checks it against §7's checklist. Returns findings with a severity (§5.3). | Edits code. Asks for work outside the brief. |

Each writer and reviewer starts fresh with only its brief, so the brief has to carry everything it needs: the design sections to read, the files it may touch, the tests to add and the acceptance criteria.

## 2. Ground rules for every agent

These go at the top of every brief.

- **Architecture boundaries.** `Models/` and `Services/` stay free of WPF so the `qp` CLI and the Linux test project can use them. Anything that calls Windows (`EnumWindows`, timers tied to the dispatcher) is app-only and is not linked into the tests. View models stay UI-free, as the existing ones are.
- **Tests are linked, not referenced.** A new UI-free file is added to `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj` as `<Compile Include="..\QuickerPlaces\…" Link="Linked\…" />`, like its neighbours.
- **Test first.** Write the failing tests from the brief, run them and see them fail, then write the code. The report says which tests failed before the change.
- **Never real data.** Tests use `TempDirectory`, `FakePlacesStorage`, `ManualTimeProvider`, `TestZones`, `FakeShell` and `TestPaths` (Linux has no `C:\`). Nothing reads `%LocalAppData%`.
- **Match the code around it.** Doc comments that say why, decision numbers cited as the code already does ("(G8)"), the same store and `PersistenceResult` patterns as `SessionStore` and `RecentFilesStore`.
- **No CI/CD changes.** No files in the Pi agent folder are staged.
- **No model identifiers** in code, comments or commit messages.
- **Small diffs.** If a brief turns out to need a file it doesn't list, the writer stops and says so rather than widening the change.

## 3. Environment

The cloud container has no .NET SDK at the moment. Task T0 installs the .NET 10 SDK, as an earlier session did (see `BUILD_SUMMARY.md`).

| Check | Command (from the repo root) | Where it runs |
|---|---|---|
| Build, XAML included | `dotnet build src/QuickerPlaces.sln -p:EnableWindowsTargeting=true` | Linux container |
| Tests | `dotnet test src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj` | Linux container |
| Full suite, as CLAUDE.md asks | `dotnet test src/QuickerPlaces.sln` from `C:\QuickerPlaces\src` | Thomas's Windows PC |
| Running the app, the §13 checklist, the §10 measurements | Visual Studio, `dotnet-counters`, Stopwatch logging | Thomas's Windows PC |

WPF can be compiled but not run on Linux. Tasks T8, T9 and T11 are therefore **compile-checked only** until Thomas runs them. Their reviews pay extra attention to bindings, resource keys and threading, which the compiler doesn't check.

## 4. The loop for each task

1. **Brief.** The overseer writes the brief (§5.1) and records the commit the work starts from.
2. **Write.** The writer implements it test first, runs the build and the tests, and reports (§5.2).
3. **Review.** The reviewer gets the brief, the report and `git diff <start>`, and returns findings (§5.3).
4. **Fix rounds.** **Blocking** and **should-fix** findings go back to the writer in a short fix brief, then the reviewer checks the new diff. **Nits** are collected and applied by the writer in the same round only if they're in files it's already changing. A finding the writer disputes goes to the overseer, who decides and says why in the fix brief.
5. **Limit.** After three rounds with blocking findings still open, the overseer stops the loop and either finishes the task itself or splits it.
6. **Gate.** The overseer runs §6, reads the whole diff once, and commits with a message naming the task and the design decisions it implements.

Independent tasks can run in parallel (§8). Each writer then works in its own git worktree so their diffs don't mix, and the overseer merges them one at a time, running the gate after each.

## 5. Templates

### 5.1 Task brief (overseer → writer)

```
Task Tn: <name>
Start commit: <sha>
Read first: design §… (and nothing else unless listed); existing code: <files>
Goal: <one paragraph>
Decisions this implements: G…
Files you may create: …
Files you may change: …
Tests to write first (they must fail before your change): …
Acceptance: build has 0 warnings; all tests pass; <task-specific>
Out of scope: …
Open points: <anything the overseer has already decided for this task>
Ground rules: <§2, verbatim>
Report back in this form: <§5.2>
```

### 5.2 Writer report (writer → overseer and reviewer)

- Files changed, one line each.
- Tests added. Which failed before the change, and how they failed.
- Build result (warnings count) and test result (passed/failed/skipped, total).
- Anything not done, and why.
- Questions the brief didn't answer, and what was assumed.

### 5.3 Review findings (reviewer → overseer)

One item per finding:

- **Severity:** blocking (wrong behaviour, a broken design decision, data loss, a crash, a boundary break, a missing required test), should-fix (a real but minor problem, a test that doesn't actually prove its claim), or nit (style, naming).
- **Where:** `file:line`.
- **What:** the defect, and a concrete case that shows it (input → wrong result).
- **Design reference** when it's about a decision (G…, §…).

And a one-line verdict: **approve**, or **changes needed**.

## 6. The overseer's gate (before every commit)

1. Build: 0 warnings, 0 errors.
2. Full Linux test suite passes, and the count went up by the tests the task added. T0 records the starting count.
3. The diff touches only the brief's files, plus the `.csproj` link lines.
4. For one or two of the new tests, revert the fix locally and see them fail. This proves they test the change.
5. No WPF type in a file linked into the tests. Search for `System.Windows` in the linked files.
6. The design's status line and `BUILD_SUMMARY.md` are updated at each milestone (§8), not after every task.

## 7. Reviewer checklist

1. **Does it do what the design says?** Check each decision the brief names, with an example input.
2. **Do the tests prove it?** Each acceptance point has a test. Thresholds are tested on both sides (for example 59 and 60 minutes). Day boundaries use a non-UTC zone from `TestZones`.
3. **Boundaries.** No WPF in UI-free files. App-only code isn't linked into the tests.
4. **Persistence.** Behaves like the other stores: damaged and newer files are left untouched, with a notice; `PersistenceResult` is reported, never thrown; writes are atomic through `IPlacesStorage`.
5. **Threads.** Anything the sampler's timer touches is locked or handed to the dispatcher. Nothing blocks the UI thread. A disposed timer stops writing.
6. **Privacy.** Paths are never logged (`DiagnosticLog` logs types and counts only, as `PlaceLauncher` does). Dismissals store hashes, not paths (G9).
7. **Scope.** Nothing beyond the brief, and nothing in the design's "not in this version" list.
8. **Idiom.** Reads like the code next to it.

## 8. Tasks

The design's §11 steps, split so each task is one reviewable diff. The "Parallel with" column lists tasks that touch different files and can run at the same time.

| Task | What | Design | Depends on | Parallel with |
|---|---|---|---|---|
| **T0** | *Overseer.* Install the .NET 10 SDK, build, run the tests, record the warning and test counts as the baseline. | — | — | — |
| **T1** | `SuggestionSensitivity`, `SessionSuggestionOptions.For(overall, usedTogether?, openTogether?)` with §6.1's table, the output records (`SessionSuggestion`, `SuggestionReason`, a `StrongPair` record shared by both rules), `SessionSuggestionKey`. | §6, §6.1, G5, G9, G15 | T0 | — |
| **T2** | `OpenedTogetherRule` (rule A): bursts, active days, days together, share. | §4, G3, G4 | T1 | T3, T6, T10a |
| **T3** | `SessionSuggestionsDocument` and `SessionSuggestionStore`: switches, the three sensitivities, dismissals, `openSpans` field (stored, not yet filled), pruning. | §8, G14 | T1 | T2, T6, T10a |
| **T4** | `SessionSuggester`: grouping, matching against sessions (new vs add), dismissals, ranking, naming with " (2)" for a clash. | §6, G8, G9, G11 | T2 | T6, T10a |
| **T5** | `SessionSuggestionSource`: ties the stores and `SessionStore` together, caches until Recent Files records (G7), drops missing files through `IShell.FileExists` (G12), quick New and Add through `SessionStore.TryCreate`/`TryUpdate` with Undo. | G7, G10, G12 | T3, T4 | T6 |
| **T6** | `PanelTypes.Suggestions`, `WorkspaceLayoutService.ShowBeside`, the panel's minimum height in `PanelLayoutEngine`. | §7.1, G13 | T0 | T2–T5 |
| **T7** | View models: `SuggestionsViewModel` (cards, actions, status, Undo), `SuggestionSettingsViewModel` (G16), `SessionEditorViewModel.ForSuggestion`, the gear's count in `SessionsViewModel`. | §7, G10, G16 | T5 | — |
| **T8** | *WPF, compile-checked.* `SuggestionsPanel`, `SuggestionSettings` control, the gear on `SessionsPanel` with badge and context menu, hosting in `RecentFilesSettings`, wiring in `WorkspaceView` and `MainWindow`. | §7 | T6, T7 | — |
| **M1** | *Milestone: rule A works end to end* (compile-checked). Overseer updates `BUILD_SUMMARY.md` and the design's status, and asks Thomas whether to pause here for a Windows check before rule B. | | | |
| **T9** | *WPF, compile-checked.* Drag cards and single files onto session rows; the side pane in `SessionsWindow`. | §7.1 | T8 | T10a |
| **T10a** | `OpenSpanRecorder`: spans from samples, missed samples close spans, 62-day pruning. | §5 | T1 | T2–T5 |
| **T10b** | `OpenTogetherRule` (rule B) and its always-open filter; spans saved in the store; rule B pairs fed into `SessionSuggester`. | §5, G1, G4 | T4, T10a, T3 | T9 |
| **T11a** | Move `FindViewerWindows` out of `WindowsOpenDocumentProbe` into `DocumentWindows`. **Behaviour unchanged.** The review checks it's a pure move. | G6 | T0 | any |
| **T11b** | *App-only, compile-checked.* `OpenDocumentSampler`: 5-minute timer off the UI thread, pause and presence, scope, stopping on exit; the rule-B switch in the settings control. | §5, G2, G6 | T10b, T11a, T8 | — |
| **M2** | *Milestone: both rules, compile-checked.* Overseer updates the docs and hands over to Thomas (T13). | | | |
| **T12** | User guide section (`USERGUIDE.md`), `BUILD_SUMMARY.md` entry, the design's status line, `ai/README.md`. The writer drafts, the reviewer checks it against what was built, and the overseer commits. | | M2 | — |
| **T13** | *Thomas, on Windows.* The full suite, the design's §13 checklist and its §10 measurements (before/after under the same conditions, repeated runs, with spread). Results go into the design. Tuning §6.1's values, if needed, is a new small task. | §10, §13 | T12 | — |

**Reviewer focus by task**, on top of §7:

- **T2, T10b:** threshold edges on both sides; local midnight and DST; case-only path differences; the 40-file burst cap.
- **T3:** loader outcomes match the other stores; unknown sensitivity strings read leniently; turning rule B off empties `openSpans`.
- **T4:** the order-dependence of greedy grouping. The same data in a different order must give the same groups. Monotonic sensitivity: Low never shows more than High on the same data.
- **T5:** Undo after a failed save; a session renamed or deleted between showing a card and pressing Add; the cache cleared when it should be.
- **T6:** works in rows and columns layouts; undoable; no change outside Arrange mode except `ShowBeside`.
- **T8, T9, T11b:** binding paths exist on the view models; resource keys exist (`Icon.Settings`, the card styles); nothing runs on the UI thread that could wait on Windows; the sampler stops when tracking is paused, on sign-out and on exit.
- **T11a:** a line-by-line move; the probe's existing tests still pass unchanged.

## 9. Running it

The overseer starts each writer and reviewer as a subagent: the writer with Sonnet at medium effort, the reviewer with Opus at medium effort. The design's task list gives about 15 tasks. With one review round on average plus some fixes, that's roughly **35–45 agent runs**. That's well above the session's default size for one multi-agent workflow (under 10 agents), so it would run **one milestone at a time**: T1–T8 up to M1, then T9–T11b up to M2, then T12. Each milestone starts only when Thomas says so.

Commits go to the working branch (`ccr-6f869762-m72lq5` unless Thomas names another), one per task, after the gate. No pull request is opened unless Thomas asks.

## 10. When the overseer asks Thomas

- The design doesn't decide something the code has to (for example how to name a suggestion whose files share nothing and have no project number).
- A reviewer finding means changing a design decision.
- A task needs a file outside the design's §9 list.
- At M1, whether to check rule A on Windows before starting rule B.
- After T13, whether §6.1's values need tuning.

## 11. Done means

- Every task committed after its gate, with 0 build warnings and the Linux suite passing.
- The design's §13 checklist walked on Windows and its §10 measurements recorded (T13).
- `USERGUIDE.md`, `BUILD_SUMMARY.md`, the design's status and `ai/README.md` current.

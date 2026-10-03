# Input/render diagnosis — follow-up to the performance handoff

## Status and scope

Priority 1 **advanced, not closed**. Diagnosis only, on `perf/input-render-diagnosis`, based on committed revision `fab00f8fedfea6ba96752627ef487d37bfd5c748`. No tracked application/test/CI/CD changes, staging or commits. Thomas's uncommitted [handoff](261002_Performance%20Handoff.md) is preserved. Idle/retention, query benchmarking, broader workloads and startup remain pending.

The [first optimization's](261002_Performance%20Optimization.md) gains and regressions remain historical results. This session makes **no new optimization, memory-improvement or physical-input-to-pixel claim**.

### Findings

1. **Callback/read ordering measurably affects the UIA metric under the same current build.** The Library callback began during 22 of 24 no-match row-count read intervals; it began after the read in two. No callback completed before the read started. Two otherwise equivalent first-cycle actions completed UIA in **45.04 vs 6.69 ms**, despite Library application ending at **44.79 vs 44.15 ms** and the next WPF render-handler end occurring at **51.02 vs 50.59 ms**. A fast Saved-grid check can substantially precede Library/calendar work.
2. **The main measured no-match callback cost is synchronous year presentation mutation**, not the pure calendar builder or Saved filtering. Mean ± sample SD of three run medians: year presentation **17.01 ± 5.06 ms**, builder **0.26 ± 0.11 ms**, Saved filtering **1.25 ± 0.30 ms**, background query **3.06 ± 0.56 ms**. These are elapsed spans, not CPU attribution: collection notifications, synchronous WPF work, GC/scheduling and observer overhead can contribute.
3. **Typing is not equivalent to whole-text replacement.** For character-by-character `no-match-baseline`, heat changed at the prefix `no`. Subsequent characters left heat unchanged and skipped calendar rebuilding. Year-presentation spans at that transition were **12.28 / 13.83 / 8.94 ms** in the three diagnostic runs. Most later empty-result edits applied in much shorter spans.

The trace confirms the handoff's ordering hypothesis is possible and important; it does **not** prove that the earlier before→after regression was entirely an automation artifact. Nor does it establish that users saw no regression. There is no matched historical-build diagnostic or physical keyboard/paste cohort yet.

## Baseline and validation

Fresh current-commit Release build saved, before diagnostic instrumentation, at:

- `.a5c/input-render/baseline-bin/`
- `QuickerPlaces.dll` SHA-256: `6c7bdb99fa882c7bd3339caa56580878f3e3082dc6fdd4de5f08c6b0d77a692d`

This is the **next implementation comparison's before binary**. Neither `.a5c/optimization/original-bin/` nor the instrumented binary below is that baseline. First-iteration binaries/evidence were not overwritten.

New validation in this session, outside captures:

```powershell
dotnet build src/QuickerPlaces.sln -c Release
Push-Location src
dotnet test QuickerPlaces.sln -c Release --no-build
Pop-Location
```

**1,267 passed, 0 failed/skipped; build 0 warnings/errors.** Logs: `.a5c/input-render/build.log`, `tests.log`. No new tests were added because no production fix was implemented.

## Live execution path and instrumentation

Actual search order is important:

1. `WorkspaceView.xaml` `SearchBox.Text` binding → `WorkspaceViewModel.SearchText` → `LibraryViewModel.SearchText`.
2. `SetProperty` raises notifications **before** `QueryEdited`. `WorkspaceViewModel.OnLibraryPropertyChanged` forwards the search change; `WorkspaceView.xaml.cs` synchronizes `MainViewModel.SearchText`.
3. `MainViewModel.SearchText` synchronously runs `PlacesView.Refresh()` for Saved places. Synchronization can re-enter the Library setter with identical text; its equality guard prevents another query. Re-entrant setter markers must not be counted as extra input actions/queries.
4. `LibraryViewModel.QueryEdited` → `Refresh` → `DispatcherBackgroundWork.Run` → thread-pool `LibraryQueryEngine.Run`.
5. Dispatcher application checks generation, then `BuildRows`, conditional `BuildCalendar`, notifications; WPF layout/render follows and can also perform synchronous work during notifications.

Instrumentation was added **only to a copied app project**, `.a5c/input-render/diagnostic-src/`. Four local patches preserve exactly what changed: `library-instrumentation.patch`, `saved-instrumentation.patch`, `input-instrumentation.patch`, `eventsource-instrumentation.patch`.

A diagnostic `EventSource` marks preview text/key input, search setters, Saved filtering, query request/start/end, apply/stale-result handling, rows, calendar builder, year presentation and month presentation. Calendar builder timing encloses `ActivityCalendar.BuildYear`; year presentation encloses `UpdateCalendarWeeks(CalendarWeeks, year.Weeks)`; month timing encloses `BuildMonth`. Query-end→apply-start includes continuation/dispatcher scheduling, not just time in a dispatcher queue. Stale-result rejection is unchanged; the three recorded stale applies per diagnostic process were startup generations 1–3.

Instrumented DLL SHA-256: `171c328e6639111e305ce18088594dd57fda946af9f826ede9c89f0c29b209c1`. It is **diagnostic-only**, not a candidate optimization. EventSource calls and WPF ETW add overhead which has not been separately quantified; UIA creates automation peers. Do not compare these timings to counter-only sustained results as improvements.

## Capture method

Environment remained Windows 11 Pro build 26200, i5-12600K/16 logical processors, RTX 3060 driver 32.0.15.9186, Balanced power, .NET SDK 10.0.401 and desktop/runtime 10.0.12. Fixture settings retain the earlier 1200×800 workspace; no window/DPI change was requested. Full environment and hashes are recorded locally.

WPF ETW was available through Windows `logman` without elevation in this session. Visual Studio Community was found at `C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\devenv.exe`; its interactive Application Timeline was not run.

Primary diagnostic cohort: `.a5c/input-render/capture-2/`, approximately **23:12–23:18 EDT on 2026-10-02**. Four fresh processes: one uninstrumented current-build smoke, then three instrumented repeats. Each uses `--workspace --data-root <fresh isolated root>` and copies the untouched original fixture. Normal stores, tracking and clipboard were untouched; no documents/external applications were opened.

- Same 120-place/12-session/60-recent-file corpus, dated history and fixed 2026-10-02 anchor as the handoff. Search counts `10, 1, 0, 3, 120` revalidated in every cycle.
- 10-second settle; **eight** diagnostic cycles per repeat (three in the uninstrumented smoke).
- Same five replacement queries and All→Sessions→Recent→Saved tabs, but **one-second post-action pacing**, not four-second cycle slots or the five-minute sustained workload.
- Each process additionally types `Project 001` and `no-match-baseline`, at 100 ms character pacing, then clears. `WM_CHAR` is sent **only to the isolated process's HWND**, with timeout checking; UIA focuses that window's SearchBox. No global `SendInput`/`SendKeys`.
- **375 recorded actions/checks**: 112 targeted characters, 108 replacement searches, 108 tab selections, 31 clears, eight typing resets and eight final typing validations. Replacement/clear counts, tab selections and final typed text/counts passed. Individual character entries verify successful delivery, not a row-count assertion at every prefix.
- Instrumented WPF `PreviewTextInput` confirmed all **28 characters per diagnostic process** reached the text input path. This is **synthetic window-directed character input**, not physical keyboard, IME or paste evidence.
- WPF provider `{E13B77A8-14B6-11DE-8069-001B212B5009}`, keywords `0x1108` (input/layout/graphics), informational level; separate diagnostic provider in the same session. **Zero lost ETW events** in all four traces.
- Harness boundaries and ETW use the same **10,000,000 Hz QPC clock**. The reader verifies QPC intervals against ETW relative milliseconds. UTC is only an annotation, not the ordering clock.

Exploratory `pilot-1` failed before launching an app because `logman` does not accept repeated `-p` arguments; provider-file configuration corrected that. `pilot-2` and `capture-1` are retained but excluded from the table below. Review of `capture-1` exposed uncertain near-boundary UTC/Stopwatch correlation; `capture-2` repeated the workload with direct QPC boundaries. Do not pool the cohorts.

## Repeated no-match timelines

Milliseconds; each cell is **run median / nearest-rank p95**, eight no-match actions per run. With only eight samples, p95 is the maximum; variation is descriptive, not a confidence interval.

| Span/proxy | Run 1 | Run 2 | Run 3 |
|---|---:|---:|---:|
| Saved filtering | 0.93 / 3.10 | 1.51 / 2.30 | 1.30 / 3.03 |
| Background query | 2.63 / 20.55 | 2.86 / 4.16 | 3.69 / 8.22 |
| Query end → apply start | 0.70 / 4.52 | 0.90 / 4.93 | 1.15 / 8.14 |
| BuildRows | 1.15 / 4.53 | 1.34 / 7.93 | 3.42 / 6.11 |
| Pure calendar builder | 0.22 / 0.42 | 0.19 / 0.42 | 0.38 / 0.57 |
| Year presentation update | 13.91 / 42.29 | 14.26 / 40.30 | 22.85 / 36.14 |
| Entire callback application | 18.04 / 43.79 | 22.16 / 41.47 | 26.95 / 37.42 |
| External UIA Saved-grid completion | 42.72 / 51.31 | 37.25 / 48.99 | 39.67 / 65.84 |

### Two concrete ordering examples

Same first-cycle replacement `Project 001` → `no-match-baseline`; times from the external action's QPC start:

| Event | Run 1, generation 7 | Run 3, generation 7 |
|---|---:|---:|
| External row-count read begins | 3.58 ms | 4.32 ms |
| Background query duration | 2.64 ms | 2.62 ms |
| Library callback begins | 6.07 ms | 6.73 ms |
| Library callback ends | 44.79 ms | 44.15 ms |
| External row-count read ends | **45.04 ms** | **6.69 ms** |
| First subsequent WPF render-handler end | 51.02 ms | 50.59 ms |

Run 1's external read remained outstanding during the callback; run 3's read ended just before it began. This establishes ordering sensitivity in the observer's completion metric. It does not identify the UIA request's internal service point or prove that every millisecond of the external read was blocked by the callback.

For no-match, a subsequent render-handler end was observed before the next action in **20/24** cases; in **17/20** it was later than UIA completion. Missing cases remain missing, not zero latency. The reader maps WPF event IDs using the installed `wpf-etw.man` and preserves raw ETL for richer decoding. These following-event markers lack frame/HWND causal attribution: **neither `WClientRenderHandlerEnd` nor UCE render/present events establish scan-out, final meaningful paint or physical input-to-pixel latency**. Cursor/animation activity can produce unrelated events. The analysis deliberately does not publish first-following-present intervals as user latency.

Other actions remain distinct: clear-search medians for query-end→apply-start were **37.16 / 33.13 / 30.23 ms**; UIA completion medians were **106.48 / 97.99 / 112.76 ms**. All per-action query, callback and render-proxy spans for ordinary replacement searches, typed prefixes, clears and tabs are in `timelines.csv`, with per-run distributions in `analysis.json`. These are diagnostic observations, not a new sustained acceptance cohort.

## Next decision and remaining gates

**Do not change dispatcher priority to make a row-count benchmark look faster.** The calendar still must update. Delaying work could improve an observer metric while worsening visible completion or input backlog.

The evidence supports a narrow next investigation of **changed day-cell visual/container churn during `UpdateCalendarWeeks`**. Week containers already remain stable; unequal day records are still replaced and notify WPF separately. Synchronous year-presentation time is an actionable measured span, but allocation/CPU stacks and changed-cell/container counts are still needed to attribute its interior cost. Do not infer that every replacement inflates a particular template from these elapsed spans alone.

Before a production fix:

1. Capture repeated **manual physical typing, paste, Esc clear and tab selection without UIA**, with WPF Application Timeline or WPF/DWM tracing and causal layout/render attribution. Compare the actual receipt→render behavior, not just Saved-grid completion. A safe local `manual-capture.ps1` is prepared and syntax-checked, **not executed**; it opens only a fresh isolated workspace, records WPF/custom ETW, and asks the operator to perform the actions. Inspect its scope before running it. Clipboard changes occur only if the operator deliberately copies/pastes text.
2. Obtain allocation/CPU stacks and realized-container/change counts around year presentation, and determine whether retaining changed-cell presentations would improve actual render latency while preserving heat, date/culture/selection behavior and immutable builder snapshots.
3. Only then author a focused failing regression test and implement a small candidate. Keep current generation rejection and UI-free boundaries. Measure it against the saved **uninstrumented current-commit baseline**, with equivalent instrumentation/workload, at least five counterbalanced sustained pairs and separate real-input confirmation.
4. Advance idle/capacity/retention investigation after this review: ordinary settled measurements, 30–60-minute/open-close workloads and separate induced-GC/root diagnostics. No memory conclusions from this timing cohort.

## Evidence and replay

Local-only evidence/tooling under `.a5c/input-render/` is not tracked and will not exist in a fresh clone:

- `baseline-bin/`, `baseline-hash.json`, validation logs.
- `diagnostic-src/`, the four instrumentation patches, diagnostic build logs.
- `capture.ps1`, `providers.txt`, `etl-reader/` (TraceEvent 3.2.4), `analyze.py`, `manual-capture.ps1`.
- `capture-2/`: four raw ETLs, decoded event JSONL files and loss summaries, action QPC boundaries, process IDs, environment/fixture/binary/harness hashes, untouched input provenance, per-run stores/logs, `timelines.csv`, `analysis.json`/`.txt`.
- Exploratory pilots and `capture-1/` retained separately; never overwrite/pool them.

Capture replay, after validation/builds finish, with a **new** output directory and the verified local diagnostic binary:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .a5c/input-render/capture.ps1 `
  -OutputDirectory C:/QuickerPlaces/.a5c/input-render/capture-replay -Cycles 8
# Decode each run using its PID from processes.json, then analyze:
dotnet .a5c/input-render/etl-reader/bin/Release/net10.0/EtlReader.dll `
  <run-N.etl> <recorded-pid> <events-N.jsonl>
# Save the reader's stdout as event-summary-N.json for every run.
python .a5c/input-render/analyze.py C:/QuickerPlaces/.a5c/input-render/capture-replay
```

Operator-driven capture (interactive; do not run as unattended automation):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .a5c/input-render/manual-capture.ps1 `
  -OutputDirectory C:/QuickerPlaces/.a5c/input-render/manual-1
```

The manual cohort is not compatible with the automated analyzer's four-run action schema; retain/decode its ETL and review actual input/query/layout/render events separately. Revalidate dated history and binary/harness hashes before replay. No profiling/build/test concurrency, production forced GC or normal-store reuse.

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

## 2026-10-03 follow-up — validation and manual-capture readiness

Priority 1 remains **open**. On this machine, branch is `perf/input-render-diagnosis`, HEAD `5926986d10e28eefb3a5085fdaa07d8a6d13622f` (the diagnosis documentation commit), with a clean initial worktree. The local remote-tracking ref is two commits ahead; no pull/merge was performed. Application source under `src/` is unchanged from `fab00f8`. No production/test/CI/CD changes, staging or commits were made in this follow-up.

Fresh validation, outside profiling: **1,267 passed, zero failed/skipped; Release build zero warnings/errors**. A new uninstrumented build archive is `.a5c/input-render/readiness-261003/baseline-bin/`, DLL SHA-256 `9f37e83699ee9810ccbf2dd9b297b37874966fd8952445674f2cea7d3715e214`. Its revision/runtime metadata and logs are in `readiness-261003/validation.json`, `build.log` and `tests.log`. This is a fresh current-HEAD before archive; the earlier baseline and all first-iteration binaries remain intact and hash-verified. The new hash is not evidence of a production performance change.

The manual harness was audited and hardened locally, **not executed as a capture**:

- Requires a fresh child output under `.a5c/input-render/`, explicit `-OperatorPresent` and console confirmation before starting any application or ETW session. No UIA, injected input or programmatic clipboard access. Only the operator performs keyboard/paste/tab actions.
- Checks diagnostic/fixture hashes, corpus counts and disabled tracking/startup/hotkey/tray settings. A successful `-ValidateOnly` invocation creates no output directory, app or ETW session.
- Static fixture audit on **2026-10-03** confirms 120 places/12 favourites, 12 sessions/120 paths, 60 recent files and 12,000/480/10,800 open timestamps. No recent opens would be pruned by the current 365-day local-date rule, and no file exceeds the 500-open cap. This is **not loaded-query or visible-heat verification**. The October 2 anchor is preserved; any new capture is a separate dated cohort and must not be pooled with `capture-2`. The operator must inspect October 2 heat, not assume today's cell has activity.
- Records PID/HWND, QPC launch/action-end boundaries, date/environment/provenance and operator-reported counts, heat and deviations. Manual coarse boundaries/observations are not latency measurements or automated correctness assertions.

A source audit found an input-marker blind spot: the old diagnostic `PreviewKeyDown` listener was registered after `InitializeComponent`, while the XAML-bound search handler sets `Handled` for Esc. That later listener can miss the clear key. A **separate** copied project, `.a5c/input-render/diagnostic-manual-src/`, moves the key marker to the start of that existing handler and adds a `DataObject.Pasting` marker without reading or altering clipboard data. It changes no handler behavior. The prior diagnostic source/binary are untouched. The new diagnostic Release build succeeds with zero warnings/errors; DLL SHA-256 `c28a6312ca31358a46fadd5a264fbe418ac015e800e94bb636727f936674f32f`. Patch, build log, hash and the previous harness are saved in `readiness-261003/`. These added markers have unquantified overhead; their receipt/order still needs runtime confirmation in the operator trace.

**Remaining blockers are unchanged:** no physical-input capture has run; there is no causal final-render/scan-out confirmation, new inner-span CPU/allocation attribution, candidate optimization, retention/endurance or startup result. WPF/custom ETW plus these markers can support input/query/layout diagnosis, but the harness does not collect DWM frame attribution. Use Visual Studio WPF Application Timeline or richer WPF/DWM review for the final-render gate; do not publish first-following-render intervals as input-to-pixel latency. Diagnostics tools, WPR, DWM provider registration and Visual Studio are present locally; this follow-up has not revalidated capture permissions or started those tools.

Scoped Babysitter run `01M4136TMADPY5YCKM47HPAMXN` journaled readiness work and initially stopped at the operator gate. That readiness gate was fulfilled after Thomas executed/stopped the captures; the readiness run is completed, not proof that priority 1 is closed. No historical completed run was resumed.

### Later on October 3 — operator captures inspected

Thomas ran two isolated manual captures. Earlier "not run" statements above describe readiness before these attempts. `manual-261003-1/` is a partial typing-only pilot. `manual-261003-2/` is a usable **single-cycle exploratory trace with workload deviations**, not the repeated acceptance cohort. Both ETLs decode with zero reported lost events; original artifacts are preserved, with decoded events, event summaries and `review-1.md` added inside each directory. The second app exited cleanly at 15:28:06 UTC; its process and capture session are stopped.

The second trace confirms character-by-character `Project 001`, Ctrl+A/Ctrl+V and a paste event, then Esc preceding the empty-query setter. Later typing and a second Esc are also present. However, the paste included the console's expected-count annotation: **`no-match-baseline (0)`**. Later typed text was **`no match baseline`** with spaces. These are exploratory variants, not exact matches for the prescribed queries. Multiple empty-query refreshes are recorded, but the markers do not identify the named tab sequence. Visible counts and October 2 heat restoration remain unverified. Operator observations contain `1`; Thomas reports one cycle with variable pauses. Pauses between actions do not invalidate within-action diagnostic spans, but this workload cannot be pooled with prior cohorts.

Single-action elapsed spans, milliseconds:

| Action | Receipt marker → Library apply end | Background query | Query end → apply start | Year presentation |
|---|---:|---:|---:|---:|
| Paste `no-match-baseline (0)` | 364.52 | 1.25 | 342.39 | 10.26 |
| First Esc clear | 154.12 | 32.66 | 92.80 | 18.69 |
| Typed prefix `no` | 39.37 | 1.59 | 1.74 | 10.51 |
| Second Esc clear | 101.53 | 27.78 | 58.41 | 10.15 |

Receipt means a WPF diagnostic paste/key/text marker, not hardware receipt; completion means the Library callback, **not final pixels**. Instrumentation overhead remains unquantified. The paste shows a substantial query-end→application gap without UIA, whose cause is not established; do not attribute it to human pauses, dispatcher priority alone or the calendar span. At typed prefix `no`, Saved filtering took 13.45 ms and BuildRows 9.61 ms; later empty-result typing skipped calendar rebuilding. The interior CPU/allocation and scheduling causes still require attribution. No physical-input before/after regression verdict, causal final-render confirmation, optimization or memory claim follows from this single trace.

### Scheduling follow-up — existing trace, no new capture

Thomas requested investigation of the scheduling gap. `DispatcherBackgroundWork.cs:18–28` runs the query through `Task.Run`, then a thread-pool `ContinueWith(..., TaskScheduler.Default)`, then `_dispatcher.BeginInvoke`. The selected overload defaults to **Normal**, not Background/Idle; there is no deliberate delay in this path. Priority does not preempt an already executing dispatcher operation. Query-end→apply-start still includes continuation execution and enqueue/dispatcher timing because those intermediate markers were not recorded.

Same-thread QPC interval analysis places **342.1699 ms of the paste's 342.3848 ms gap (99.94%) inside an in-progress WPF layout span on UI thread 12156**. Its callback starts **0.2149 ms after `WClientLayoutEnd`**. The layout's total elapsed span is **343.2770 ms**; **341.5262 ms occurs after its last recorded `WClientArrangeEnd`**. The later year-presentation mutation is a separate ~10.26 ms span, not the cause of that preceding interval.

| Action | Query-end → apply-start | UI-thread layout overlap | Gap outside layout |
|---|---:|---:|---:|
| Paste `no-match-baseline (0)` | 342.38 ms | 342.17 ms | 0.21 ms |
| First Esc clear | 92.80 ms | 91.96 ms | 0.84 ms |
| Typed prefix `no` | 1.74 ms | 1.59 ms | 0.16 ms |
| Second Esc clear | 58.41 ms | 57.65 ms | 0.77 ms |

All **19 completed generations with gaps ≥20 ms** in this one process have **96.68–99.94%** same-thread layout overlap. Ordinary early `Project...` typing is mostly overlapping measure/arrange, whereas the paste and several clears have long post-arrange tails. These are within-process action observations, not independent repeats. Interval overlap is **not active CPU attribution or proof the continuation had already enqueued the callback**; concurrent GC, blocking waits or OS descheduling remain possible.

The installed WPF manifest and public upstream **v10.0.12** source show that the outer layout span also encloses SizeChanged, LayoutUpdated, automation-peer event processing and font/buffer-cache resets. Subspan events for the first three are **Verbose (level 5)**, but this capture used WPF **level 4**, so they cannot be recovered by further decoding. The version-tagged source reference is not a captured stack. There are no CPU stacks, runtime GC or context-switch events to attribute the 341.53 ms tail. Application size handlers and WPF automation processing are candidates, **not established causes**; lack of UIA harness input does not prove absence of automation peers or other accessibility clients.

**Conclusion:** investigate the in-progress UI-thread layout/post-layout span before changing scheduling. There is no evidence-supported dispatcher-priority fix. A focused next diagnostic needs verbose WPF subspans, continuation/enqueue/callback markers and GC/CPU/thread-time evidence under explicit permissions/scope. Repeating the same level-4 capture with stricter human pacing would still miss those facts. Causal final-render and historical-build comparison remain open.

Artifacts: `.a5c/input-render/scheduling-261003/` contains `report.md`, `scheduling-analysis.json`, focused raw `paste-gap-events.jsonl`, upstream source snapshots and `analyze_scheduling.py`. **Nine interval/pairing self-tests and three explicit real-trace gap invariants pass**; all 826 layout/890 measure/890 arrange/940 render-handler spans pair with zero unmatched boundaries. Original-artifact hashes are recorded and checked by `--verify-only`. No app launch, new trace, production/test/CI/CD change, staging or commit was performed for this analysis. Scoped run: `01M416MX49H87SDW12P7Q8FRPW`.

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
# Preflight only: no app, ETW session or output directory.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .a5c/input-render/manual-capture.ps1 `
  -OutputDirectory C:/QuickerPlaces/.a5c/input-render/manual-next-1 -ValidateOnly
# Run in an interactive local console ONLY when the operator is ready.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .a5c/input-render/manual-capture.ps1 `
  -OutputDirectory C:/QuickerPlaces/.a5c/input-render/manual-next-1 -OperatorPresent
```

The manual cohort is not compatible with the automated analyzer's four-run action schema; retain/decode its ETL and review actual input/query/layout/render events separately. Revalidate dated history and binary/harness hashes before replay. No profiling/build/test concurrency, production forced GC or normal-store reuse.

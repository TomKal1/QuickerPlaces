# QuickerPlaces performance optimization

## Status

First scoped iteration completed on 2026-10-02 after Thomas authorized work on the [initial Windows memory and responsiveness baseline](261002_Performance%20Baseline.md). The agent made no commits, staging or CI/CD changes; Thomas subsequently committed the first iteration as `fab00f8` on `perf/memory-responsiveness-baseline`. Existing onboarding/`.gitignore` changes were left alone during implementation. Nothing in the Pi agent folder was changed or committed.

**Retained result:** the combined calendar-presentation and query changes reduce allocation, GC pauses, sustained process memory and aggregate UI Automation interaction times in five matched pairs. **Trade-offs:** initial idle private memory rises about 3.9 MiB, and no-match search completion becomes slower. This is not an across-the-board memory/latency improvement or a leak fix. The optional memory goal and idle/no-regression aspiration are not met; review those trade-offs before merging.

The original baseline document remains historical evidence. Its numbers are not overwritten with this comparison.

## Implementation and live paths

Only two production files changed. No XAML, startup-order, store schema, CLI interface, global cache, GC settings or new dependencies changed. Shared code remains UI-free and continues to compile in the plain `net10.0` test/CLI boundary.

### 1. Preserve calendar visuals and unchanged filter chips

`src/QuickerPlaces/ViewModels/LibraryViewModel.cs`

Live path: search/tab/period change → `LibraryViewModel.Refresh` → `BuildCalendar`/`BuildMonth` → YearActivityPanel's nested ItemsControls. Query result → `BuildRows` → kind-filter chips.

The existing `CalendarIsCurrent` shortcut remains. When heat or selection really changes, the old presentation code cleared/refilled the year/month collections and recreated markers/weekday labels. This discarded unchanged containers and caused WPF template/binding/layout churn.

The new code:

- Leaves `ActivityCalendar.BuildYear` and its immutable results unchanged.
- Creates UI-thread presentation week records with **observable day lists**. Same-position weeks with the same start date keep their containers; only unequal day cells are replaced.
- Retains unchanged markers, weekday labels and strip width; avoids redundant property notifications.
- Uses point updates/removals for changes in year/month layout instead of resetting the entire collection.
- Keeps equal kind-filter chips; replaces only chips whose count/selection changed.

The retained builder result is separate from mutable presentation lists, so a month navigation cannot mutate the builder snapshot or the year view's day list. Existing generation checks still reject stale background query results. No attempt was made to change grouped row collection behavior or already-enabled FileShelf virtualization in this iteration.

### 2. Remove duplicate all-time indexing and temporary history lists

`src/QuickerPlaces/Services/Library/LibraryQueryEngine.cs`

Live path: `LibraryViewModel.Refresh` → `LibraryQueryEngine.Run` → `LibraryIndex.Build` and recent-file/history attribution.

- An all-time query formerly built the same resource index twice: once for rows, then again for heat attribution. It now reuses that index **within that query only**.
- A chosen period still builds its own period index and a separate all-history index, preserving the rule that period selection does not narrow calendar heat.
- Recent-file summaries count opens and compute the maximum timestamp in one pass, without allocating an intermediate open-timestamp list. All-time summaries avoid unnecessary local-date conversions; dated queries still use local dates.
- No cached state crosses queries, so no new invalidation or thread-safety policy is introduced.

Measured results below apply to the **combined two-file candidate**, not separate per-method savings. There was no ablation study attributing an exact percentage to either change.

## Regression tests and build

Tests were authored and run against the original source first. Red phase: four calendar-presentation assertion failures and one all-time-history traversal failure; other new cases passed. They failed for the intended collection/indexing behavior, not compilation errors.

New UI-free tests:

- `src/QuickerPlaces.Tests/CalendarPresentationTests.cs`: selection/clear preserves week containers and unchanged cells, point notifications, changed heat labels, marker/weekday/width reuse, kind-chip stability, midnight updates, and fresh-builder parity for year/month/week/month selection across en-US/en-GB/fr-FR including leap-year, blank and future cells.
- `src/QuickerPlaces.Tests/LibraryQueryAllocationTests.cs`: bounded history traversal for all-time versus dated queries, empty and unsorted histories, correct counts/last-open timestamp, and heat unaffected by the selected period.

All **nine new cases pass**. Full solution: **1,267 passed, 0 failed, 0 skipped**. Release build: **0 warnings, 0 errors**. Existing date/source/root/tag/session scope, retention, selected-row and stale-background-result tests remain passing.

Validation commands, from `C:\QuickerPlaces`:

```powershell
dotnet build src/QuickerPlaces.sln -c Release
Push-Location src
dotnet test QuickerPlaces.sln -c Release --no-build
Pop-Location
```

Build and tests were outside performance measurements. No app-source changes were made after the measured candidate binary was saved.

## Matched comparison method

Five counterbalanced before/after pairs, ten fresh processes, approximately **20:58–22:12 EDT** on 2026-10-02 (00:58–02:12 UTC on 2026-10-03).

Same Windows machine/runtime as the original baseline: Windows 11 Pro build 26200; i5-12600K, 16 logical processors; 15.75 GiB RAM; RTX 3060; Balanced power; .NET/WindowsDesktop 10.0.12; Release framework-dependent build; 96 DPI, fixed 1200×800 window. Normal background desktop applications remained running. Exact environment/process snapshot is in `.a5c/optimization/paired/environment.json`.

The original executable was saved **before any production edit**, verified against the initial baseline DLL hash. The candidate was built, copied to a separate folder and hash-verified. DLL SHA-256:

- Before: `680b3bdd1ecf9f00bc1a3bb86d9eaee5174db5ba24d856b860a5cd91d1ad8d71`
- After: `4ff1cb66b891f4e1f7eb7e55949bcc806189e156f4d6bb115e4ff93cbdb5997a`

Both run the **same original fixture paths and contents**: 120 places/12 favourites, 12 sessions/120 file paths, 60 recent files, substantial history, dark/green Desk layout, tracking off. Each data-root receives fresh copies of the same fixture stores; hashes are recorded. Normal app data and Windows startup registration remain untouched.

Pair ordering: before→after, after→before, before→after, after→before, before→after. Each run:

1. Startup handle/automation-ready measurement; attach `System.Runtime` counters at one-second intervals.
2. 10 seconds settle, 30 seconds idle.
3. 15 normal-use cycles, 135 actions, approximately 60 seconds.
4. **75 sustained cycles, 675 actions, 300 seconds**.
5. 30 seconds recovery idle, then clean shutdown.

The search texts, expected row counts, tab sequence and 250 ms per-action pacing are unchanged from the original baseline. **This comparison adds four-second cycle slots** to keep offered work equal: a faster candidate waits for the next slot rather than executing more cycles. Therefore this is a new controlled-load cohort; its before allocation rate is 62.7 MiB/sec, not the original unconstrained 75.4 MiB/sec. Do not compute a percentage improvement by mixing those cohorts.

Each run had 810 verified actions; **8,100 total**. No sustained cycle exceeded its slot. Three original-build normal cycles exceeded four seconds by **102–128 ms**; normal action counts remained equal. These small warm-up overruns are disclosed, not discarded. All collectors and app exits completed cleanly; there were no one-second WM_NULL timeouts or app-log errors.

Primary metrics have the same limitations as the baseline: UI Automation completion is not physical-keypress-to-pixel latency; Saved-grid row count does not prove all asynchronous Library/calendar visuals have finished; heap gauges describe the last GC, not continuously measured reachable objects. Counter/UIA overhead is included equally. No allocation trace or heap collection runs concurrently with the comparison.

## Five-pair results

Each cohort cell is **mean ± sample SD across five per-run values**. Memory is the mean of per-run phase medians; interaction p95 is the mean of per-run nearest-rank p95s. Paired reduction is computed within each pair, then averaged—not a claim of confidence intervals.

### Sustained workload

| Metric | Before | After | Mean paired reduction ± SD |
|---|---:|---:|---:|
| Allocation, MiB/sec | **62.687 ± 0.163** | **32.549 ± 0.117** | **48.08% ± 0.15%** |
| GC pauses, seconds per five minutes | **34.309 ± 0.316** | **15.728 ± 0.051** | **54.15% ± 0.51%** |
| Search completion p95, ms | **214.49 ± 3.60** | **133.50 ± 4.19** | **37.73% ± 2.63%** |
| Tab-selection p95, ms | **219.89 ± 2.28** | **117.93 ± 2.17** | **46.36% ± 1.47%** |
| Private memory, MiB | **169.80 ± 1.69** | **162.99 ± 0.81** | **4.00% ± 1.44%** |
| Working set, MiB | **257.59 ± 1.64** | **248.04 ± 1.40** | **3.70% ± 1.03%** |
| CPU, percent of one logical core | **44.90 ± 0.38%** | **29.07 ± 0.26%** | **35.25% ± 0.22%** |
| Last-GC heap median, MiB | **65.45 ± 1.05** | **59.97 ± 0.54** | **8.34% ± 2.05%** |
| GC committed median, MiB | **80.50 ± 0.94** | **75.21 ± 0.81** | **6.55% ± 1.83%** |

Ranges:

- Allocation: before **62.480–62.930**, after **32.435–32.729 MiB/sec**.
- GC pause: before **34.033–34.843**, after **15.664–15.792 sec**.
- Search p95: before **209.78–218.65**, after **129.44–140.60 ms**.
- Tab p95: before **217.74–223.52**, after **114.95–120.99 ms**.
- Sustained private: before **167.09–171.76**, after **162.18–164.35 MiB**.

The improvements occur in every pair and exceed observed variability. GC pause burden drops from about **11.4% to 5.2% of wall time**. Gen2 collections per sustained run fall from **403–412 to 142–144**; overlapping generation counters must not be added together. Single-core CPU figures are not whole-machine utilization (divide by 16 for that approximation).

### Per-pair timing/allocation evidence

Each cell is before → after:

| Pair | Allocation MiB/sec | GC pause sec | Search p95 ms | Tab p95 ms |
|---|---:|---:|---:|---:|
| 1 | 62.93 → 32.73 | 34.22 → 15.71 | 215.85 → 132.03 | 217.74 → 120.99 |
| 2 | 62.67 → 32.47 | 34.31 → 15.66 | 211.82 → 140.60 | 220.15 → 118.02 |
| 3 | 62.48 → 32.51 | 34.03 → 15.77 | 218.65 → 132.65 | 223.52 → 114.95 |
| 4 | 62.72 → 32.44 | 34.13 → 15.79 | 216.36 → 129.44 | 218.18 → 117.32 |
| 5 | 62.64 → 32.60 | 34.84 → 15.70 | 209.78 → 132.79 | 219.85 → 118.36 |

### Trade-offs, idle and startup

| Metric | Before | After | Interpretation |
|---|---:|---:|---|
| Idle private, MiB | **128.92 ± 1.80** | **132.79 ± 1.03** | **+3.86 MiB (~3%)**, beyond a no-idle-regression aspiration |
| Idle working set, MiB | **180.75 ± 1.31** | **184.62 ± 0.76** | **+3.87 MiB**, not an idle-memory improvement |
| Recovery private, MiB | **171.35 ± 3.51** | **165.23 ± 5.50** | Lower on average, but substantial variability |
| Startup handle, ms | **1,264 ± 67** | **1,234 ± 36** | Difference is within variability; no startup-improvement claim |
| Automation readiness, ms | **3,931 ± 135** | **3,843 ± 25** | Observer-heavy proxy; no render-time claim |

Idle last-GC heap medians differ by about **+0.64 MiB**, but GC committed capacity differs by about **+4.11 MiB**. This is consistent with some of the process-memory increase being heap-capacity/collection timing, but it does not prove a complete attribution of the extra private bytes.

Pooled sustained action times show both benefits and a regression:

| Action | Median before → after | p95 before → after |
|---|---:|---:|
| Clear search/show all places | **197.6 → 118.4 ms** | **232.5 → 148.8 ms** |
| Return to Saved places | **198.4 → 103.7 ms** | **240.0 → 137.4 ms** |
| Select Recent | **147.6 → 72.9 ms** | **183.9 → 107.6 ms** |
| No-match search | **25.7 → 48.8 ms** | **57.4 → 79.4 ms** |

The no-match regression is **not hidden by the aggregate p95 improvement**. This measurement can include different ordering of the asynchronous query callback and the UIA row-count check; a physical-input/render timeline is needed to establish the user-visible effect. Do not infer that every query or tab became faster.

## Separate heap diagnostic and WPF binding smoke

`dotnet-gcdump` 10.0.745401 captured initial and post-use heaps for one additional before/after pair. These collections **induce full GC** and are excluded from all primary comparisons. Initial diagnostic state already includes a search/filter-restore smoke check; post-use adds twenty equal cycles. No production GC call was added.

Reported collected heap sizes:

| Diagnostic snapshot | Before | After |
|---|---:|---:|
| Initial | **22.76 MiB**, 330,609 objects | **22.65 MiB**, 333,314 objects |
| After twenty cycles | **25.89 MiB**, 410,301 objects | **26.93 MiB**, 426,662 objects |

The candidate has slightly more post-use managed objects/bytes in this single diagnostic, despite lower ordinary process memory in the repeated workload. This is insufficient to claim a leak, leak elimination, reduced retained-object count or a production benefit from induced GC. Longer repeats and root analysis remain required. A large portion of the ordinary ~60–65 MiB last-GC heap gauge is not the same thing as these collected/reachable-object reports.

A live WPF/UIA smoke check in **both binaries** confirms the visible current-day calendar name updates from `60 files opened · 12 sessions reopened` to `no activity` on no-match filtering, then restores the original heat label after clearing. This checks that the observable day-list changes actually reach WPF/accessibility bindings; it is not a physical-input/render latency benchmark.

## Acceptance and remaining work

Retain this narrow change on the feature branch as a measured **allocation/GC and aggregate responsiveness improvement with disclosed trade-offs**, not as fulfillment of every proposed goal.

For this controlled offered-load cohort, the provisional allocation (≤53 MiB/sec), GC pause (≤30 sec/five minutes) and aggregate interaction p95 (≤150 ms) goals are met. **Sustained private ≤150 MiB is not met**, and idle memory/no-match latency regress. Startup has no reliable measured improvement.

Remaining recommended paths:

1. **Actual-input/render confirmation:** Visual Studio WPF Application Timeline or WPF/DWM ETW/PerfView for normal typing, no-match search, clear-search and tab changes. Investigate no-match callback ordering before claiming that regression is merely an automation artifact.
2. **Idle footprint and retention:** repeat idle/collected-heap snapshots; trace roots of added post-use visual/binding/automation objects and run 30–60-minute use plus panel/window open-close cycles. No leak claim from the current short diagnostics, and no forced GC in production.
3. **Remaining query/history/path churn:** establish a dedicated UI-free benchmark before considering broader normalized-identity or immutable-projection caching. Preserve date/source/tag/root/session semantics and stale-result rejection.
4. **Other representative workloads:** tracking enabled, real application/document discovery, network/offline paths, store mutations, scrolling and panel editing. The current tracking-off synthetic corpus cannot validate all of these.
5. **Startup:** obtain first meaningful rendered/interactive UI and reboot-cold measurements before proposing deferred initialization. Single-instance/store recovery guarantees remain unchanged.

No CI/CD integration is needed. No further broad refactor, cache or startup change is justified by this first iteration alone.

## Diagnosis follow-up — not a second optimization

See [Input Render Diagnosis](261002_Input%20Render%20Diagnosis.md). On `perf/input-render-diagnosis`, the current commit was freshly built/tested and archived as the next uninstrumented before binary. Repeated WPF ETW captures with separate copied-source instrumentation identify synchronous year-presentation mutation as the main measured no-match callback span and confirm that callback/UIA-read ordering can change completion times under the same build.

These are diagnostic cohorts with different pacing and instrumentation, **not new before/after gains**. Physical typing/paste without UIA, causal render attribution and inner-span CPU/allocation attribution remain gates before a narrow fix. No production implementation, idle/retention improvement or startup change was made. The first-iteration tables above remain unchanged.

## Evidence and replay

Local evidence under `.a5c/optimization/`:

- `original-bin/`, `optimized-bin/`: exact compared binaries.
- `compare.ps1`: paired harness; `analyze_compare.py`: descriptive/per-pair analysis.
- `paired/`: `environment.json`, fixture/binary/harness hashes, `results.json`, `memory.csv`, `actions.csv`, ten counter CSVs/logs, `summary.json`, `summary.txt`, `details.txt`, and isolated per-run stores/app logs.
- `heap-diagnostic.ps1`, `heap-diagnostic/`: four GC dumps, reports, accessible-label evidence and summary. Reports from Windows PowerShell are preserved in UTF-16 and also converted to UTF-8 for inspection.
- `red.log`, `query-red.log`, `focused-green.log`, `green.log`, `build.log`, `comparison.log`. The short pilot is separate and excluded.

Original evidence stays under `.a5c/assessment/`. Raw capture files and copied binaries are local artifacts, not committed source. The baseline and this document are the reviewable project documentation.

Replay on this machine, after rebuilding/test validation **outside timing** and saving the two intended binaries into the harness's original/candidate folders:

```powershell
# Use a NEW directory; the harness refuses to overwrite evidence.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .a5c/optimization/compare.ps1 `
  -OutputDirectory C:/QuickerPlaces/.a5c/optimization/paired-replay
python .a5c/optimization/analyze_compare.py C:/QuickerPlaces/.a5c/optimization/paired-replay
```

Do not feed post-run stores back into the fixture, use an optimized binary as the before build, compare induced-GC diagnostics with ordinary process memory, or compare different pacing/instrumentation cohorts as if they were equivalent. Revalidate dated history/corpus counts if replaying on another date.

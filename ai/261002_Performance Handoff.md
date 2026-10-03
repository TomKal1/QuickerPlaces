---
status: first optimization committed; continue measurement-led follow-up
branch: perf/memory-responsiveness-baseline
head: fab00f8
date: 2026-10-02
---

# Performance handoff — next session

## Follow-up progress — 2026-10-03

Work is now on focused branch `perf/memory-tracking-diagnosis`, HEAD `5926986` (diagnosis documentation), preserving the uncommitted input-diagnosis notes. Its parent `perf/input-render-diagnosis` was two commits behind its local remote-tracking ref; no pull/merge, staging or commits were performed. Application source remains unchanged from `fab00f8`.

- Thomas redirected the follow-up toward memory and unobtrusive Explorer/recent-folder tracking. See [Memory and Tracking Diagnosis](261003_Memory%20and%20Tracking%20Diagnosis.md). A single shortened ordinary-memory diagnostic reached **165.84 MiB sustained private / 160.69 MiB recovery**, with ~74 MiB reported GC committed and substantial last-GC fragmentation; no leak/native-root attribution or improvement claim. Passive Explorer measurements returned zero entries and therefore do not validate real tracking. Nine synthetic 120-file handle reads completed in **73.51–141.11 ms**, not a full real-viewer/session-scan benchmark. Normal settings/stores remain untouched; real document scans and endurance/root analysis are still pending scope/evidence. Thomas approved synthetic-only Explorer tracking comparisons. Two preserved attempts (`explorer-1/`, `explorer-2/`) opened/warmed owned synthetic windows but could not acquire foreground; **zero matched pairs completed and no tracking-on process launched**. Both test windows/apps closed cleanly. Windows rejected foreground activation even with the isolated app minimized; no input/foreground-lock bypass was used. Thomas then ran `explorer-3/` from his desktop: **three complete off/on pairs, 138 navigations, 111 eligible ticks and 12 visits**, zero probe failures, correct in-tree recording and clean cleanup. Pooled COM-ready navigation median **211.28 ms off / 211.36 ms on**; pair median deltas **−1.19, −3.50, +23.04 ms**, so no consistent local navigation slowdown. Whole-app CPU rose **1.23% → 3.84% of one core** (mean extra **1.16 CPU seconds per ~44-second window**), without stack attribution. No retained-memory verdict, pixel/subjective imperceptibility, default-idle/network/endurance acceptance or optimization claim. See diagnosis section 6 and `.a5c/memory-tracking/explorer-measured-summary.json`; review run `01M41EEV8F5RTKS2RWQWDHQACQ`. No further latency capture is justified solely to refine millisecond timings.

- Fresh Release validation: **1,267 tests passed; zero warnings/errors**. Current-HEAD uninstrumented archive: `.a5c/input-render/readiness-261003/baseline-bin/`. Earlier archives/evidence remain intact.
- Thomas subsequently ran two manual captures: a partial typing pilot and a usable single-cycle exploratory trace, both with zero reported lost ETW events. The second confirms typing, paste and Esc-clear markers, but query-text deviations and unverified named tabs/visible counts/heat prevent treating it as the repeated acceptance cohort. Both attempts are preserved separately from prior automated evidence.
- A separate diagnostic copy corrects a potential missing Esc marker and adds a paste marker; no production behavior changed. Its build succeeds, and the second trace confirms paste/Esc instrumentation. Existing-trace scheduling analysis places **342.17 ms of the paste's 342.38 ms gap inside UI-thread WPF layout**, with callback start 0.21 ms after layout end. A 341.53 ms post-arrange tail needs verbose layout subspans and CPU/GC/wait attribution; elapsed overlap does not establish its interior cause. No priority fix, causal final-render verdict or new improvement/memory claim.
- See [Input Render Diagnosis](261002_Input%20Render%20Diagnosis.md#2026-10-03-follow-up--validation-and-manual-capture-readiness) for hashes, safeguards, evidence and remaining gates. Readiness run `01M4136TMADPY5YCKM47HPAMXN` completed after operator captures; scoped scheduling analysis is journaled in `01M416MX49H87SDW12P7Q8FRPW`. Priority 1 remains open; priorities 2–4 remain pending.

## Follow-up progress — 2026-10-02

Work has started on `perf/input-render-diagnosis`, still based on `fab00f8`. See [Input Render Diagnosis](261002_Input%20Render%20Diagnosis.md) for new validation, the fresh current-commit baseline archive, repeated QPC-correlated WPF ETW timelines, and remaining gates.

- Current Release build: **1,267 tests passed; zero build warnings/errors**.
- New uninstrumented comparison baseline: `.a5c/input-render/baseline-bin/`; first-iteration artifacts remain untouched.
- No-match diagnosis identifies a synchronous year-presentation cost and confirms callback/UIA-read ordering sensitivity under the same current build. This is not a new optimization or proof that the historical regression was only an automation artifact.
- Priority 1 is **advanced, not closed**: window-directed synthetic characters were traced, but physical keyboard/paste and causal final-render confirmation still require operator-driven capture. A local manual-capture harness is prepared, not run.
- No tracked application/test/CI/CD changes, staging or commits. Priorities 2–4 remain pending; no new memory/retention/startup claims.

The original handoff state and instructions below remain historical context.

## Start here

Read:

1. `CLAUDE.md` and `.a5c/project-profile.md` (the profile exists on this machine but is not tracked).
2. [Performance Baseline](261002_Performance%20Baseline.md) — original measurements and proposed targets.
3. [Performance Optimization](261002_Performance%20Optimization.md) — first implementation, five matched pairs, trade-offs and evidence.
4. This handoff for the current committed state and next steps.

**Thomas committed the first iteration.** At handoff, branch is `perf/memory-responsiveness-baseline`, HEAD is `fab00f8`, and the working tree was clean before this document was created. `9737cd3` updates `.gitignore`; `fab00f8` contains the production changes, tests, documentation and `CLAUDE.md`. Earlier documents' statements about “no commits” describe the agent's work before Thomas committed, not the current repository state. There is no evidence here that the branch has been merged; check status rather than assuming it.

This handoff adds documentation only. Do not commit anything unless Thomas asks. Keep subsequent changes on a focused feature branch; do not edit CI/CD or stage/commit Pi agent folder files. Preserve WPF/MVVM and UI-free shared-code boundaries.

## What is already implemented

Only two production files changed in the first iteration:

- `src/QuickerPlaces/ViewModels/LibraryViewModel.cs`: stable presentation week containers with observable, point-updated day lists; reuse unchanged calendar markers/weekday labels/width and kind-filter chips. `ActivityCalendar.BuildYear` remains an immutable pure builder. Presentation lists are separate from its results.
- `src/QuickerPlaces/Services/Library/LibraryQueryEngine.cs`: reuse the all-time resource index **within a single query**; aggregate recent-open counts/maximum timestamps without temporary lists. Dated queries still build separate period/all-history indices, so period selection does not narrow heat.

No global/cross-query cache, startup-order change, store migration, XAML redesign, GC tuning or production `GC.Collect` was added. Existing stale-result generation checks remain.

Regression files:

- `src/QuickerPlaces.Tests/CalendarPresentationTests.cs`
- `src/QuickerPlaces.Tests/LibraryQueryAllocationTests.cs`

Last validation before Thomas's commits: **1,267 tests passed, zero failed/skipped; Release build had zero warnings/errors**. Nine new cases cover presentation identity/notifications, calendar parity across cultures and navigation, midnight, chips, and all-time versus dated history traversal. Existing source/date/root/tag/session/selection/stale-result tests passed. Re-run validation in the new session; do not represent these historical results as a new test run.

## Results to preserve — and regressions to investigate

Five counterbalanced pairs, ten fresh processes, same corpus and offered workload. Mean ± sample SD across five run values:

| Sustained metric | Before first iteration | After first iteration |
|---|---:|---:|
| Allocation | 62.687 ± 0.163 MiB/sec | 32.549 ± 0.117 MiB/sec |
| GC pauses / five minutes | 34.309 ± 0.316 sec | 15.728 ± 0.051 sec |
| Search completion p95 | 214.49 ± 3.60 ms | 133.50 ± 4.19 ms |
| Tab-selection p95 | 219.89 ± 2.28 ms | 117.93 ± 2.17 ms |
| Private memory | 169.80 ± 1.69 MiB | 162.99 ± 0.81 MiB |

Paired reductions: allocation **48%**, GC pause **54%**, search p95 **38%**, tab p95 **46%**, sustained private **4%**. These are combined-candidate results, not separately attributed savings from each method.

**Known trade-offs:**

- Initial idle private: **128.92 ± 1.80 → 132.79 ± 1.03 MiB**, approximately **+3.9 MiB**. Idle working set also rises about 3.9 MiB.
- Pooled sustained no-match search: median **25.7 → 48.8 ms**, p95 **57.4 → 79.4 ms**. Do not hide this behind aggregate improvement.
- The optional **≤150 MiB sustained-private goal is not met**; the no-idle-regression aspiration is not met.
- No reliable startup improvement was demonstrated.

UIA completion is **not physical input-to-render latency**. Search completion verifies the Saved grid's row count, not asynchronous Library/calendar completion; tab completion verifies selection. Handle appearance/automation readiness are not first meaningful paint. Fast paced WM_NULL probes do not establish busy-dispatcher responsiveness.

A separate single-pair `dotnet-gcdump` diagnostic induced full GC: initial collected heaps **22.76 / 22.65 MiB** before/after; after twenty cycles **25.89 / 26.93 MiB**. The candidate has slightly more post-use objects/bytes in that diagnostic. It does not prove a leak, leak elimination, or lower live-object retention. A WPF/UIA smoke did confirm visible calendar heat labels update and restore in both binaries.

## Next priorities, in order

### 1. Explain the no-match regression with actual input/render evidence

**First task should be diagnosis, not another optimization.** Reproduce no-match, ordinary typing, clear-search and tab transitions under the current committed build. Use Visual Studio WPF Application Timeline or WPF/DWM ETW in PerfView, with .NET diagnostics as needed. Check tool availability/permissions; do not silently substitute browser/Electron profiling or unsafe global keystroke automation.

Trace:

`WorkspaceView.SearchBox` → `WorkspaceViewModel.SearchText` → `LibraryViewModel.Refresh` and the Saved places filter → `DispatcherBackgroundWork` query completion → `BuildRows` / `BuildCalendar` → WPF measure/layout/render.

Useful locations: `LibraryViewModel.cs` around `Refresh` (898), `BuildRows` (924), `BuildCalendar` (1010), presentation helpers (1038–1070); `Services/DispatcherBackgroundWork.cs`; `Views/WorkspaceView.xaml.cs`; `Views/Panels/YearActivityPanel.xaml`; Saved-grid filtering in `MainViewModel.cs`.

Separate input receipt, synchronous Saved-grid filtering, background query duration, dispatcher callback application, and render completion. The query now completes sooner, so its callback **may** race differently with UIA's row-count read. That is a hypothesis, not evidence that the regression is just an automation artifact. Use real typing/paste scenarios, not only whole-text UIA replacement. Keep any instrumentation minimal and its overhead disclosed.

**Deliverable:** repeated per-action timelines and an identified bottleneck/ordering explanation. Only then propose a narrow fix with failing tests and equivalent before/after measurements.

### 2. Explain idle footprint and investigate retention

Reproduce the ~3.9 MiB idle increase using fixed settling/idle durations and record private bytes, working set, last-GC heap/committed capacity and collection timing. Existing idle gauges show only ~0.64 MiB more last-GC heap but ~4.11 MiB more committed GC capacity; this suggests, but does not prove, capacity/timing effects.

Use separate retained-object/root diagnostics for visual containers, binding/automation peers, event subscriptions and calendar/row objects. Add **30–60-minute sustained and panel/window open-close workloads**, with repeated post-idle observations. Induced-GC snapshots belong in a separate diagnostic cohort, never the ordinary baseline. Do not “fix” this with production forced GC or infer a leak from process-memory growth alone.

**Deliverable:** distinguish live retention, reclaimable objects, managed capacity and native/process footprint; identify an actionable root before changing code.

### 3. Remaining query/history/path churn

Establish a deterministic **UI-free query benchmark** before broader caching. Start in `LibraryQueryEngine.cs`, `LibraryIndex.cs`, `ResourceIdentity.cs`, `DocumentPaths.cs` and `LibrarySnapshot.cs`.

Important: a `LibrarySnapshot` **shares `Place` objects**; it is not a deeply immutable snapshot. Cross-query caching would need explicit invalidation for place/session/store mutations, dates/retention, source/tag/root/session filters, culture and reloads. The first change deliberately avoids that complexity. Preserve stale-generation rejection, selection identity and portable tests/CLI use.

### 4. Broaden coverage, then revisit startup

Current evidence is for tracking-off navigation/search/history. Add tracking-on, real document discovery, network/offline paths, mutations, scrolling and panel editing with safe isolated stores and explicit representative workloads. Opening real applications or tracking personal folders needs deliberate scope; do not silently change Thomas's normal settings.

Startup stays lower priority. Obtain first meaningful rendered/interactive UI and reboot-cold measurements before initialization deferral. Preserve the single-instance gate and store-recovery-before-mutation ordering.

## Evidence, harnesses and comparison traps

**These files exist locally on `C:\QuickerPlaces`, but `.a5c` evidence/tooling is not tracked. A fresh clone will not contain it.** If unavailable, stop and reconstruct/validate the workload rather than inventing measurements.

- `.a5c/assessment/`: original corpus, baseline scripts/raw results, counters, allocation trace and derived analysis.
- `.a5c/optimization/paired/`: ten-run JSON/CSVs, counters/logs, fixture/binary/harness hashes, environment, summaries and isolated app logs.
- `.a5c/optimization/compare.ps1`, `analyze_compare.py`: matched comparison harness/analysis.
- `.a5c/optimization/heap-diagnostic.ps1`, `heap-diagnostic/`: separate induced-GC evidence and accessible-label smoke.
- `.a5c/optimization/original-bin/`: **pre-optimization executable**, not the baseline for a second iteration.
- `.a5c/optimization/optimized-bin/`: first-iteration measured candidate. DLL SHA-256 `4ff1cb66b891f4e1f7eb7e55949bcc806189e156f4d6bb115e4ff93cbdb5997a`.
- Original DLL SHA-256: `680b3bdd1ecf9f00bc1a3bb86d9eaee5174db5ba24d856b860a5cd91d1ad8d71`.
- Diagnostics 10.0.745401 installed under `%LocalAppData%\Temp\qp-baseline-tools` (`dotnet-counters`, `dotnet-trace`, `dotnet-gcdump`); temporary installs may disappear. Check availability first.

For the next iteration, save a **fresh current-commit Release build as the new before binary**, and compare it with the next candidate. Preserve the old artifacts; fork/parameterize the harness to use new binary/output directories. The existing harness hardcodes `original-bin`/`optimized-bin`; simply running it again would compare the first iteration, not the next one. Record hashes/revisions again—rebuilding after commits may change assembly metadata.

Corpus: 120 places, 12 favourites, 12 sessions/120 paths, 60 recent files, 12,000 place-open / 480 session-open / 10,800 recent-open timestamps; fixed anchor **2026-10-02 16:00 UTC**. Revalidate dated history/pruning before future runs; if regenerating dates, use the same corpus for both binaries and start a new cohort. Normal stores must remain untouched via `--workspace --data-root <isolated root>`.

Matched harness: 10s settle, 30s idle, 15 normal cycles, **75 sustained cycles in 300s**, 30s recovery. Five search values with expected Saved-grid counts `10, 1, 0, 3, 120`, then All → Sessions → Recent → Saved; 250ms action pacing, **four-second cycle slots**. Five counterbalanced pairs took roughly **74 minutes**. A short pilot is useful but cannot replace sustained evidence. The original unconstrained baseline ran 89/90 sustained cycles and allocated ~75.4 MiB/sec; **do not mix its rates with the matched ~62.7 MiB/sec before cohort**.

Use fresh output/data-root directories; harnesses refuse existing outputs. Do not overwrite raw evidence, reuse post-run stores as fixtures, run builds/tests concurrently with timing, or mix counter-only runs with trace/induced-GC runs. Report per-run distributions and variation, including regressions.

## New-session first actions

1. Check branch/HEAD/worktree and read the documents above. Confirm local evidence/tools exist.
2. Validate current source, outside profiling:

   ```powershell
   # From C:\QuickerPlaces
   dotnet build src/QuickerPlaces.sln -c Release
   Push-Location src
   dotnet test QuickerPlaces.sln -c Release --no-build
   Pop-Location
   ```

3. Save/hash the current build for a **second-iteration baseline**, without replacing first-iteration binaries.
4. Start priority 1's input/dispatcher/render diagnosis, then priority 2's idle/retention measurements. Share findings before broad implementation.
5. Make only evidence-supported, reviewable changes; add focused correctness/performance guards, run the full suite and compare equivalent workloads. Update the optimization document with new cohorts, unresolved goals and limitations.

If using Babysitter, check `babysitter --help` and adapt its process definitions to .NET/WPF. Prior runs are **completed**, not pending implementation: baseline `01M3ZGPY0B4D4XGKZPXQJ08FYX`, first optimization `01M3ZKKM4MHQ5QKRMFGM2DZ5Y8`, stored under `C:\Users\Thomas\.a5c\runs`. Start a new scoped run rather than resuming those terminal runs. Thomas has since requested work on this plan; the follow-up remains measurement-led and does not authorize new commits, CI/CD integration or an unattended broad rewrite.

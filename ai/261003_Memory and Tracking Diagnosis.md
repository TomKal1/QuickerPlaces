# Memory, Explorer and document-check diagnosis — 2026-10-03

## Scope and status

Thomas redirected the performance work toward the ~163 MiB footprint and unobtrusive recent-folder tracking. Work is now on focused branch `perf/memory-tracking-diagnosis`, HEAD `5926986`, preserving the existing uncommitted input-diagnosis/handoff documentation. **Diagnosis only: no production/test/CI/CD changes, staging or commits.** Normal stores/settings and personal-folder tracking remain untouched. No real viewer/document was opened and no Windows Recent Items contents were read by the new diagnostics.

The [first optimization](261002_Performance%20Optimization.md) remains the historical five-pair result, including its idle/no-match regressions. This document is **not a new optimization or proof of lower retention**. A scoped Babysitter run journals this initial diagnosis: `01M4186CCB4CZGQWE85J580C9A`.

Fresh Release validation outside measurement: **1,267 passed, zero failed/skipped; application build zero warnings/errors**. The separate local probe initially needed an explicit System.IO import; its repaired build also has zero warnings/errors. Failure and successful build logs are preserved locally.

## 1. Fresh ordinary-memory diagnostic

One fresh process of the saved uninstrumented current build, SHA-256 `9f37e83699ee9810ccbf2dd9b297b37874966fd8952445674f2cea7d3715e214`, launched with `--workspace --data-root <fresh isolated root>` and the original fixture. Fixture hashes/date/retention/config preflight passed. Tracking remains off, with no roots. Runtime counters are sampled once per second; **no forced GC, allocation trace or heap dump** runs in this ordinary cohort.

Harness is a scoped fork of the matched comparison harness. The new cohort adds 10-second settling and 30-second idle **before UIA is attached**, then 10-second settling/30-second UIA idle, five normal cycles, **30 sustained cycles over 120 seconds**, and 60-second recovery. Four-second cycle slots and the same search counts/tab sequence are retained. All **315 actions** passed; no cycle exceeded its slot; process/collector exited cleanly. Total process duration about 289 seconds.

This is deliberately a **single shortened diagnostic**, not the original five-pair/300-second acceptance cohort. Do not pool rates or compute a new improvement percentage against historical results. Startup readiness fields include the extra pre-UIA wait and are not comparable startup measurements.

Phase medians, MiB:

| Phase | Private | Working set | Last-GC heap gauge | Last-GC fragmentation gauge | Reported GC committed |
|---|---:|---:|---:|---:|---:|
| Before UIA, idle | 142.47 | 195.79 | 31.00 | 10.48 | 40.36 |
| After UIA, idle | 134.48 | 191.02 | 27.42 | 6.71 | 45.91 |
| Normal interactions | 173.43 | 256.02 | 63.39 | 27.41 | 78.72 |
| Two-minute sustained interactions | **165.84** | **249.22** | **60.44** | **27.34** | **74.11** |
| One-minute recovery | **160.69** | **246.43** | **59.26** | **26.45** | **73.78** |

Counter summaries are trimmed to each phase's recorded first/last process samples. This avoids assigning the unmarked UIA-attachment interval to bare idle; one-second counter intervals still blur boundaries. Collection counts describe those interiors, not full phase totals. Recovery has **zero gen2 collections in its sampled interior**. The lower after-UIA idle private value cannot be interpreted as UIA reducing memory: ordering, startup settling and GC timing differ.

**Interpretation:** the ~163 MiB scale persists in this short current-build workload, but it is not 163 MiB of live managed application objects. The runtime reports ~74 MiB GC committed capacity during sustained use, and the ~60 MiB last-GC heap gauge includes ~27 MiB fragmentation. Those gauges do not measure continuously reachable objects. Process private bytes also cover native/runtime/WPF allocations and other process commitment; subtracting these different gauges is not an exact native-memory attribution. No actionable native/root cause or leak is established yet. Do not add production GC.Collect.

## 2. Historical managed-heap context — separate cohort

The existing induced-GC diagnostic's candidate heaps report **22.65 MiB / 333,314 objects initially**, and **26.93 MiB / 426,662 objects after use**. These were a separate older binary/process pair with UIA and forced collections; they are not current baseline samples.

Type-count review shows substantial WPF binding/property infrastructure: post-use tables contain 6,753 BindingExpressions, 6,755 PropertyPathWorkers, 7,628 EffectiveValueEntry arrays and 67,202 WeakReferences. This supports investigating visual/binding roots and container lifetime rather than assuming history data explains the entire footprint. Counts are descriptive: the table does not provide a complete byte decomposition or dominator/root attribution. WeakReferences alone do not imply leaks.

**Still required:** repeated 30–60-minute interaction/open-close workloads, post-idle observations, native/process-region analysis, and a separate retained-object/root diagnostic. This initial run does not close idle-regression or leak/retention questions.

## 3. Actual background and on-demand paths

### Recent-folder tracking

`App.OnStartup` → `ActivityTrackingHost.Start` → dedicated background STA → `ActivityTrackingLoop` → `FolderActivityTracker.Tick` → `EventShellWindowProbe.Sample` → `ActivityStore.Record`.

- Enabled/active sampling cadence is **1.5 seconds**; no roots/locked/suspended/paused states wait for signals, while user-idle checks are 15 seconds.
- Ordinary samples read a cached array plus foreground HWND. Explorer COM subscriptions/navigation and one-minute reconciliation run on a separate STA, not the app UI thread.
- Buffered activity flushes every five minutes and at lifecycle boundaries. Root/signal handling and shutdown still deserve latency coverage; being off the UI thread does not by itself prove no Explorer/CPU impact.
- A specific live-path candidate for the navigation test is `ShellComEvents.BrowserNavigationEventsSink.NavigateComplete2` → `EventShellWindowProbe.Worker.Navigated` → synchronous `RefreshPath` COM queries inside the event callback. Window-registration reconciliation is deferred, but this navigation refresh is not. The current empty-entry probe never exercised that path, so its impact on Explorer event delivery is unknown—not a measured stall or approved fix.
- The existing ActivityProbe Stress mode forces full GC every 1,000 passes. It was **not run or reused as ordinary memory evidence**.

### Recent-file tracking

`RecentFilesHost` timer → shared `WindowsRecentItems.Read` cache → scoped `RecentFilesStore.Record`; reads after an initial 20 seconds, then once a minute. A concurrent slow pass is skipped rather than queued. Changed shortcut targets are cached; cache pruning occurs above twice the 400-shortcut limit. This is distinct from folder tracking and the full session scan. Its real Recent Items workload was not measured here.

### Open-document scan

`SessionEditorDialog.ScanAsync` → awaited `WindowsOpenDocumentProbe.ScanAsync` → Task.Run → window/process clues → `WindowsHeldFiles.Read` → Recent Items → candidate existence/Restart Manager → UI ApplyScan.

This runs **on demand**, not on every folder-tracking tick. The implementation has a nominal 10-second scan budget, three-second held-file budget, one-second existence waits and a held-handle watchdog. These are **not a guaranteed hard deadline**: preparatory window/Recent Items reads, handle enumeration, some native calls and final path processing are not cancellable by those loop checks. Timed-out File.Exists work can continue, and stuck handle/COM workers can be abandoned. These are source-inspection risks for explicit slow/network/endurance tests, not observed failures or authorization to rewrite the code.

## 4. Passive Explorer and synthetic held-file measurements

A local console probe references the exact archived application assembly. Measurements ran separately from the app/counters/builds. No personal observed paths were persisted, no Explorer window was opened/navigated, no full session scan or real document viewer was started, and no GC was induced.

### Explorer — empty-cache case only

Three fresh-process bursts sampled the cached probe 5,000 times each. **Every probe returned zero Explorer entries**, with no navigation/window events. Cold setup was **106–139 ms**; tiny warmed burst timings are not representative of useful foreground tracking and CPU counter granularity prevents interpreting their recorded zero CPU as zero cost.

A separate fresh-process, 47-sample **69-second** run at roughly 1.5-second pacing covered initial and periodic reconciliation: **p95 0.0256 ms**, max 0.5514 ms, **46.875 ms process CPU total / 0.0677% of one core**, and 9,616 measured allocated bytes. Measurement scaffolding/reconciliation are included. This is encouraging for the **empty Explorer case only**. It does not establish navigation latency, populated cache performance, correct foreground attribution, enabled-host overhead or unobtrusive real tracking. Do not conclude no real Explorer windows exist merely because this API returned zero entries.

### Held files — 120 local fixtures in a helper

An explicitly launched synthetic helper held 120 local fixture PDFs. Nine reads targeted only that helper (no personal documents or viewer processes); all returned **120 files, complete**. Scan elapsed median **84.63 ms**, range **73.51–141.11 ms**; first scan 141.11 ms. Median measured managed allocation per read is about **1.35 MiB**. These include process/system-handle enumeration and mapped-drive lookup, but not window evidence, real PDF/Office behavior, Recent Items, candidate checks or the full session scan.

The probe's sampled private memory and handles rose during those nine fast scans; with no induced-GC/root cohort and probe bookkeeping included, this is **not leak evidence**. It is a reason to include repeated actual scans in endurance/root diagnostics. System-handle enumeration also uses temporary native buffers, so scan-only transient memory must be kept distinct from steady folder tracking.

## 5. Authorized synthetic Explorer comparison — initial attempts foreground blocked

Thomas authorized: **"yes run that explorer window test with only synthetic folders"**. Follow-up Babysitter run: `01M41C224R4NC9GC8AF99S4XX0`. The intended workload is three alternating-order off/on pairs of fresh isolated app processes, each with 20 rapid navigations plus 10-second dwell in three distinct folders. Folder readiness is verified through COM target path/non-busy state/32-item count; it is not pixel latency. The isolated root uses the normal 5-second dwell threshold and an explicitly modified 60-minute idle timeout in both cohorts, without fabricated physical input; default idle/lock behavior is out of scope.

**Two attempts were blocked before the first off workload; zero matched pairs completed and no tracking-on process launched.** Each created a new owned Explorer window on 12 synthetic folders containing 32 text fixtures apiece and warmed navigation across those folders. Normal Explorer windows/tabs and app stores/settings were not modified. The first could not acquire foreground after app launch. The one controlled retry minimized only the isolated app in both intended cohorts, but also failed foreground acquisition. Retry evidence records `SetForegroundWindow=false`, foreground HWND **3868640** unchanged before/after, versus owned Explorer HWND **199418**. Do not infer a slow/failing tracking implementation from this Windows automation limitation.

The foreground gate was kept: no injected keyboard/mouse events, foreground-lock changes or thread-input attachment bypasses. Both owned Explorer windows were confirmed closed, both isolated apps exited cleanly, tracking-off shutdown logs report ticks/failures/eligible/visits all zero, and their activity stores contain no days. Original binary/fixture hashes and each attempt's exact controller source/binary snapshot were verified. Fresh application tests: **1,267 passed, zero failed/skipped**; repaired local controller build zero warnings/errors. Those two blocked attempts provide no CPU, latency, visit-correctness or "imperceptible" comparison result; the subsequent operator capture is reviewed below.

Evidence: `.a5c/memory-tracking/explorer-1/`, `explorer-2/`, frozen `explorer-request.md`, controller snapshots, `explorer-blocked-summary.json`. Replay: `python .a5c/memory-tracking/analyze_explorer.py --verify-only`.

Thomas subsequently launched **`explorer-3/`** from his desktop terminal. It completed; do not rerun its fresh-output-only launcher over the existing evidence.

## 6. Operator capture — three complete off/on pairs

Capture on October 3 at approximately **17:42–17:48 UTC**, Windows build **26200**, runtime **10.0.12**, exact archived application hash above. One warmed synthetic Explorer window, 12 folders/32 text files per folder; six fresh isolated app processes in order **off/on, on/off, off/on**. The app was minimized in both cohorts. Each measured window lasted **44.23–44.86 seconds**, with the same 20 rapid navigations and three 10-second dwell navigations. **138 measured actions** total; 69 per mode. No new capture or application launch was performed during review. Review run: `01M41EEV8F5RTKS2RWQWDHQACQ`.

### Correctness and integrity

- All six rounds passed foreground and COM target/non-busy/32-item verification; all sampled holds retained owned-window foreground. Exact navigation order, initial configuration equivalence (except enabled/start time), and saved per-round versus combined results independently verified.
- Each on process logged **37 eligible ticks, zero probe failures and four visits**. All three required dwell folders received time and visits, with **no stored folder outside the synthetic tree**. The extra Folder-11 visit is consistent with starting a new process while Explorer remained on the prior round's final folder; shutdown counters include setup, unlike the CPU window.
- Off processes logged **zero ticks/eligible/visits**, and persisted no activity days. Across on processes: **111 eligible ticks, 12 recorded visits**. Actual persisted compact `s`/`v` values agree with the capture's audits.
- All six apps logged clean exits and unchanged normal startup registration. The owned Explorer window was confirmed closed; no forced app shutdown reported. Original fixture/binary and exact archived capture controller/source hashes verified; raw capture files preserved.

### Navigation and resources

Nearest-rank p95, per pair, milliseconds (off → on):

| Pair | Median | p95 | Maximum |
|---|---:|---:|---:|
| 1 | 211.57 → 210.38 | 287.31 → 274.18 | 336.12 → 320.86 |
| 2 | 215.35 → 211.85 | 378.29 → 288.50 | 465.72 → 419.33 |
| 3 | 194.42 → 217.46 | 374.39 → 317.69 | 383.89 → 327.94 |
| Pooled 69 actions/mode | **211.28 → 211.36** | **374.39 → 317.69** | **465.72 → 419.33** |

Pairwise median differences: **−1.19, −3.50, +23.04 ms**. **No consistent navigation slowdown** in this small workload; pooled central latency is nearly identical. The lower observed on tails are not a causal improvement or an optimization claim. Actions share folders/process/session and are not independent replications; the replication count is three pairs.

Whole QuickerPlaces process CPU, mean normalized to **one fully busy core**: **1.23% off → 3.84% on**, a **+2.61 percentage-point** increase. Every pair had more app CPU on, adding **0.98–1.30 seconds CPU** (mean **1.16 seconds**) over its ~44-second measurement. This is a measurable cost, not "free tracking". Explorer process CPU was broadly similar (**11.42–12.36% off**, **11.55–12.20% on** of one core). These are process aggregates, not stacks/GC/COM callback attribution or whole-machine Task Manager percentages; startup/shutdown are excluded, but initialization tails/background activity can still contribute. Do not divide the CPU delta by all 37 shutdown ticks to manufacture a per-tick cost.

Endpoint app private bytes ranged **118.68–126.38 MiB off / 122.52–123.75 MiB on**. Pair differences **−3.13, +3.83, −2.32 MiB** do not establish a retained-memory increase or reduction. This minimized, short navigation workload is **not comparable to the earlier ~166 MiB active-search workload**, which already reached that footprint with tracking disabled. Long-term growth/native/retained-root questions remain open.

### Conclusion and limits

The **synthetic local navigation/recording gate passed**. No navigation fix is justified by this evidence; pause further latency profiling unless Thomas reports visible stalls. The added whole-app CPU is the concrete resource observation to revisit if ordinary-use CPU/battery behavior warrants it, not an attributed defect.

This still does not prove physical-input-to-pixel latency or subjective "imperceptibility", nor cover large roots, network/offline folders, default idle/lock/resume or long-run retention. Recorded input-idle time reached **329.14 seconds** in the last on round—past the normal five-minute default—so the explicitly configured 60-minute diagnostic timeout matters. Automated COM navigation did not fake physical input. No real document/Recent Items scans or production changes were made.

Review artifacts outside the raw capture: `analyze_explorer_measured.py`, `explorer-measured-summary.json`, `explorer-measured-analysis.log`. Replay: `python .a5c/memory-tracking/analyze_explorer_measured.py --verify-only` (nearest-rank checks, independent correctness/config/log comparison and input hashes).

## Next scope decision

The initial populated, foreground synthetic Explorer comparison is complete; no repeat is needed merely to refine millisecond timings. The memory path can proceed with isolated stores and separate ordinary/endurance/native/root cohorts. More tracking diagnostics should be driven by actual visible stalls, CPU/battery concerns, larger workloads or deliberate idle/lock/stop and slow/offline requirements—not automatically launched. Do not lock the machine or probe real/offline shares automatically.

For real open-file/session scans, specify PDF/Office applications, document counts and local versus mapped/network storage. Synthetic held files cannot substitute for those acceptance cases. No fix is justified solely by being background work or having a nominal timeout.

## Evidence

Local-only `.a5c/memory-tracking/`:

- `request.md`, scoped process definition under `.a5c/processes/`, validation logs.
- `memory-capture.ps1`, `memory-1/`: current binary/harness/fixture hashes, environment, process/runtime CSVs, verified actions, phase/cycle boundaries, isolated stores/logs.
- `probe/`, `probe-capture.ps1`, `probes-1/`: built local diagnostic, hashes, three bursts, cadence and nine synthetic held scans, count-only JSON/logs and helper fixtures.
- `analyze.py`, `summary.json`, `analysis.log`: phase-interior summaries, historical table counts, limitations and input hashes. Replay: `python .a5c/memory-tracking/analyze.py --verify-only`.

Original assessment/optimization/input traces and ordinary binaries remain untouched. Application source/test/CI/CD diff and index are empty; documentation only is reviewable in git. No optimization or full real-tracking acceptance claim.

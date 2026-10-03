# QuickerPlaces: initial Windows memory and responsiveness baseline

> Historical baseline captured before optimization, 2026-10-02. Thomas subsequently authorized work on the recommended paths. See [Performance Optimization](261002_Performance%20Optimization.md) for implementation, comparisons and remaining work. The measurements and limitations below remain the original baseline.

## Status and scope

Assessment only; **no application-source, CI/CD, or shared-code changes**, no staging or commits. Read `CLAUDE.md` and `.a5c/project-profile.md`. Work is on `perf/memory-responsiveness-baseline`, revision `14605bae63d88f714a2625cea3b6d3a3c638f785`. Pre-existing `.gitignore` modifications and untracked onboarding files were left alone. New files are local assessment tooling, evidence and process records under `.a5c/`, not implementation changes or Pi-agent-folder files.

Three fresh-process baseline runs completed on 2026-10-02, approximately **20:04–20:25 EDT** (2026-10-03 00:04–00:25 UTC). All **2,826 UI Automation actions passed their checks**. Build succeeded; the existing Release test suite passed **1,258 tests**, with no skips or failures. Tests/build ran before measurement, not concurrently.

**Finding:** allocation/GC churn during workspace interactions is a stronger initial optimization target than idle memory or startup. Private memory increases after use, but this assessment does **not establish a memory leak**. No improvement claims are made.

## Environment

- Windows 11 Pro, x64, build **26200**, active local console session 1.
- Intel Core i5-12600K: 10 cores, 16 logical processors.
- Physical RAM: **15.75 GiB**. Ordinary desktop/background applications remained running; the machine was not a clean benchmarking image. Background process snapshot and available physical memory are in `environment.json`.
- NVIDIA GeForce RTX 3060; driver **32.0.15.9186**; display **2560×1440**, reported refresh **59 Hz**.
- Balanced Windows power plan; window DPI **96**.
- .NET SDK **10.0.401**, MSBuild **18.9.11**, .NET/WindowsDesktop runtime **10.0.12**.
- Release `net10.0-windows`, framework-dependent apphost, launched directly; no debugger or `dotnet run`.
- `dotnet-counters` and `dotnet-trace` **10.0.745401**, installed locally under `%LocalAppData%\Temp\qp-baseline-tools`, not added to the project.
- PowerShell/Windows UI Automation plus Win32 message-loop and GUI-resource probes. Process memory comes from `System.Diagnostics.Process`; runtime metrics come from `System.Runtime`.

## Corpus and repeatable workload

Each process uses `--workspace --data-root <isolated run-N-data folder>`. Normal saved stores, startup registration and the normal single-instance scope are not used or changed. Each run starts from the same fixture files, whose SHA-256 hashes are recorded. Run-specific writes remain inside the assessment data folders.

Synthetic, populated Desk layout, dark/green theme, fixed **1200×800 DIP** window at 100,100; expanded Saved places grid:

- **120 saved local folders**, 12 favourites, four team tags and notes.
- **12 sessions**, ten existing synthetic PDF paths each, 120 unique paths total. External applications are never opened; fixture PDFs are path fixtures, not a document-rendering benchmark.
- **12,000 place-open timestamps**, **480 session-open timestamps**.
- **60 recent files**, **10,800 recorded opens over 180 days**.
- Fixed corpus date anchor: 2026-10-02 16:00 UTC.
- Folder tracking has no roots; Recent Files tracking is **off**, while recorded history is populated. Global hotkey, startup and minimize-to-tray are disabled in the isolated settings.

This is a representative medium-sized **navigation/search/history** scenario, not evidence of Thomas's actual workload distribution. Tracking-on, real application/document discovery, UNC/offline drives, store mutations, document launching, panel editing, drag/drop, scrolling and hours-long endurance are not covered yet.

Each run:

1. Fresh process; measure main-window handle appearance and UI Automation readiness/120-place validation. OS/framework/file caches remain warm from build/pilots; **not reboot-cold startup**.
2. Attach runtime counters; **10 seconds settle**, then **30 seconds idle**.
3. Normal use: **15 cycles**, 135 actions, about **52–54 seconds**.
4. Sustained use: repeat the same cycle for **at least five minutes**, completing the last cycle. 89/90/90 cycles, or 801/810/810 actions.
5. **30 seconds recovery idle**, then clean shutdown. Each process lasts about **427–429 seconds**.

A cycle replaces search text with `Project 00` (10 places), `Project 001` (1), `no-match-baseline` (0), `Project 12` (3), then empty (120), validating the Saved grid's row count each time. It selects All → Sessions → Recent → Saved places, verifying selection, with **250 ms pacing after every action**. Queries are whole-text replacements, not character-by-character typing. Substring matches such as Project 100/112 are intentionally included in the expected counts. UIA operates on the isolated window, without simulated global keystrokes.

Before primary measurement, fixture/expected-count pilots exposed an invalid GUID format and two incorrect search assumptions; these were corrected in the assessment harness. Failed pilots are preserved separately and **excluded** from all tables.

## Metric definitions and limitations

- **Private MiB:** process private committed memory, not managed heap or total system footprint.
- **Working-set MiB:** resident process pages, including shared runtime pages; not private bytes. GPU allocations are not separately measured.
- Memory rows below are **per-run phase medians**, sampled once per second during idle and once per interaction cycle (roughly 3.3 seconds) during use. They are not exact instantaneous maxima. The OS peak working-set counter is reported separately.
- **Search completion:** wall-clock UIA `ValuePattern.SetValue` through retrieval of the expected Saved grid `GridPattern.RowCount`. Includes cross-process automation and dispatcher work. It does **not** prove asynchronous Library/calendar updates or pixels have finished rendering.
- **Tab completion:** UIA selection request through selected-state verification. It does **not** establish that every tab result/visual has finished loading.
- **Startup handle:** elapsed time from just before `Start-Process` to a nonzero main-window handle. **Automation readiness** includes several UIA tree lookups and corpus validation. Neither is first-frame or first-meaningful-paint time.
- **WM_NULL latency:** `SendMessageTimeout` on the main-window message loop, with a 1-second timeout. Mostly sampled after paced interactions; a useful diagnostic proxy, **not a busy-dispatcher or input-to-render benchmark**.
- Runtime counters are collected at **1-second intervals after startup** in all three primary runs. Their overhead and UIA's creation of automation peers are included and not independently quantified. No CPU/GC allocation trace runs concurrently with these baseline measurements.
- Heap figures sum the **last-GC heap-size gauges** across generations, including fragmentation. They are not continuously measured live/reachable object sizes. Gen0/gen1/gen2 collection counters overlap by generation: do not sum them as disjoint events. GC pause totals are sums of runtime pause-duration increments, not individual-pause percentiles.
- Only **three repeats**: standard deviations/ranges are descriptive, not confidence bounds. Natural collection timing and normal background activity contribute variability. The corpus must be rerun before expiry/pruning changes it; fixtures contain dated history.

## Startup

| Metric | Run 1 | Run 2 | Run 3 | Mean ± sample SD | Range |
|---|---:|---:|---:|---:|---:|
| Main-window handle, ms | 1,431 | 1,244 | 1,125 | **1,267 ± 154** | 1,125–1,431 |
| Automation-ready, ms | 4,169 | 4,206 | 3,729 | **4,035 ± 265** | 3,729–4,206 |
| Private MiB at ready | 131.8 | 130.9 | 128.7 | — | 128.7–131.8 |
| Working-set MiB at ready | 179.5 | 177.6 | 178.6 | — | 177.6–179.5 |

Readiness is a measurement proxy with substantial observer work; do not call this a four-second visible startup without a WPF render trace.

## Process memory

Each cell is **private / working-set MiB**, phase median:

| Scenario | Run 1 | Run 2 | Run 3 | Across-run mean of medians ± SD, private / WS |
|---|---:|---:|---:|---:|
| Idle | 130.0 / 181.1 | 129.2 / 179.0 | 127.7 / 181.1 | **129.0 ± 1.2 / 180.4 ± 1.2** |
| Normal use | 177.7 / 262.8 | 170.5 / 257.1 | 172.0 / 255.9 | **173.4 ± 3.8 / 258.6 ± 3.7** |
| Five-minute sustained use | 172.5 / 260.1 | 172.2 / 261.9 | 170.1 / 258.2 | **171.6 ± 1.3 / 260.1 ± 1.9** |
| Recovery idle | 171.5 / 260.1 | 175.3 / 259.5 | 169.4 / 258.6 | **172.1 ± 3.0 / 259.4 ± 0.8** |

- Private increase, initial idle → recovery median: **41.6 / 46.1 / 41.7 MiB**.
- OS peak working set: **278.9 / 276.4 / 275.5 MiB**.
- Sustained private-memory fitted slope: **+0.28 / +1.48 / +1.66 MiB/minute**. Short duration and collection variability prevent attributing these slopes to a leak.
- End handles: **748 / 753 / 753**; GDI objects **35 / 35 / 35**; USER objects **38 / 41 / 39**. No conclusion about leak absence from these small counts alone.
- There was **no collection during recovery idle**. The higher footprint may include live visuals/caches, heap capacity, dead-but-uncollected objects and fragmentation. A retained-object diagnostic is needed before any leak claim or GC-tuning proposal.

## Responsiveness

UIA wall-clock milliseconds; median / nearest-rank p95 / max. Normal phases contain 75 searches and 60 tab selections each; sustained phases contain 445/450/450 searches and 356/360/360 tab selections.

| Scenario / metric | Run 1 | Run 2 | Run 3 |
|---|---:|---:|---:|
| Normal search | 95 / 246 / 335 | 100 / 305 / 467 | 96 / 233 / 323 |
| Normal tab selection | 109 / 230 / 345 | 83 / 269 / 360 | 123 / 241 / 336 |
| Sustained search | 87 / 226 / 266 | 89 / 214 / 260 | 88 / 219 / 258 |
| Sustained tab selection | 113 / 232 / 282 | 112 / 221 / 251 | 109 / 223 / 243 |

Across-run p95 variation:

- Normal search **261.5 ± 38.5 ms**, range **233.4–305.5**.
- Sustained search **219.8 ± 5.9 ms**, range **214.2–225.9**.
- Normal tab selection **246.6 ± 19.9 ms**, range **229.9–268.6**.
- Sustained tab selection **225.0 ± 5.9 ms**, range **220.7–231.7**.

Pooled sustained operations distinguish the expensive actions:

| Action | Median | p95 |
|---|---:|---:|
| Clear search (show all places) | **201.6 ms** | **238.2 ms** |
| Return to Saved places | **202.9 ms** | **241.3 ms** |
| Select Recent | 150.7 ms | 185.9 ms |
| No-match search | 25.1 ms | 60.6 ms |
| Select All | 26.3 ms | 51.4 ms |

All message-loop probes succeeded without a 1-second timeout. Whole-run WM_NULL p95 was **4.14 / 3.41 / 3.19 ms**, but run 2 had a **388 ms** normal-use outlier. Fast between-action probes do not invalidate the much slower interaction measurements.

## Managed allocation, GC and CPU

| Sustained metric | Run 1 | Run 2 | Run 3 |
|---|---:|---:|---:|
| Allocation rate, mean MiB/sec | **76.22** | **74.95** | **74.93** |
| Last-GC heap, median MiB | 64.62 | 66.68 | 65.40 |
| GC committed, median MiB | 79.77 | 81.86 | 79.18 |
| Gen0 / Gen1 / Gen2 collections | 1,222 / 1,023 / 483 | 1,234 / 1,033 / 489 | 1,235 / 1,039 / 490 |
| Cumulative GC pause time, sec | **42.82** | **41.35** | **41.35** |
| CPU, percent of one logical core | **54.39%** | **53.16%** | **53.28%** |

Sustained allocation mean across runs: **75.37 ± 0.74 MiB/sec**, approximately **22 GiB allocated per five minutes** despite a roughly 172 MiB private footprint. GC pause total: **41.84 ± 0.85 sec**, roughly **14% of elapsed time**. These are allocation turnover/pause measures, not retained memory or GC CPU time. Host-wide CPU percentages are approximately one-sixteenth of the single-core percentages (about 3.3–3.4%).

Idle last-GC heap median is about **26.3 MiB**, with **41.8 MiB GC committed**. Normal-use allocation means are **72.0–73.7 MiB/sec**. Idle allocation means vary **0.16–1.79 MiB/sec**, substantially affected by initial collection/initialization timing; do not characterize steady idle allocation from those three short windows alone. Recovery allocation drops to **0.026–0.095 MiB/sec**, without a new collection.

## Separate diagnostic trace

`diagnostic.nettrace` is a **60-second**, post-startup repeated-interaction trace with `dotnet-sampled-thread-time,gc-verbose`, 32 MiB configured buffer. It is **excluded from all baseline aggregates**. The trace is approximately 357 MiB; derived ETLX/index files are also retained locally, not committed.

`dotnet-trace report` and a small external TraceEvent reader parsed it successfully. The initial PowerShell wrapper incorrectly interpreted a null process ExitCode as failure even though the tool's log said `Trace completed.`; artifact completion and parsing were verified, and the wrapper was repaired. There was no change to primary baseline measurements.

Allocation ticks total approximately **3.76 GiB** over the diagnostic interval. They are sampled allocation buckets, not precise per-type retained sizes or exact attribution of every allocated byte. Leading attributed tick types include:

- WPF `EffectiveValueEntry[]`: approximately **287 MiB**.
- WPF `InheritablePropertyChangeInfo[]`: approximately **214 MiB**.
- `String`: approximately **159 MiB**.
- `WeakReference`: approximately **121 MiB**.
- WPF `DependencyObject[]`, XAML writer frames and `BindingExpression` also rank highly.

Nearest resolved application frames include `LibraryViewModel.BuildCalendar` (approximately **254 MiB** of sampled buckets) and `BuildRows` (approximately **89 MiB**), with path normalization/query/history work also present. Most buckets resolve only to `App.Main` plus framework layout/template frames, so this is **not a complete attribution of allocations to application methods**.

Sampled **thread-time** reports show WPF `UpdateLayout`, measure/template/binding work and finalization, along with extensive waiting. Their percentages include waiting across threads and are **not CPU utilization percentages**. This supports investigation of visual churn; it does not prove a single root cause or a physical-input performance improvement.

## Live execution paths and proposed optimization targets

No target below is approved or implemented. Existing query work already uses `DispatcherBackgroundWork`; existing FileShelf grids already enable virtualization/recycling. Do not propose blindly moving the entire query to a background thread or merely setting virtualization flags.

### 1. Calendar/visual reconstruction and collection churn — highest priority

Live path: `App.OnStartup` → `MainWindow` → `WorkspaceView.Attach` → `LibraryViewModel.Refresh` → `BuildCalendar`/`BuildMonth` → YearActivityPanel's nested ItemsControls → WPF layout/templates. `LibraryViewModel.BuildRows` clears/refills changed row collections; FileShelf displays them.

Evidence: reproducible ~75 MiB/sec allocation, ~14% GC pause burden, WPF property/template allocations, calendar allocation frames, expensive clear-search/Saved-place return. `CalendarIsCurrent` already avoids some unchanged rebuilds, so first identify **which searches/source transitions legitimately change heat and which recreate equivalent visuals**.

Proposed experiment, after approval: profile genuine typed input and one action at a time; count calendar/row rebuilds and realized elements; preserve filtering/heat semantics while testing incremental calendar/row updates and stable visual reuse. Ensure the grouped grids have a finite viewport in the real workspace before changing virtualization.

### 2. Query/history/path-normalization allocation — next priority

Live path: search/tab change → `LibraryViewModel.QueryEdited/Refresh` → `LibraryQueryEngine.Run` → `LibraryIndex.Build`, history filtering/heat generation and `DocumentPaths`/`ResourceIdentity`. QueryEngine builds period and all-history indices; repeated identities/history arrays are visible in the allocation trace.

Proposed experiment: isolate a deterministic UI-free benchmark for the same snapshot and queries; determine whether normalized identities, immutable projections or heat results can safely be reused across search-only changes. Preserve generation/stale-result suppression, dates, tags, session scopes and retention. Keep shared logic WPF-free. This is an allocation hypothesis, not proof that this code dominates perceived stalls.

### 3. Post-use retention — diagnose before changing

Evidence: private memory stays ~42–46 MiB above idle after use, but recovery performs no GC. Next diagnostic: equivalent initial/post-use **managed retained-object snapshots** in a separate run, with any induced collection documented, plus longer 30–60-minute repeats and panel/window open-close cycles. Inspect roots of UI elements, automation peers, bindings, event handlers and calendar/row models. Do not force GC in production or call ordinary heap capacity a leak.

### 4. Startup — lower initial priority

Live path: `App.OnStartup` synchronously loads stores, constructs view models, themes and workspace before showing the window. Measure true first meaningful rendered/interactive UI with Visual Studio WPF Application Timeline or WPF/DWM ETW in PerfView; add reboot-cold/normal-store cases. Any initialization deferral must preserve the single-instance/recovery-before-mutation guarantees. Current handle/readiness proxies alone do not justify changing startup ordering.

## Provisional acceptance goals, not predicted savings

For a first scoped change, Thomas can approve or adjust these **experimental goals**:

- Sustained allocation **≤53 MiB/sec** (~30% below baseline).
- Five-minute GC pause total **≤30 seconds** (~28% below baseline).
- Sustained search and tab-selection p95 **≤150 ms**, while separately confirming physical-input/render behavior.
- Optional memory goal: sustained private median **≤150 MiB** (~13% reduction), but only after distinguishing live visual state from reclaimable/unused capacity.
- No regressions in corpus counts, history/heat/date/source filters, saved selection/layout, store recovery, CLI behavior or UI-free boundaries. No startup/idle memory regression beyond established variation.

Validate under equivalent Release build, runtime, corpus, DPI/window size, pacing, power plan and profiler settings. Use at least **five paired/interleaved before/after runs**, report per-run distributions/SD/ranges and real-input confirmation. Increase samples if differences overlap noise. A before/after trace with different instrumentation is not an improvement measurement. Add focused UI-free correctness/benchmark coverage and rerun the full solution tests. Stop for review before any application implementation; no CI/CD changes are needed.

## Evidence and replay

Primary artifacts, all under `.a5c/assessment/`:

- `baseline.ps1`, `fixture/`, `fixture-hashes.csv`, `build-harness-hashes.txt`.
- `environment.json`, `results.json`, `startup.json`, `memory.csv`, `actions.csv`.
- `counters-1.csv` / `-2.csv` / `-3.csv`, collector logs and empty error logs.
- `analyze.py`, `summary.json`, `summary.txt`, `action-detail.txt`.
- `trace.ps1`, `diagnostic.nettrace`, thread-time reports, `trace-allocations.json`, `trace-reader/`.
- `build.log`, `tests.log`, `baseline.log`, isolated per-run app logs.

From `C:\QuickerPlaces`, after **archiving all previous primary outputs and moving the assessment's `run-1-data`/`run-2-data`/`run-3-data` folders aside** (never the normal app data):

```powershell
# Rebuild/test before timing, never concurrently with measurement.
dotnet build src/QuickerPlaces.sln -c Release
Push-Location src
dotnet test QuickerPlaces.sln -c Release --no-build
Pop-Location
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .a5c/assessment/baseline.ps1
python .a5c/assessment/analyze.py
```

The harness intentionally refuses existing run roots. Do not reuse a modified post-run store as input. The separate trace also refuses an existing `diagnostic-data` folder; archive its outputs before replay. Its reader can be rerun with `dotnet run --project .a5c/assessment/trace-reader/TraceAnalysis.csproj -c Release -- <absolute diagnostic.nettrace path>`. Keep diagnostic captures out of primary timing comparisons and rerun dated corpus validation before drawing conclusions on a later date.

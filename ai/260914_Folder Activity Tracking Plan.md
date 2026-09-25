---
title: QuickerPlaces — Folder Activity Tracking Detailed Plan
status: design — not ready to implement until Phases 1 to 3 have landed
created: 2026-09-14
parent: ai/260901_Professional Improvements Plan.md
covers: sections 4.28 to 4.33 (Phase 9 — Opt-in root folder activity tracking)
last_revised: 2026-09-15 — longest period is a month, not a year (D16); monthly downsampling removed with it
---

# Phase 9 — Opt-in root folder activity tracking

## 0. How to use this document

This is the file-level and signature-level plan for Phase 9 of the [Professional Improvements Plan](260901_Professional%20Improvements%20Plan.md). The parent plan states *what* must be true; this document states *which files change, in what order, and how each requirement is proven*.

It breaks the `ai/README.md` convention that only the next phase gets a detailed plan, and does so deliberately: the design question — *can the application record the folders a user has opened under a chosen root, over a week and a month?* — was asked and answered in full now, and the answer contains enough Windows-specific detail (what the platform does and does not provide, and what the tracker must never do) that writing it down later would mean deriving it twice. Treat sections 1 and 4 as settled; expect section 5's file layout to need a pass against the code as it actually exists when the phase is reached.

Nothing in this document should be implemented before Phase 1 (persistence reliability) and Phase 3 (usage tracking) have landed. Phase 3 defines what an "open" means and establishes the honesty standard this feature depends on.

## 1. What this feature is, and what it cannot be

The user picks a **root folder**. From that moment on, QuickerPlaces records which folders under that root they visit in File Explorer, how often, and for how long — and presents that as a Week or Month view, a per-day breakdown to help with a timesheet, and a heat map of when the work happened. A month is the longest period the feature offers; D16 says why.

Two limits are structural, not implementation shortcuts, and both must be stated in the UI rather than buried here.

**There is no retroactive history.** Windows does not keep a per-user log of folders opened. What exists is shallow, undocumented, or both:

| Source | What it actually provides |
|---|---|
| `%AppData%\Microsoft\Windows\Recent\AutomaticDestinations\*.automaticDestinations-ms` | Undocumented OLE compound files, roughly 20 to 30 entries, mostly files. Already an explicit non-goal (parent §2). |
| `%AppData%\Microsoft\Windows\Recent\*.lnk` | The same shallow, self-pruning list. Days, not a year. |
| Directory `LastAccessTime` | Off by default on NTFS since Vista (`NtfsDisableLastAccessUpdate`), and when enabled it fires for the search indexer, antivirus and backup software too. It records that *something* touched the folder, not that the user opened it. |
| Directory `LastWriteTime` | Changes only when an entry is added, removed or renamed directly in that folder. Not browsing; not editing a file in a subfolder. A weak activity proxy at best. |
| USN change journal, audit policy | Real, but a change log rather than an access log, requires administrator rights, and wraps. |

So a root added today produces a full Month view a month from now. Every period is shown from the date tracking started, labelled with that date, and a partial or empty period says *"no data yet — tracking started on <date>"* rather than showing a misleading zero.

**Coverage is File Explorer, while QuickerPlaces is running.** The tracker observes shell windows. It does not see the file-open dialog inside Revit or Word, a third-party file manager, or anything that happens while the application is closed. Section 5.6 adds an optional tray/startup mode so "while running" can mean "all day", but the limit remains and the UI says so.

## 2. Scope

**In scope:** opt-in per-root tracking with a configurable rollup, a dwell threshold before a visit counts, foreground-and-active time accounting, a local activity store with a flat day-level retention window, Week, Month and per-day views, a heat map, CSV export for timesheets, **Add as Place** from any tracked folder, and a purge.

**Out of scope, deliberately:** any period longer than a month (D16), and the downsampling machinery that would need; reading Explorer's internal storage in any form; tracking anything outside a root the user explicitly added; tracking file *contents*, file names, or applications; any transmission of activity data anywhere; automatic creation of Places; automatic favourites or reordering (parent §2 keeps that non-goal); and any form of reporting designed for a second person to read. This is a tool for the person using the computer to see their own work. Section 8 states what that constrains.

## 3. Product constraints

- **Opt in per root.** No tracking exists until the user adds a root and confirms. There is no global "track everything" switch.
- **Visible while it runs.** A tracking indicator is present in the main window whenever a tracker is active, and names how many roots are being tracked.
- **Local only.** Activity data lives in `%LocalAppData%`, never in the roaming `places.json`, and is never included in a places export.
- **Reversible.** Disabling a root stops tracking; deleting a root deletes its data, immediately and completely, with a confirmation that says so.
- **Honest.** Recorded time is *time an Explorer window on this folder was in the foreground while you were active at the keyboard or mouse*. The UI uses that wording, or something equally plain. It is a prompt for filling in a timesheet, not a measure of billable work, and must never be presented as one.

## 4. Design decisions

These are settled here so they do not get re-litigated during implementation.

**D1 — Observe shell windows; never watch the file system.** Tracking uses the documented `ShellWindows` COM collection (Internet Explorer's `SHDocVw`, present on every supported Windows) to read the folder each open Explorer window is showing. A recursive `FileSystemWatcher` over a large root is rejected: it costs real CPU and I/O, overflows its internal buffer on a busy tree, and answers the wrong question — it reports changes, not attention.

**D2 — Poll adaptively rather than on a fixed timer.** One enumeration pass, with the handful of Explorer windows a person actually has open, costs well under a millisecond of CPU. The sampling rate is what decides the cost, so it varies: ~1.5 s while an Explorer window is in the foreground, ~15 s when it is not, and **no timer at all** while the session is locked, while the user has been idle past the threshold, or while no `explorer.exe` shell window exists. A fixed 1 s metronome is the only part of this design with a measurable power cost on a laptop, and this removes it.

**D3 — Event-driven observation is a later swap, not the first version.** `DShellWindowsEvents` (`WindowRegistered` / `WindowRevoked`) plus per-window `NavigateComplete2` sinks idle at zero cost, but need re-attaching whenever Explorer restarts and fail quietly when a sink is dropped. The poller goes behind `IShellWindowProbe` (5.1) so the event-driven implementation can replace it without touching the accounting, the store, or the UI.

**D4 — COM wrappers are released on every pass.** Every object obtained from `ShellWindows` — the collection, each window, each `IShellFolderViewDual`, each `Folder` and `FolderItem` — is released in a `finally` before the pass returns. Holding them pins references inside `explorer.exe`, and over an eight-hour session that shows up as Explorer's memory and handle count climbing. This is the single most common way this feature is implemented badly, and section 9 makes it a measured acceptance criterion rather than a hope.

**D5 — COM runs on its own STA thread, never on the UI thread.** A hung Explorer window must not be able to freeze QuickerPlaces. The probe owns a dedicated STA background thread; every pass has a timeout; a pass that times out is abandoned and logged (once per occurrence class, per D12), not retried in a loop.

**D6 — Time is attributed backwards, from a monotonic clock, with a cap.** Each sample attributes the elapsed interval since the previous sample to the folder that was foreground *at the previous sample*, measured with `Stopwatch` rather than wall-clock time. Any single attribution is capped at twice the current poll interval, and `SystemEvents.PowerModeChanged` (suspend/resume) plus `SessionSwitch` (lock/unlock) discard the gap entirely. Without the cap, closing a laptop lid for the weekend adds 60 hours to whatever folder was last on screen.

**D7 — Foreground only, and only while the user is present.** A visit accrues time only if its window handle matches `GetForegroundWindow()`, and only if `GetLastInputInfo()` shows input within the idle timeout (default 5 minutes). Three Explorer windows open in the background must not each bank a working day, and a folder left open over lunch must not report 50 minutes of work.

**D8 — The rollup is per root and user-chosen.** Three modes, because a jobs root and a reference library want different answers:

| Mode | Observing `C:\Jobs\Acme\Drawings\Rev3` under root `C:\Jobs` credits |
|---|---|
| `RootChild` (default) | `C:\Jobs\Acme` — the immediate child of the root |
| `Exact` | `C:\Jobs\Acme\Drawings\Rev3` — the folder itself |
| `Depth(n)` | the ancestor `n` levels below the root; `Depth(1)` is `RootChild` |

`RootChild` is the default because it produces a short, stable list that stays readable across a month without search. `Exact` is the honest answer to "what did I actually open" and is the right choice for a shallow root.

**D9 — A visit must survive a dwell threshold before it counts.** Default 5 seconds, configurable per root. Walking down a tree to reach one folder must not credit every folder passed through. Time accrues from the moment the threshold is met, not retroactively from arrival — under-counting by a few seconds is preferable to crediting folders that were only transited.

**D10 — Data is buffered in memory and flushed periodically, not written through.** `places.json` writes on every change because there are a few dozen a day; activity samples arrive thousands of times an hour. The store flushes every 5 minutes, on idle, on lock, and on exit. A crash loses at most the last 5 minutes of activity, which is the correct trade for this data — and it is stated in the user guide rather than presented as durable.

**D11 — Configuration and data are separate files.** Tracked roots, rollup modes and thresholds go in `settings.json` (already machine-local, and drive mappings make a root list machine-specific anyway); the recorded activity goes in its own `activity.json` with its own schema version. An activity write must never be able to fail, delay, or corrupt a places or settings save — the Phase 1 reliability work does not inherit a noisy writer.

**D12 — Tracker failures are silent to the user and visible in the log.** Explorer restarting, a COM call failing, a window reporting a path that no longer exists: none of these are the user's problem and none justify a dialog. They are logged through Phase 1's `DiagnosticLog`, rate-limited to one entry per failure class per session, and — per Phase 1's rule that the log holds no aliases or paths — recorded as the failure class and window count only, never the folder path.

**D13 — Non-filesystem shell locations are discarded, not guessed at.** *This PC*, *Recycle Bin*, Control Panel, a network location, an FTP site: `Folder.Self.Path` returns a `::{GUID}` shell path or a protocol string for these. Anything that is not a rooted filesystem path under a tracked root is dropped at the probe boundary.

**D14 — Ambiguous Explorer tabs are skipped rather than guessed.** Windows 11's tabbed Explorer can surface multiple entries sharing one window handle, with no documented way to tell which tab is frontmost. When more than one candidate maps to the foreground handle and they disagree, the sample is discarded. Losing a sample is a rounding error; attributing an hour to the wrong job is a wrong timesheet.

**D15 — Paths are normalized, and mapped drives are the user's call.** Comparison against a root is case-insensitive, separator- and trailing-separator-insensitive, and done on the full-path form. `J:\Jobs` and `\\server\share\Jobs` are *not* silently unified — the tracker does not resolve mapped drives behind the user's back. A root may instead carry an optional list of equivalent prefixes the user adds explicitly.

**D16 — A month is the longest period, and there are no rollups below it.** Decided 2026-09-15, replacing the original Week/Month/Year set. A Year view sounds free and is not: it forces a second, coarser storage tier, fold-at-the-boundary arithmetic, two representations of the same period that can disagree, and a heat map with 366 columns that has to be aggregated before it can be read. It also over-promises — a year of data only exists after a year of running (§1), so the view would be empty or misleading for most of the feature's life. A month covers what the feature is actually for: remembering last week, and filling in a timesheet at a month end. Day-level records with a flat retention window answer every offered period by summation, from one source of truth.

## 5. Work items

### 5.1 The probe seam and a testable tracker (parent 4.28)

| File | Status | Purpose |
|---|---|---|
| `src/QuickerPlaces/Services/Activity/IShellWindowProbe.cs` | new | One method: `IReadOnlyList<ShellWindowSnapshot> Sample()` |
| `src/QuickerPlaces/Services/Activity/ShellWindowProbe.cs` | new | The COM implementation (D1, D4, D5, D13, D14) |
| `src/QuickerPlaces/Services/Activity/ShellWindowSnapshot.cs` | new | `record(string Path, nint Hwnd, bool IsForeground)` |
| `src/QuickerPlaces/Services/Activity/IUserPresence.cs` (+ `UserPresence.cs`) | new | `TimeSpan IdleFor { get; }`, `bool SessionLocked { get; }` over `GetLastInputInfo` and `SessionSwitch` |
| `src/QuickerPlaces/Services/Activity/IMonotonicClock.cs` (+ impl) | new | `Stopwatch`-backed; the seam that makes D6 testable |
| `src/QuickerPlaces/Services/Activity/FolderActivityTracker.cs` | new | The accounting loop: dwell threshold, foreground/idle gating, rollup, suspend-gap discard |

The tracker takes all four seams as constructor parameters and contains **no COM and no P/Invoke**. Every behaviour in D6 through D9 and D14 is then a plain unit test that feeds it a scripted sequence of snapshots and clock readings. This is the whole reason the seam exists; a tracker that calls `GetForegroundWindow()` directly cannot be tested at all.

COM access uses `<COMReference>` to *Microsoft Internet Controls* (`SHDocVw`) and *Microsoft Shell Controls and Automation* (`Shell32`) with `EmbedInteropTypes=true`, so no interop assembly ships alongside the executable. Path extraction is `IWebBrowser2` → `Document` as `IShellFolderViewDual` → `Folder.Self` as `FolderItem` → `Path`, with the window handle from `IWebBrowser2.HWND`. If embedded interop proves awkward under single-file publish (Phase 8), the fallback is late binding through `Type.GetTypeFromProgID("Shell.Application")` and `dynamic` — same call sequence, same release discipline, no reference.

### 5.2 The activity store (parent 4.29)

| File | Status | Purpose |
|---|---|---|
| `src/QuickerPlaces/Services/Activity/IActivityStore.cs` | new | Record, query, prune, purge |
| `src/QuickerPlaces/Services/Activity/FileActivityStore.cs` | new | `%LocalAppData%\QuickerPlaces\QuickerPlaces\activity.json`, buffered per D10 |
| `src/QuickerPlaces/Models/Activity/ActivityDocument.cs` | new | Root persisted document, `SchemaVersion = 1` |
| `src/QuickerPlaces/Models/Activity/RootActivity.cs` | new | One tracked root's recorded data |
| `src/QuickerPlaces/Models/Activity/DayActivity.cs` | new | One day: per-folder totals plus the root's 24-hour histogram |
| `src/QuickerPlaces/Models/Activity/FolderTotal.cs` | new | `Seconds`, `Visits`, `LastSeenAt` |

Shape, elided:

```jsonc
{
  "schemaVersion": 1,
  "roots": [{
    "rootId": "e2b1…",                       // matches the configured root in settings.json
    "trackingStartedAt": "2026-09-14T08:12:00Z",
    "days": {
      "2026-09-14": {
        "hours": [0,0,0,0,0,0,0,0,420,3180,2460, …],   // 24 ints, seconds, root-level heat map
        "folders": { "C:\\Jobs\\Acme": { "s": 4321, "v": 7, "last": "2026-09-14T15:41:00Z" } }
      }
    }
  }]
}
```

Days are the only granularity stored, and every period is summed from them. Detail is kept for a retention window of 62 days by default — two months, so a full previous month is always available on any day of the current one — and days older than that are deleted outright. There are no monthly rollups and no downsampling, which is the practical dividend of D16: no fold-at-the-boundary arithmetic, no two sources of truth for the same period, and nothing to get wrong at a month end.

Per-day totals for the folders actually touched that day are small — a busy day is tens of folders, not hundreds — and the hour histogram is deliberately **root-level only**. A day-by-hour grid *per folder* is what makes this feature expensive: 24 buckets × every day × every folder is megabytes of JSON for a view nobody asked for. Root-level hours answer "when do I work"; day-level folder totals answer "what did I work on"; together they cover both views in a few hundred KB.

Pruning runs at load and once a day thereafter, inside the flush, never on the UI thread.

### 5.3 Configuration (parent 4.30)

`AppSettings` gains a `TrackedRoots` list and the schema version increments. Per root: `RootId`, `Path`, `Enabled`, `Rollup` (`RootChild` | `Exact` | `Depth`), `Depth`, `DwellThresholdSeconds` (default 5), `IdleTimeoutMinutes` (default 5), `EquivalentPrefixes` (D15), and `DayDetailRetentionDays` (default 62).

Settings are still saved on exit (Phase 1, 5.4 out-of-scope note) — but adding, disabling or deleting a root saves immediately, because losing a root the user just configured, or worse, resurrecting one they just deleted, is not acceptable at any reliability level.

### 5.4 Presentation (parent 4.31)

A separate **Activity** window, opened from the main window; the main window keeps its current shape, gaining only the tracking indicator (§3) and the menu entry.

- Root selector; period toggle **Week / Month**; a **Day** view with a date picker.
- Grid: Folder, Visits, Time, Last opened. Sortable. Time formatted `3h 12m`, never a raw seconds count.
- **Add as Place** on any row, routed through the existing `PlacesService.TryAdd` with the folder name as the default alias, and the existing validation and conflict messages. This is the action that makes it a QuickerPlaces feature rather than analytics.
- Heat map: day × hour grid for the selected period — at most 31 columns, which is why it stays legible without aggregation — from the root-level histogram, with a legend and a text alternative — a grid of coloured squares that only means something to a sighted user at full colour is not acceptable as the only presentation of the data.
- **Copy for timesheet** and **Export CSV**: the selected period's rows, day-stamped. User-initiated, always; nothing is written outside the data folder unless the user picks a destination.
- Empty and pre-tracking states carry the "tracking started on <date>" wording from §1.

### 5.5 Opt-in, indicator and purge (parent 4.32)

**Add root** opens a folder picker, then a confirmation panel that states plainly, before anything is recorded: what is recorded (folder paths under this root, visit counts, foreground-and-active time), what is not (file names, contents, applications, anything outside this root), where it is stored, that it never leaves the machine and is never exported with places, and that it only records while QuickerPlaces is running. The user confirms; nothing is recorded before that.

**Delete root** removes its configuration and its recorded data in the same operation, and says so in the confirmation. A "stop tracking but keep the data" option is `Enabled = false`, offered separately and labelled as such.

### 5.6 Background coverage (parent 4.33)

Tracking only while the main window is open covers a fraction of a working day, which makes the Week view misleading rather than useful. Two opt-ins, both off by default, both reversible from the same settings page:

- **Minimize to tray** — a `NotifyIcon` with Open, Pause tracking, and Exit. Pausing is visible in the icon's tooltip and the indicator.
- **Start with Windows** — an `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry, written only on opt-in and removed on opt-out. `HKCU` only; nothing here touches machine-wide state or needs elevation.

This is the largest single piece of user-visible behaviour change in the phase and the one most likely to annoy someone who did not ask for it. Hence: off, off, and clearly reversible.

## 6. Performance budget

These are acceptance criteria, measured on Windows before the phase is called done — not aspirations.

| Measure | Budget |
|---|---|
| One probe pass, 5 Explorer windows open | < 2 ms |
| Average CPU, tracking active, over an 8-hour session | < 0.1% of one core |
| `explorer.exe` handle count growth over an 8-hour session | none attributable to QuickerPlaces (D4) |
| QuickerPlaces private bytes growth, same session | < 10 MB |
| Timer wakeups while idle or locked | zero (D2) |
| `activity.json` at steady state, one root, daily use, 62-day window | < 500 KB |
| UI thread blocked by tracking | never (D5) |

If the first three cannot be met, the event-driven probe (D3) moves from "later swap" to "required", rather than the budget moving.

## 7. Test plan

Unit tests, `FolderActivityTracker` against scripted snapshots and a fake clock — no COM, no Explorer, no Windows-specific setup:

- A visit shorter than the dwell threshold records nothing; one that crosses it records from the crossing point (D9).
- A background window accrues no time while another window is foreground (D7).
- Idle past the timeout suspends accrual and resumes it on the next input (D7).
- A lock, a suspend, and a clock gap each discard the interval rather than attributing it (D6).
- A single attribution never exceeds twice the poll interval (D6).
- Each rollup mode maps a deep path to the correct folder, including a path that *is* the root and a root at a drive root (D8).
- A `::{GUID}` path, a URL, a relative path, and a path outside the root are all dropped (D13).
- Two disagreeing candidates on the foreground handle discard the sample; two agreeing ones record once (D14).
- Path comparison is case- and separator-insensitive; an explicitly configured equivalent prefix matches; a mapped drive that was *not* configured does not (D15).

Store tests, against a temp directory using the existing `TempDirectory` fake:

- Buffered writes flush on interval, on idle, and on exit; nothing is lost across a clean shutdown.
- Days older than the retention window are deleted; days inside it are untouched; a period query spanning the boundary returns only what is still stored, and says so rather than reporting a low total as fact.
- Round-trip of a document with unicode paths, a day with no folders, and a root with no days.
- A malformed or unreadable `activity.json` is quarantined and tracking restarts empty — it never blocks startup and never touches `places.json`. Activity data is not user-authored content; there is no recovery dialog for it.
- Purging a root removes every trace of it from the document.
- A store write failure is logged and dropped, and the next flush still succeeds (D11, D12).

Manual verification on Windows, because none of this can be proven in the repository's current environment (no .NET SDK, no Explorer):

- Every item in section 6, with the method and the numbers recorded in `BUILD_SUMMARY.md`.
- Explorer restarted mid-session: tracking recovers without a user-visible error.
- Windows 11 tabbed Explorer: switching tabs does not attribute time to the background tab.
- Two monitors, several windows, rapid switching: totals stay plausible.
- A long weekend with the machine suspended adds nothing to any folder.

## 8. What this must not become

Recorded separately from the non-goals in section 2 because it is a product boundary, not a scope boundary.

This is a tool for one person to see their own work. It is not an employee monitor, and the design must keep it from quietly becoming one: activity data stays on the machine, is never uploaded, never syncs with `places.json`, and is never included in an export the user did not explicitly perform themselves. There is no scheduled report, no aggregation across users, no silent mode, and no way to run it without the tracking indicator visible in the window. The feature is opt-in, per root, with an explanation shown before the first sample is recorded and a purge that actually deletes. Any future request to add a "manager view", a remote sink, a hidden mode, or reporting for anyone other than the person at the keyboard is out of scope by design, and should be refused on that basis rather than costed.

## 9. Order of work

1. `IShellWindowProbe`, the snapshot record, and `FolderActivityTracker` with its full unit test suite — no UI, no store, no COM.
2. `IActivityStore` and `FileActivityStore` with retention and purge, and their tests.
3. `ShellWindowProbe` (COM) behind the seam; measure section 6's first three rows before going further.
4. Configuration in `AppSettings` with its schema migration; add/disable/delete a root with immediate save.
5. The opt-in flow and the tracking indicator — nothing records before this exists.
6. The Activity window: grid, periods, Add as Place.
7. Heat map, Day view, CSV and clipboard export.
8. Tray and start-with-Windows, both off by default.
9. Manual verification pass on Windows; record the numbers; update `USERGUIDE.md` and `BUILD_SUMMARY.md`.

Steps 1 and 2 are the phase's real content and are fully testable in isolation. If the phase stalls after step 3 because the performance budget is not met, nothing user-visible has shipped and nothing has been recorded.

## 10. Definition of done

- A user can add a root, choose its rollup and thresholds, and see Week, Month and Day views populated from their own Explorer use.
- Every measure in section 6 has been taken on Windows and recorded.
- Every unit test in section 7 passes; every manual item has been performed and its result written down.
- Nothing is recorded before an explicit opt-in, the indicator is visible whenever tracking is active, and deleting a root deletes its data.
- An activity failure — a COM error, a corrupt `activity.json`, a full disk — cannot block startup, cannot produce a dialog, and cannot affect `places.json`.
- `USERGUIDE.md` documents what is recorded, what it means, what it cannot see, and how to turn it off and purge it, in the plain wording of §3.

## 11. Open questions

1. **Does `Exact` need a floor?** A root with thousands of leaf folders makes the Month view long under `Exact`. Less pressing now that a month is the longest period, but still real. Options: cap the stored folder count per root per day, roll the tail into an "other" bucket, or leave it and rely on Phase 7's search. Recommend deciding after real data exists; the store shape supports all three.
2. **Should a Place that is opened through QuickerPlaces also feed the activity store?** Phase 3 already counts those opens on the Place itself. Double-counting in two places with two different definitions would be confusing; not counting them leaves a gap when the user launches from a bubble rather than Explorer. Recommend: the launch opens an Explorer window, which the tracker then sees naturally — verify that is what happens before adding anything.
3. **Per-root or global tray/startup opt-in?** Section 5.6 treats background coverage as one application-level setting. If a user tracks one root for work and one for a hobby, they may want coverage only for the first. Deferred; the simpler shape ships first.
4. **Is 62 days the right retention default?** It is the smallest window that always contains a complete previous month. Someone who wants to look back at a quarter would need more, and nothing in the store shape prevents raising it — the cost is linear and small. Revisit once a real `activity.json` has a few months in it.

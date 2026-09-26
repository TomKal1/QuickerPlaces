---
title: QuickerPlaces — Folder Activity Tracking Detailed Plan
status: refreshed against the code on 2026-09-25 and ready to implement (next phase, roadmap §1.1) on claude/phase-9-folder-activity
created: 2026-09-14
parent: ai/260901_Professional Improvements Plan.md
covers: sections 4.28 to 4.33 (Phase 9 — Opt-in root folder activity tracking)
builds_on: ai/260925_Phase 3 Handoff.md §5; ai/260925_Phase 3 Detailed Plan.md D23–D33; ai/BUILD_SUMMARY.md
last_revised: 2026-09-25 — refreshed against the code after Phase 3 and against the user's answers (§0.1, D17–D26): late-bound COM, root configuration in activity.json, a GitHub-style year calendar replacing the hour heat map, tray and start-with-Windows in the first version, mapped-drive equivalence, timesheet export deferred. 2026-09-15 — longest period is a month, not a year (D16); monthly downsampling removed with it
---

# Phase 9 — Opt-in root folder activity tracking

## 0. How to use this document

This is the file-level and signature-level plan for Phase 9 of the [Professional Improvements Plan](260901_Professional%20Improvements%20Plan.md). The parent plan states *what* must be true; this document states *which files change, in what order, and how each requirement is proven*.

It breaks the `ai/README.md` convention that only the next phase gets a detailed plan, and does so deliberately: the design question — *can the application record the folders a user has opened under a chosen root, over a week and a month?* — was asked and answered in full now, and the answer contains enough Windows-specific detail (what the platform does and does not provide, and what the tracker must never do) that writing it down later would mean deriving it twice. Treat sections 1 and 4 as settled; expect section 5's file layout to need a pass against the code as it actually exists when the phase is reached.

Nothing in this document should be implemented before Phase 1 (persistence reliability) and Phase 3 (usage tracking) have landed. Phase 3 defines what an "open" means and establishes the honesty standard this feature depends on. *Both have landed; see §0.1.*

### 0.1 Refresh, 2026-09-25

Phases 1 to 3 are merged (PR #7), and the user moved this phase up to next (roadmap §1.1). This pass checked the plan against the code as it now is, and asked the user three questions. Sections 1 to 3, 8 and the decisions D1–D16 stand, except where D17–D26 (section 4) amend them. Sections 5, 6, 7, 9 and 11 were revised in place.

The user's answers, 2026-09-25:

- **First root: a mapped network drive** (for example `J:\Jobs`). Equivalence between a drive letter and its `\\server\share` path is needed from the first version (D22).
- **Tray and start-with-Windows: in the first version**, both off by default (D25).
- **Outputs: Week and Month views, Add as Place, and a GitHub-style heat map**: one bubble per day, weeks as columns and weekdays as rows, covering **a full year** (D20). Timesheet export (CSV and copy) was not chosen, and moves out of the first version (D24).

What the code check changed:

- `<COMReference>` cannot be resolved by `dotnet build`, which is how this project is built, on Windows and on Linux. So COM is late-bound (D17).
- `settings.json` saves on a best-effort basis, swallowing write errors and silently falling back to defaults when damaged. That is right for window chrome, and wrong for the user's tracked roots. So root configuration moves into `activity.json` (D18).
- Phase 2 and 3's rule is one injected `TimeProvider` for every wall-clock decision. It applies to day boundaries here (D19).
- The header has no room at 700 px for another button *and* an indicator, so one icon does both (D21).
- Phase 3 settled that a QuickerPlaces launch is counted on the place and not fed into this store. Its column name "Last opened" becomes **Last visited** here (D23).

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

**D11 — Configuration and data are separate files.** Tracked roots, rollup modes and thresholds go in `settings.json` (already machine-local, and drive mappings make a root list machine-specific anyway); the recorded activity goes in its own `activity.json` with its own schema version. An activity write must never be able to fail, delay, or corrupt a places or settings save — the Phase 1 reliability work does not inherit a noisy writer. *Amended by D18 (2026-09-25):* the roots' configuration moved into `activity.json` beside their data, because `settings.json`'s saves swallow failures. The separation from `places.json` and `settings.json` that this decision exists for is unchanged.

**D12 — Tracker failures are silent to the user and visible in the log.** Explorer restarting, a COM call failing, a window reporting a path that no longer exists: none of these are the user's problem and none justify a dialog. They are logged through Phase 1's `DiagnosticLog`, rate-limited to one entry per failure class per session, and — per Phase 1's rule that the log holds no aliases or paths — recorded as the failure class and window count only, never the folder path.

**D13 — Non-filesystem shell locations are discarded, not guessed at.** *This PC*, *Recycle Bin*, Control Panel, a network location, an FTP site: `Folder.Self.Path` returns a `::{GUID}` shell path or a protocol string for these. Anything that is not a rooted filesystem path under a tracked root is dropped at the probe boundary.

**D14 — Ambiguous Explorer tabs are skipped rather than guessed.** Windows 11's tabbed Explorer can surface multiple entries sharing one window handle, with no documented way to tell which tab is frontmost. When more than one candidate maps to the foreground handle and they disagree, the sample is discarded. Losing a sample is a rounding error; attributing an hour to the wrong job is a wrong timesheet.

**D15 — Paths are normalized, and mapped drives are the user's call.** Comparison against a root is case-insensitive, separator- and trailing-separator-insensitive, and done on the full-path form. `J:\Jobs` and `\\server\share\Jobs` are *not* silently unified — the tracker does not resolve mapped drives behind the user's back. A root may instead carry an optional list of equivalent prefixes the user adds explicitly.

**D16 — A month is the longest period, and there are no rollups below it.** Decided 2026-09-15, replacing the original Week/Month/Year set. A Year view sounds free and is not: it forces a second, coarser storage tier, fold-at-the-boundary arithmetic, two representations of the same period that can disagree, and a heat map with 366 columns that has to be aggregated before it can be read. It also over-promises — a year of data only exists after a year of running (§1), so the view would be empty or misleading for most of the feature's life. A month covers what the feature is actually for: remembering last week, and filling in a timesheet at a month end. Day-level records with a flat retention window answer every offered period by summation, from one source of truth.

*Amended by D20 (2026-09-25):* the Week and Month views still stop at a month. The one exception is a **year of per-day totals**, one number per root per day, kept only to draw the calendar heat map. That is a second tier, which this decision argued against. It is accepted because it holds one integer per day and never feeds the Week or Month numbers, so the two tiers can never disagree about any period those views show.

### Decisions added by the 2026-09-25 refresh

**D17 — COM is late-bound; there is no COM reference.** `<COMReference>` needs the full-framework MSBuild of Visual Studio; `dotnet build`, which this project uses on Windows and in Linux sessions, cannot resolve it (MSB4803). The probe creates `Shell.Application` through `Type.GetTypeFromProgID` and walks `Windows()` → `Document` → `Folder.Self.Path` and `HWND` through `dynamic`. That is the call sequence 5.1 already named as the fallback, now the only path. Nothing ships an interop assembly, and Phase 8's single-file concern about embedded interop disappears. D4's release discipline applies unchanged: every object obtained is passed to `Marshal.ReleaseComObject` in a `finally`.

**D18 — Tracked roots live in `activity.json` beside their data, not in `settings.json`.** This replaces the configuration half of D11. `settings.json` is saved best-effort: `SettingsService.Save` swallows write errors, and `Load` silently returns defaults for a damaged or newer file. Both are right for window bounds and wrong for a list the user built, where a failed save could resurrect a deleted root or lose a new one without a word. In `activity.json`:

- A root's configuration and its recorded days are in **one file**, so *delete a root and its data* (§3, 5.5) is one atomic write, not two files that can disagree.
- **Configuration changes** (add, disable, re-enable, delete, edit a setting) are written **at once**, through the same temp-file-and-replace pattern as `FilePlacesStorage`, and a failure is **reported** in the Activity window. Unlike activity samples, configuration is user-authored.
- **Activity data** is still buffered and flushed per D10, and a failed flush is still logged and dropped (D12).
- `settings.json` keeps only the two machine-wide switches of D25. `AppSettings` goes to version 4.
- A damaged `activity.json` is still quarantined, and tracking restarts empty without blocking startup (§7). Because configuration now lives there too, the Activity window says so in one line ("Your tracked roots couldn't be read and were reset; the damaged file was kept as …") instead of silently showing no roots.

**D19 — A day is the user's local calendar day, from the injected `TimeProvider`.** Durations come from the monotonic clock (D6). *Which day* an accrued interval belongs to comes from `TimeProvider.GetLocalNow()` in its `LocalTimeZone`, the same seam as Phase 2 and 3 (their D12). So tests pin the zone with `TestZones` and never read the machine's. An interval that spans local midnight is split between the two days. A daylight-saving day is simply 23 or 25 hours long. With the hour histogram gone (D20), nothing is keyed by clock hour.

**D20 — The heat map is a GitHub-style calendar of the last year.** It replaces the day × hour grid of 5.4.

- One bubble per day, **weeks as columns** (oldest left) and **weekdays as rows**, starting on the week start of the user's culture.
- Colour intensity comes from the day's total time, in five levels (none, then four quartiles of the root's non-zero days in view), with a legend.
- Each bubble's tooltip and accessible name give the date and total ("Tue 14 Sep 2026 — 3h 12m in 9 folders"). That is the text alternative §5.4 requires.
- Clicking a bubble opens the Day view for that date. For a day older than the 62-day detail window, the Day view shows the total and says the per-folder detail has expired.
- Storage: `activity.json` keeps a per-root `dayTotals` map (local date → seconds, visits) for **365 days**, beside the existing 62-day per-folder `days` (5.2). The root-level `hours` array is removed from the store. The totals are updated in the same flush as the details, from the same attributed intervals.

**D21 — One header icon opens the Activity window and is the tracking indicator.** An icon button joins the header's icon group: Segoe `E823`, the "recent" clock glyph. While any root is tracking, it carries a small accent dot, and its tooltip says "Activity — tracking 2 folders", or "paused". Its `AutomationProperties.Name` says the same. That meets §3's and §8's "visible whenever tracking is active" without a second control. The header already fills its 700 px minimum width, so it has no room for two. The "More" menu question stays with Phase 4 (Phase 2 plan §12, q1).

**D22 — A root on a mapped drive offers its network path as an equivalent.** This refines D15 without changing its rule that nothing is unified behind the user's back. When the chosen root is on a drive letter that `WNetGetConnection` reports as mapped (for example `J:` → `\\fileserver\projects`), the Add Root confirmation shows the equivalent network path and asks "Also count `\\fileserver\projects\Jobs` as this root?", **ticked**, since Explorer shows the same folder under either path depending on how it was reached. The user can untick it, and can add or remove equivalents later. A root chosen by its network path cannot infer drive letters, so the user adds those by hand. The P/Invoke lives in the app, behind an `INetworkDriveResolver` seam, so the tracker and its tests stay free of it.

**D23 — QuickerPlaces' own opens are not fed into this store, and the column is "Last visited".** This answers open question 2 of §11: a folder place launched from QuickerPlaces opens an Explorer window, which the tracker observes like any other. Phase 3's Opens (on the place, roaming) and this store's Visits (per folder, machine-local) are different measures (Phase 3 D24), never summed, and labelled apart. This grid's "Last opened" is **Last visited**.

**D24 — Timesheet export is deferred.** The user did not choose it on 2026-09-25. **Copy for timesheet** and **Export CSV** leave the first version, and 5.4 lists them as deferred. The store already holds what they need (per-folder, per-day totals for 62 days), so adding them later is a view change, not a schema change.

**D25 — Tray and start-with-Windows ship in the first version, as two switches in Settings, both off.**

- **Minimise to tray** uses Windows Forms' `NotifyIcon`. `UseWindowsForms` is turned on for it alone. `ImplicitUsings` is already off, so the WPF/WinForms name clashes the csproj comment warns about cannot arise implicitly; the one file that uses WinForms aliases what it needs.
- While the switch is on, closing the main window **hides** it instead of exiting. The tray menu has **Open**, **Pause tracking** / **Resume tracking**, and **Exit**. Exit runs the existing close path (`App`'s `Closing` handler: the unsaved-changes retry and the settings save) and then flushes `activity.json`.
- The second-launch and global-hotkey paths already call `BringToFront`, which must also show a window hidden to the tray.
- **Start with Windows** writes `HKCU\...\Run\QuickerPlaces` = `"<exe path>" --tray` on opt-in and deletes it on opt-out. `--tray` starts hidden in the tray, and only while the tray switch is on; otherwise it starts normally.

**D26 — The tracker runs on its own STA thread, and its flush is driven from there too.** D5's thread owns the probe, the adaptive timer (D2), and the 5-minute flush timer (D10). On lock and on suspend (`SystemEvents`) it both discards the gap (D6) and flushes. On exit, `App` asks it to stop and waits a bounded time, 2 seconds, for its final flush; a flush that cannot finish in time is logged and dropped, and never delays the close. Nothing in the tracker, the probe or the store touches the WPF dispatcher. The Activity window reads the store through snapshot queries, which take the store's lock briefly and copy out.

## 5. Work items

### 5.1 The probe seam and a testable tracker (parent 4.28)

| File | Status | Purpose |
|---|---|---|
| `src/QuickerPlaces/Services/Activity/IShellWindowProbe.cs` | new | One method: `IReadOnlyList<ShellWindowSnapshot> Sample()` |
| `src/QuickerPlaces/Services/Activity/ShellWindowProbe.cs` | new | The COM implementation (D1, D4, D5, D13, D14) |
| `src/QuickerPlaces/Services/Activity/ShellWindowSnapshot.cs` | new | `record(string Path, nint Hwnd, bool IsForeground)` |
| `src/QuickerPlaces/Services/Activity/IUserPresence.cs` (+ `UserPresence.cs`) | new | `TimeSpan IdleFor { get; }`, `bool SessionLocked { get; }` over `GetLastInputInfo` and `SessionSwitch` |
| `src/QuickerPlaces/Services/Activity/IMonotonicClock.cs` (+ impl) | new | `Stopwatch`-backed; the seam that makes D6 testable |
| `src/QuickerPlaces/Services/Activity/FolderActivityTracker.cs` | new | The accounting loop: dwell threshold, foreground/idle gating, rollup, suspend-gap discard, midnight split (D19) |
| `src/QuickerPlaces/Services/Activity/RootPathMatcher.cs` | new | Case- and separator-insensitive matching of a path to a root and its equivalent prefixes, and the rollup (D8, D15), as a pure function the tracker and the Add Root flow share |
| `src/QuickerPlaces/Services/Activity/INetworkDriveResolver.cs` (+ `NetworkDriveResolver.cs`) | new | `string? GetNetworkPath(string driveLetterPath)` over `WNetGetConnection` (D22); the implementation stays in the app, the interface is linked into the tests |
| `src/QuickerPlaces/Services/Activity/ActivityTrackingHost.cs` | new | The STA thread: adaptive timer, probe, tracker, flush timer, lock/suspend handling, bounded stop (D2, D5, D26) |

The tracker takes all four seams as constructor parameters (the probe, presence, the monotonic clock, and `TimeProvider` for the day, D19) and contains **no COM and no P/Invoke**. Every behaviour in D6 through D9, D14 and D19 is then a plain unit test that feeds it a scripted sequence of snapshots and clock readings. This is the whole reason the seam exists; a tracker that calls `GetForegroundWindow()` directly cannot be tested at all. `FolderActivityTracker`, `RootPathMatcher`, the seam interfaces, the snapshot record and the models are UI-free and linked into the test project, like `PlacesService`. The probe, `UserPresence`, `NetworkDriveResolver` and the host are app-only.

*Revised 2026-09-25 (D17):* COM is late-bound. `Type.GetTypeFromProgID("Shell.Application")` → `Windows()` → each window's `HWND` and `Document.Folder.Self.Path`, through `dynamic`, with every object released in a `finally`. The first version of this plan used `<COMReference>` with embedded interop types and kept late binding as the fallback. `dotnet build` cannot resolve COM references, so the fallback is now the design. The call sequence and the release discipline (D4) are unchanged.

### 5.2 The activity store (parent 4.29)

| File | Status | Purpose |
|---|---|---|
| `src/QuickerPlaces/Services/Activity/IActivityStore.cs` | new | Configure roots (immediate, reported), record (buffered), query snapshots, prune, purge a root |
| `src/QuickerPlaces/Services/Activity/FileActivityStore.cs` | new | `%LocalAppData%\QuickerPlaces\QuickerPlaces\activity.json`: configuration written at once, activity buffered per D10, temp-file-and-replace for both, quarantine on damage (D18) |
| `src/QuickerPlaces/Models/Activity/ActivityDocument.cs` | new | Root persisted document, `SchemaVersion = 1` |
| `src/QuickerPlaces/Models/Activity/TrackedRoot.cs` | new | One root's configuration *and* recorded data (D18) |
| `src/QuickerPlaces/Models/Activity/DayActivity.cs` | new | One day's per-folder totals (62-day detail) |
| `src/QuickerPlaces/Models/Activity/DayTotal.cs` | new | One day's root-level total, for the year calendar (D20) |
| `src/QuickerPlaces/Models/Activity/FolderTotal.cs` | new | `Seconds`, `Visits`, `LastSeenAt` |

Shape, elided. *Revised 2026-09-25:* configuration moved in (D18), `dayTotals` added and `hours` removed (D20). `days` and `dayTotals` are keyed by **local** date (D19):

```jsonc
{
  "schemaVersion": 1,
  "roots": [{
    "rootId": "e2b1…",
    "path": "J:\\Jobs",
    "equivalentPrefixes": ["\\\\fileserver\\projects\\Jobs"],   // D15, D22
    "enabled": true,
    "rollup": "rootChild", "depth": 1,
    "dwellThresholdSeconds": 5, "idleTimeoutMinutes": 5,
    "trackingStartedAt": "2026-09-26T08:12:00Z",
    "days": {                                                    // 62 days of detail
      "2026-09-26": {
        "folders": { "J:\\Jobs\\Acme": { "s": 4321, "v": 7, "last": "2026-09-26T05:41:00Z" } }
      }
    },
    "dayTotals": { "2026-09-26": { "s": 6120, "v": 11 } }        // 365 days, calendar only
  }]
}
```

Per-folder detail is kept for 62 days, the smallest window that always holds a full previous month, and every Week, Month and Day figure is summed from it. There are no monthly rollups and no downsampling (D16). `dayTotals` is the one exception (D20): one entry per day for 365 days, updated in the same flush from the same attributed intervals, and read only by the calendar. A year of it is a few kilobytes.

A folder is always recorded under the root's **own** path, even when Explorer showed it through an equivalent prefix: `\\fileserver\projects\Jobs\Acme` is stored as `J:\Jobs\Acme`. One folder is never split into two rows by the route taken to it, and **Add as Place** gets the path the user chose the root by.

Pruning runs at load and once a day thereafter, inside the flush, never on the UI thread.

### 5.3 Configuration (parent 4.30)

*Revised 2026-09-25 (D18, D25).* Per-root configuration lives in `activity.json` (5.2), not `AppSettings`. Per root: `RootId`, `Path`, `Enabled`, `Rollup` (`RootChild` | `Exact` | `Depth`), `Depth`, `DwellThresholdSeconds` (default 5), `IdleTimeoutMinutes` (default 5), and `EquivalentPrefixes` (D15, D22). Retention is fixed for now: 62 days of detail and 365 of day totals (§11, question 4).

Adding, disabling, re-enabling, editing or deleting a root saves immediately, and a failed save is shown in the Activity window. Losing a root the user just configured, or worse, resurrecting one they just deleted, is not acceptable at any reliability level. That is why configuration left `settings.json`, whose saves swallow failures (D18).

`AppSettings` goes to **version 4**, gaining only `MinimizeToTray` and `StartWithWindows` (both `false`). The Settings dialog gains the two check boxes, and saves at once as it already does for the hotkey. As with versions 2 and 3, no migration is needed: a version-3 file lacks both fields, which reads as off.

### 5.4 Presentation (parent 4.31)

*Revised 2026-09-25 (D20, D21, D23, D24).* A separate **Activity** window, opened from the header's Activity icon, which is also the tracking indicator (D21). The main window gains nothing else.

| File | Status | Purpose |
|---|---|---|
| `src/QuickerPlaces/Views/ActivityWindow.xaml` (+ `.cs`) | new | The window: root selector and root management, calendar, period grid |
| `src/QuickerPlaces/ViewModels/ActivityViewModel.cs` | new | Every decision the window makes, UI-free and linked into the tests, as Phase 2's `RecentlyDeletedViewModel` was |
| `src/QuickerPlaces/ViewModels/ActivityCalendar.cs` | new | The year's bubbles: week columns, weekday rows, the culture's week start, intensity levels, tooltips (D20); pure and tested |
| `src/QuickerPlaces/Services/Activity/ActivityFormat.cs` | new | `3h 12m` durations and the texts the window shows; pure and tested |
| `src/QuickerPlaces/Views/AddRootDialog.xaml` (+ `.cs`) | new | The opt-in confirmation of 5.5, with D22's network-path offer |

- **Root selector**, with **Add root…**, **Stop tracking / Resume**, and **Delete root and its data** (5.5).
- **Calendar heat map** (D20): the last 365 days, GitHub-style, with a legend. Each bubble has a tooltip and an accessible name. Clicking a bubble opens the Day view for that date.
- **Period**: **Week / Month** toggle with previous and next, plus the **Day** view reached from the calendar or a date picker.
- **Grid**: Folder, Visits, Time, **Last visited** (D23). Sortable, with the same header behaviour as the main grid. Time is formatted `3h 12m`, never a raw seconds count.
- **Add as Place** on any row, routed through the existing `PlacesService.TryAdd`, with the alias defaulted by `AliasSuggestion.FromFolderPath` (the deepest folder name, as the Add Folder dialog does since Phase 3), and the existing validation and conflict messages. This is the action that makes it a QuickerPlaces feature rather than analytics. It refreshes the main window's grid the way Import does.
- **Deferred (D24):** **Copy for timesheet** and **Export CSV**.
- Empty and pre-tracking states carry the "tracking started on <date>" wording from §1. Calendar days before that date are drawn as "not tracked", never as zero.

### 5.5 Opt-in, indicator and purge (parent 4.32)

**Add root** opens a folder picker, then a confirmation panel (`AddRootDialog`) which, for a root on a mapped drive, also offers its network path as an equivalent, ticked (D22). The panel states plainly, before anything is recorded: what is recorded (folder paths under this root, visit counts, foreground-and-active time), what is not (file names, contents, applications, anything outside this root), where it is stored, that it never leaves the machine and is never exported with places, and that it only records while QuickerPlaces is running. The user confirms; nothing is recorded before that.

**Delete root** removes its configuration and its recorded data in the same operation, and says so in the confirmation. A "stop tracking but keep the data" option is `Enabled = false`, offered separately and labelled as such.

### 5.6 Background coverage (parent 4.33)

Tracking only while the main window is open covers a fraction of a working day, which makes the Week view misleading rather than useful. Two opt-ins, both off by default, both reversible from the same settings page:

- **Minimize to tray** — a `NotifyIcon` with Open, Pause tracking, and Exit. Pausing is visible in the icon's tooltip and the indicator.
- **Start with Windows** — an `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry, written only on opt-in and removed on opt-out. `HKCU` only; nothing here touches machine-wide state or needs elevation.

This is the largest single piece of user-visible behaviour change in the phase and the one most likely to annoy someone who did not ask for it. Hence: off, off, and clearly reversible.

*Revised 2026-09-25:* the user asked for both in the first version. D25 settles the mechanics: WinForms `NotifyIcon`, close-to-tray only while the switch is on, Exit through the existing close path, and `--tray` for a startup launch.

| File | Status | Purpose |
|---|---|---|
| `src/QuickerPlaces/Services/TrayIcon.cs` | new | The `NotifyIcon`, its menu, and show/hide of the main window; the one file that uses Windows Forms |
| `src/QuickerPlaces/Services/StartupRegistration.cs` | new | Writes and removes the `HKCU` Run value |
| `src/QuickerPlaces/Models/AppSettings.cs` | changed | Version 4: `MinimizeToTray`, `StartWithWindows` |
| `src/QuickerPlaces/Views/SettingsDialog.xaml` (+ `.cs`) | changed | The two check boxes |
| `src/QuickerPlaces/App.xaml.cs`, `Views/MainWindow.xaml.cs` | changed | `--tray` start; hide-on-close while the switch is on; the tracking host's start and bounded stop (D26); `BringToFront` shows a hidden window |
| `src/QuickerPlaces/QuickerPlaces.csproj` | changed | `UseWindowsForms` |

## 6. Performance budget

These are acceptance criteria, measured on Windows before the phase is called done — not aspirations.

| Measure | Budget |
|---|---|
| One probe pass, 5 Explorer windows open | < 2 ms |
| Average CPU, tracking active, over an 8-hour session | < 0.1% of one core |
| `explorer.exe` handle count growth over an 8-hour session | none attributable to QuickerPlaces (D4) |
| QuickerPlaces private bytes growth, same session | < 10 MB |
| Timer wakeups while idle or locked | zero (D2) |
| `activity.json` at steady state, one root, daily use, 62 days of detail plus 365 day totals | < 500 KB |
| UI thread blocked by tracking | never (D5) |
| Close with tracking active, including the final flush | < 2 s added (D26) |

If the first three cannot be met, the event-driven probe (D3) moves from "later swap" to "required", rather than the budget moving.

*2026-09-25:* unlike the Phase 3 checklist, these measurements are not optional. A tracker that leaks handles in `explorer.exe` degrades the user's own desktop over a working day, and nothing else would show it. The first three are taken in step 3 of section 9, before any UI exists. The eight-hour rows are taken with the user's ordinary working day as the session: the host logs its pass timings and a handle-count sample every hour, so no one has to sit and watch.

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
- A folder seen through an equivalent prefix is recorded under the root's own path (5.2), so `\\fileserver\projects\Jobs\Acme` and `J:\Jobs\Acme` are one row (D22).
- *Added 2026-09-25:* a day is the local date in the injected zone; an interval across local midnight is split between the two days; a daylight-saving day accrues its 23 or 25 hours without error (D19, using `TestZones.CentralEuropean`).

Store tests, against a temp directory using the existing `TempDirectory` fake:

- Buffered writes flush on interval, on idle, and on exit; nothing is lost across a clean shutdown.
- Days older than the retention window are deleted; days inside it are untouched; a period query spanning the boundary returns only what is still stored, and says so rather than reporting a low total as fact.
- *Added 2026-09-25:* day totals are kept 365 days while folder detail is kept 62; a day between the two has a total and no detail; totals always equal the sum of that day's folder seconds while both exist (D20).
- Round-trip of a document with unicode paths, a day with no folders, and a root with no days.
- A malformed or unreadable `activity.json` is quarantined and tracking restarts empty. It never blocks startup and never touches `places.json`. There is no recovery dialog; the Activity window says in one line that the roots were reset, and names the kept file (D18).
- *Added 2026-09-25:* adding, disabling and deleting a root each write at once, and a failed configuration write is **reported**, never swallowed; deleting a root removes its configuration and every recorded day in one write (D18).
- Purging a root removes every trace of it from the document.
- A store write failure of *activity data* is logged and dropped, and the next flush still succeeds (D11, D12).

View-model and formatting tests (UI-free, *added 2026-09-25*):

- `ActivityCalendar`: 53 week columns ending with the current week, weekday rows from the culture's first day of week (en-US Sunday, en-GB Monday), untracked days before `trackingStartedAt` marked as such, five intensity levels from quartiles of the non-zero days, and each bubble's text (D20).
- `ActivityFormat`: `0m`, `45m`, `3h 12m`, and a day over 24 hours.
- `ActivityViewModel`: Week and Month sums from day detail; Last visited is the latest `last`; Add as Place defaults the alias from `AliasSuggestion` and reports `TryAdd`'s validation message; a failed configuration save shows its message.
- `AppSettings` version 4 reads a version-3 file with both switches off.

Manual verification on Windows, because none of this can be proven in the repository's current environment (no .NET SDK, no Explorer). *2026-09-25:* the repository now builds and tests on Windows and Linux; Explorer is still Windows-only.

- Every item in section 6, with the method and the numbers recorded in `BUILD_SUMMARY.md`.
- Explorer restarted mid-session: tracking recovers without a user-visible error.
- Windows 11 tabbed Explorer: switching tabs does not attribute time to the background tab.
- Two monitors, several windows, rapid switching: totals stay plausible.
- A long weekend with the machine suspended adds nothing to any folder.
- *Added 2026-09-25:* a root on the user's mapped drive: browsing the same folder via `J:` and via its network path records one row (D22).
- *Added 2026-09-25:* with **Minimize to tray** on, closing hides to the tray, tracking continues, the hotkey and a second launch bring the window back, and **Exit** closes cleanly. With **Start with Windows** on, sign-in starts it in the tray; turning the switch off removes the Run entry.

## 8. What this must not become

Recorded separately from the non-goals in section 2 because it is a product boundary, not a scope boundary.

This is a tool for one person to see their own work. It is not an employee monitor, and the design must keep it from quietly becoming one: activity data stays on the machine, is never uploaded, never syncs with `places.json`, and is never included in an export the user did not explicitly perform themselves. There is no scheduled report, no aggregation across users, no silent mode, and no way to run it without the tracking indicator visible in the window. The feature is opt-in, per root, with an explanation shown before the first sample is recorded and a purge that actually deletes. Any future request to add a "manager view", a remote sink, a hidden mode, or reporting for anyone other than the person at the keyboard is out of scope by design, and should be refused on that basis rather than costed.

## 9. Order of work

*Revised 2026-09-25.* Each step builds on Windows and Linux and leaves the suite green; it is the intended commit sequence.

1. The seams (`IShellWindowProbe`, `IUserPresence`, `IMonotonicClock`), the snapshot record, `RootPathMatcher`, and `FolderActivityTracker` with its full unit test suite. No UI, no store, no COM.
2. The models, `IActivityStore` and `FileActivityStore`: configuration writes that report, buffered activity, 62-day detail and 365-day totals, quarantine, and their tests.
3. `ShellWindowProbe` (late-bound COM, D17), `UserPresence`, and `ActivityTrackingHost` (D26), with a diagnostic harness that logs pass timings and handle counts. **Measure section 6's first three rows before going further.** Nothing is recorded yet, because no root can be added.
4. The Activity window's root management and `AddRootDialog`, with the opt-in panel and D22's network-path offer (`NetworkDriveResolver`), plus the header icon and indicator (D21). From here, tracking really runs.
5. The Activity window's periods: Week, Month and Day, the grid with Last visited, and Add as Place.
6. The calendar heat map (D20).
7. Tray and start-with-Windows: `AppSettings` version 4, the Settings check boxes, hide-to-tray, `--tray` (D25).
8. Manual verification on Windows; record the section 6 numbers; update `USERGUIDE.md`, `BUILD_SUMMARY.md` and a hand-off.

Steps 1 and 2 are the phase's real content and are fully testable in isolation. If the phase stalls after step 3 because the performance budget is not met, nothing user-visible has shipped and nothing has been recorded.

## 10. Definition of done

- A user can add a root, choose its rollup and thresholds, and see Week, Month and Day views and the year calendar populated from their own Explorer use, including on a mapped drive reached by either path.
- With the two switches on, tracking covers the working day from sign-in, from the tray.
- Every measure in section 6 has been taken on Windows and recorded.
- Every unit test in section 7 passes; every manual item has been performed and its result written down.
- Nothing is recorded before an explicit opt-in, the indicator is visible whenever tracking is active, and deleting a root deletes its data.
- An activity failure — a COM error, a corrupt `activity.json`, a full disk — cannot block startup, cannot produce a dialog, and cannot affect `places.json`.
- `USERGUIDE.md` documents what is recorded, what it means, what it cannot see, and how to turn it off and purge it, in the plain wording of §3.

## 11. Open questions

1. **Does `Exact` need a floor?** A root with thousands of leaf folders makes the Month view long under `Exact`. Less pressing now that a month is the longest period, but still real. Options: cap the stored folder count per root per day, roll the tail into an "other" bucket, or leave it and rely on Phase 7's search. Recommend deciding after real data exists; the store shape supports all three.
2. ~~**Should a Place that is opened through QuickerPlaces also feed the activity store?**~~ *Settled 2026-09-25 by D23:* no. The launch opens an Explorer window, which the tracker sees naturally. The manual pass confirms it does.
3. **Per-root or global tray/startup opt-in?** Section 5.6 treats background coverage as one application-level setting. If a user tracks one root for work and one for a hobby, they may want coverage only for the first. Deferred; the simpler shape ships first.
4. **Is 62 days the right retention default?** It is the smallest window that always contains a complete previous month. Someone who wants to look back at a quarter would need more, and nothing in the store shape prevents raising it — the cost is linear and small. Revisit once a real `activity.json` has a few months in it. *2026-09-25:* the calendar's 365 days of totals (D20) cover the "look back further" case for totals; this question is now only about per-folder detail.
5. *Added 2026-09-25:* **Timesheet export (D24).** Copy and CSV of a period's folder-by-day rows, deferred. Revisit once the Week and Month views have been used.
6. *Added 2026-09-25:* **The calendar's colour scale.** D20 uses quartiles of the non-zero days in view, as GitHub does. A single long day then doesn't wash out the rest. The catch is that the same colour means different hours in different years. Revisit with real data.

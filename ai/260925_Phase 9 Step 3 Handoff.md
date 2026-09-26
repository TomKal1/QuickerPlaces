---
title: QuickerPlaces — Hand-off, 2026-09-25 (Phase 9 steps 1–2 done; step 3 needs the user)
status: current as of 2026-09-25 — supersede with a new dated hand-off rather than editing this one
created: 2026-09-25
parent: ai/260914_Folder Activity Tracking Plan.md
supersedes: ai/260925_Phase 9 Step 2 Handoff.md (for where to work and what next; its §3 and §4 still apply). ai/260925_Phase 9 Handoff.md §3's conventions and traps still apply too.
---

# Hand-off — Phase 9 steps 1 and 2 done; step 3 needs the user

## 1. Where to work

- **Branch `claude/phase-9-folder-activity`**, not pushed. Step 1 is `12d765f` and `c648189`; step 2 is `982211f` (`RootPathMatcher.Normalize`) and `5bcf97f` (`ActivityStore`). Check `git log` and the remote first.
- State: **496 tests pass**; the solution builds with 0 warnings on Windows.
- Everything so far is pure logic. Nothing runs in the app yet, nothing touches COM, and no `activity.json` has ever been written on the user's machine.

## 2. What step 2 built

`Services/Activity/ActivityStore.cs`, over `IPlacesStorage`, with the models in `Models/Activity/`. Read plan **D32–D36** first. In short:

| Member | Use |
|---|---|
| `new ActivityStore(storage, timeProvider)` | Loads; never throws. `LoadOutcome`, `IsAvailable`, `Notice` say how it went (D33) |
| `Roots`, `EnabledRoots()` | Snapshots for the window; configs for `FolderActivityTracker.SetRoots` |
| `TryAddRoot`, `TryUpdateRoot`, `SetEnabled`, `DeleteRoot` | Configuration, written at once, failures returned (D18) |
| `Record(intervals)` | The tracker's `Tick()` output, into memory only |
| `Flush()` | Writes if anything changed; prunes on a new local day |
| `QueryPeriod`, `QueryDayTotals` | Copies for the Week, Month and Day views and the calendar |

## 3. Step 3 — tell the user before starting

Step 3 is `ShellWindowProbe` (late-bound COM, D17), `UserPresence`, the `Stopwatch` `IMonotonicClock`, and `ActivityTrackingHost` (D26). It ends with the **performance gate** (§6's first three rows, then the eight-hour rows). That gate needs the user to run a build through a normal working day, so agree how before writing code:

- **Which build and where.** A Debug build run from the repo is enough. It must not use the real data folder for places (Phase 9 hand-off §3), so the diagnostic harness should run the probe and the tracker only, with no store writes, or write a throwaway `activity.json` in a temp folder.
- **What is measured.** The host logs each pass's duration and, every hour, `explorer.exe`'s handle count and QuickerPlaces' private bytes. Nothing records folder paths (D12).
- **What the user does.** Starts it in the morning, works normally, closes it at the end of the day, and hands over the log.

Wiring the host must also:

- Build the real storage as `new FilePlacesStorage(<%LocalAppData%>\QuickerPlaces\QuickerPlaces, "activity.json")`. No factory exists yet, and tests must never call one.
- Call `Tick()` on the STA thread every `tracker.PollInterval`, pass the result to `store.Record`, and call `store.Flush()` every 5 minutes, on lock and suspend (after `tracker.DiscardGap()`), and on exit with a 2 s bound (D26). Test that timing in the host; the store already proves recording never writes.
- Give the tracker `store.EnabledRoots()` at start and after every configuration change.
- Stay off entirely when `store.IsAvailable` is false.

## 4. Open for the user

- **Plan §11, question 7 (D27):** whether alt-tabbing back to Explorer should count as a new visit. Still open; it doesn't block step 3.
- **Step 3's measurement day**, as in §3 above.

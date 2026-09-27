---
title: QuickerPlaces — Hand-off, 2026-09-25 (Phase 9 step 1 done; start step 2)
status: current as of 2026-09-25 — supersede with a new dated hand-off rather than editing this one
created: 2026-09-25
parent: ai/260914_Folder Activity Tracking Plan.md
supersedes: ai/260925_Phase 9 Handoff.md (for where to work and what next; its §3 conventions and traps still apply)
---

# Hand-off — Phase 9 step 1 done, step 2 next

## 1. Where to work

- **Branch `claude/phase-9-folder-activity`**, not pushed. On top of the three planning commits: `12d765f` (RootPathMatcher) and `c648189` (FolderActivityTracker), plus the commit adding this hand-off. Check `git log` and the remote first, since the user sometimes pushes and merges themselves.
- State: **414 tests pass** (85 new); the solution builds with 0 warnings on Windows.
- Read the plan's new **D27–D31** (§4, "Decisions made building step 1") before touching the tracker. They settle what a visit is, when the dwell counts, what a clock gap does, how ambiguity is judged, and what idle and lock do to a visit.

## 2. What step 1 built

All in `src/QuickerPlaces/Services/Activity/`, UI-free and linked into the tests:

| File | What it is |
|---|---|
| `RootPathMatcher.cs` | `Credit(path, root)`: the folder a path credits under a root (D8, D13, D15, D22), spelled with the root's own path, or null |
| `TrackedRootConfig.cs` | The tracker's immutable view of one enabled root: id, path, equivalent prefixes, rollup, depth, dwell, idle timeout. `RollupMode` is in `Models/Activity/` |
| `FolderActivityTracker.cs` | `SetRoots`, `Tick()` → `IReadOnlyList<ActivityInterval>`, `DiscardGap()`, `PollInterval` (1.5 s or 15 s) |
| `ActivityInterval.cs` | `(RootId, Folder, Date, Duration, StartsVisit, LastSeenAt)`: what the store sums |
| `IShellWindowProbe.cs`, `ShellWindowSnapshot.cs`, `IUserPresence.cs`, `IMonotonicClock.cs` | The seams |

Tests: `RootPathMatcherTests`, `FolderActivityTrackerTests`, `FolderActivityTrackerDayTests`, with `FakeShellWindowProbe`, `FakeUserPresence`, `FakeMonotonicClock` and `TrackerHarness` (steps both clocks together) in `Fakes/`. The cap, idle gating, the ambiguity check and the midnight split were each mutation-checked: removing one fails the tests meant to catch it.

## 3. What step 2 must respect from step 1

Step 2 is the store (plan §9 step 2, §5.2, D18, D20; tests in §7's second list). The tracker's output shapes it:

- **Accumulate sub-second durations.** `Duration` is a `TimeSpan`, typically 1.5 s. Flooring each interval to whole seconds would lose a third of all time. Keep a fractional or tick-precise running total in the buffer and round only what is written as `s`.
- **Key folders case-insensitively.** `Folder` starts with the root's own path, but the rest keeps Explorer's casing, and the tracker compares folders ignoring case. The store's per-day folder map should use `StringComparer.OrdinalIgnoreCase`, or one folder can become two rows.
- **Count a visit once, where `StartsVisit` is true.** That interval can have a zero duration. A visit whose time crosses midnight is counted on the first day only.
- **`LastSeenAt` is UTC**; `Date` is already the local date (D19), so the store never converts.
- The tracker never touches the store. The host (step 3) will pass each `Tick()` result to `IActivityStore`'s record method on the tracker's thread (D26), and build `TrackedRootConfig`s from the store's enabled roots for `SetRoots`.

## 4. Left for step 3 (the host and the COM probe)

- The `Stopwatch` implementation of `IMonotonicClock` was not written in step 1. It belongs with the host.
- **The probe passes every window through**, shell locations and outside-root paths included (D30). Filtering in the probe would hide a disagreeing foreground tab.
- The tracker picks 1.5 s or 15 s and does not sample while locked. D2's "no timer at all" while locked, idle past every root's timeout, or with no Explorer window is the host's decision.
- On lock and suspend the host calls `DiscardGap()` and flushes (D26). The tracker also discards when `SessionLocked` is true at a tick.
- `Tick()` lets a probe exception escape. The host catches it, logs it rate-limited (D12), and carries on.
- **Tell the user before step 3 starts**: it ends with the performance measurement gate (§6), which needs them to run the app through a normal working day.

## 5. Open for the user

- **Plan §11, question 7 (D27):** a visit ends whenever the folder leaves the foreground, so alt-tabbing between Explorer and another application counts a visit on each return. Time is unaffected. Changing it later touches only `FolderActivityTracker.NextVisit` and its tests.

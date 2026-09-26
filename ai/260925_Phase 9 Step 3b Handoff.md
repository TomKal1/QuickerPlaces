---
title: QuickerPlaces — Hand-off, 2026-09-25 (Phase 9 step 3 in progress)
status: current as of 2026-09-25 — supersede with a new dated hand-off
created: 2026-09-25
supersedes: ai/260925_Phase 9 Step 3 Handoff.md for what next (its §2 and §3 wiring notes still apply)
---

# Hand-off — Phase 9 step 3 half done

- Branch `claude/phase-9-folder-activity`, not pushed. **509 tests pass**, 0 warnings. Last commit `c3bf944`.
- Done in step 3: `ValidationResult` moved to its own file (`4d45278`), `StopwatchMonotonicClock`, `TrackingSignal`, and `ActivityTrackingLoop` (all the host's decisions, tested and mutation-checked; read its class comment).

**The user agreed (2026-09-25) to replace the eight-hour measurement day with a short session in VS on their personal PC.** Record this as **D37** in plan §6: the budget numbers stay; the method becomes a rapid-pass stress run (about 20,000 back-to-back probe passes, with `explorer.exe` handle totals and our private bytes sampled every 1,000, after a GC) plus a live check, a one-minute lock, and an Explorer restart. Also record **D38**: while idle, a presence check every 15 s instead of "zero wakeups", because the alternative is a system-wide input hook. The mapped-drive (D22) check is a later 10-minute run on the work machine.

Next, in order:
1. App-only, in `Services/Activity/`: `ShellWindowProbe` (late-bound `Shell.Application`, D17; its own STA worker thread; every COM object released in `finally`, D4; `GetForegroundWindow` via DllImport as in `GlobalHotkey.cs`; a pass timeout abandons the worker and starts a new one, D5; recreate `Shell.Application` after a failure), `UserPresence` (`GetLastInputInfo`; `SystemEvents.SessionSwitch` and `PowerModeChanged` raised as `TrackingSignal`s; unsubscribe on Dispose), and `ActivityTrackingHost` (a background thread: `loop.Wake(signals)`, then wait for `loop.NextWait` or a signal; `Stop` with a 2 s bound).
2. A dev-only console project `src/QuickerPlaces.ActivityProbe` (net10.0-windows, `UseWPF` for SystemEvents, files linked as the tests do; add it to the .sln). Never touches the real places or settings. Redirect `DiagnosticLog` to a temp folder. Modes: live view (paths on screen only), stress run, and a host run over a throwaway store in `%TEMP%` (root `C:\`, deleted at exit) that logs wakes while locked and the stop time. Writes one log with no folder paths. Show whether the debugger is attached.
3. Build it, then hand it to the user with steps (about 20–30 minutes: Ctrl+F5, live check, stress run, lock one minute, restart Explorer, close, send the log).
4. Record the numbers against §6, then steps 4–8 of plan §9.

Still open for the user: plan §11 question 7 (D27, whether alt-tabbing back counts as a new visit).

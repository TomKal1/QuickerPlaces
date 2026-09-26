---
status: Phase 9 steps 1–7 implemented locally; live application checks pending
branch: claude/phase-9-folder-activity
date: 2026-09-26
---

# Phase 9 hand-off — app verification

Read [the Phase 9 plan](260914_Folder%20Activity%20Tracking%20Plan.md) for decisions and the performance record. The tracker, activity store, Explorer probe, host, Activity UI, calendar, and optional tray/startup controls are implemented. The tray/startup checkpoint is `e164286` and the first documentation checkpoint is `9b9b032`. Nothing has been pushed. The solution builds with zero warnings and 532 tests pass. The Depth 2 check remains unresolved; see [the investigation summary](260926_Phase%209%20Depth%20Tracking%20Investigation.md) for the latest evidence and diagnostic instructions.

## What has already been checked

The developer probe's live events, 20,000-pass stress runs, lock handling, Explorer process restart recovery, and bounded shutdown were observed with the user on Windows. The strongest five-entry run had zero failures, a 0.017 ms maximum cached pass, a projected 0.0006% of one core at 1.5-second intervals, no sampled Explorer handle growth, and no private-byte growth. These are probe measurements, not a measurement of the finished WPF app running for a workday. Full numbers and caveats are in plan section 6 and `BUILD_SUMMARY.md`.

An attempted Computer Use launch of the finished WPF app on 2026-09-26 ended with an app approval timeout before a window was returned. No QuickerPlaces process remained afterward, and no live UI result is claimed from that attempt.

The user then ran the Activity UI and saved `C:\X` with Depth 2. Week view still showed one earlier `C:\X\2024` row. The saved store confirms the new setting, but the old row contains only the already-grouped folder key and cannot be split into `240015` later. The UI now explains this, and a grouping change resets the active visit. The follow-up is to observe a **new** qualifying visit under Depth 2; the screenshot did not yet show one.

The user then recreated `C:\X` at Depth 2 and still saw no new row. The live probe saw the test folder, and the app log showed no probe failures. The host's 15-second check while another app was foreground could miss a short Explorer visit; this is now 1.5 seconds while active, with idle and locked waits unchanged. A temporary Windows host recorded one real visit with 30 ticks and zero failures after the change, but used its default `C:\` root. Recheck the user's Depth 2 root in the rebuilt app before marking this fixed in the UI.

A follow-up temporary `C:\X`/Depth 2 host run happened after the desktop was idle past five minutes and therefore sampled only once. It cannot establish Depth 2 behaviour. For the next UI check, interact with Explorer using mouse or keyboard and keep `C:\X\2024\240011` foreground for at least 25 seconds, allowing the 15-second idle-return check plus the five-second visit threshold. Then leave Activity open up to 30 seconds for its view refresh. A new `C:\X\2024\240011` row should appear. The existing real activity root is already enabled at Depth 2; do not delete it again for this check.

## Short live check still needed

Run the **QuickerPlaces** project in Visual Studio, rather than the ActivityProbe. Use a small test root that you are comfortable recording. No root is tracked before you confirm one.

1. Open **Activity** in the header. Add the test root and confirm **Start tracking**. Use File Explorer in a child folder for longer than five seconds, then switch away and back. Check the Week and Day rows, Visits and Time. Switching back to the same folder should continue one visit, without crediting the time away. Open a different child folder and check that its row appears. Click a calendar day and **Add as Place** for one row; cancel the Add Folder dialog if you do not want a new Place.
2. In **Root settings**, use **Stop tracking** and confirm the data stays while new time stops; use **Resume tracking** to turn it back on. Check the grouping and threshold fields if convenient. **Delete root and its data** should remove the test root and its totals after confirmation.
3. Add a root again for the background check. In **Settings**, turn on **Keep running in the tray when I close the window**. Close the main window, then use the tray icon's **Open**, **Pause tracking**, **Resume tracking**, and **Exit** actions. Check that the hotkey and a second launch reopen the hidden window. If **Start with Windows in the tray** is enabled, verify a sign-in starts it hidden with a tray icon; turn the switch off afterward and confirm the startup entry is removed. This sign-in check can be deferred until the next normal sign-in.
4. On a machine with a mapped drive, add a mapped-root folder, accept the offered network equivalent, and browse the same child through the drive letter and its UNC path. It should appear as one row. This is a separate check if no mapped drive is available on the current machine.

Record each result as passed, failed, or untested in the Phase 9 plan and `BUILD_SUMMARY.md`. If an app crash or wrong total appears, fix it before closing the phase. Existing probe results do not substitute for the Activity UI or tray check.

## Constraints carried forward

- Activity is local, opt-in per root, and never part of Places export. Do not introduce automatic root enrollment or path-bearing diagnostic logs.
- `activity.json` stores root configuration and data together. Root changes report failed writes and can be retried. Activity samples buffer and flush; `places.json` remains independent.
- The COM event cache and host run off the WPF UI thread. The user accepted the short Visual Studio probe session as the performance gate (D37–D38).
- D39: returning from another app to the same Explorer folder continues the visit; the time away is not credited.
- Both background switches default off. The tray icon must stay visible whenever a hidden startup instance is running; Exit follows the existing bounded shutdown path.

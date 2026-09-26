---
status: Depth 2 manual check passed on 2026-09-26; original cause not isolated
branch: claude/phase-9-folder-activity
date: 2026-09-26
---

# Phase 9 Depth 2 tracking investigation

## Original failure and observed result

With `C:\X` tracked at **Chosen depth below root = 2**, a qualifying visit to `C:\X\2024\240015` or `C:\X\2024\240011` should be recorded under that exact two-level folder. The visit threshold was initially five seconds and is now one second; the idle timeout is five minutes. Earlier visits did not appear promptly in Activity.

The first `activity.json` snapshot confirmed `C:\X` was enabled with `rollup=depth` and `depth=2`. It contained `C:\X\2023\230108` but not the two 2024 child folders. A one-level key can legitimately be recorded when the visited folder is only one level below the root. The dwell threshold was later reduced from five seconds to **one second**.

After the Activity refresh change in `496aabb`, the user confirmed `C:\X\2024\240011` appears as its own row. A read-only check of the saved store also found **both** `C:\X\2024\240011` (one visit, 9.52 seconds) and `C:\X\2024\240015` (one visit, 2.00 seconds) under Depth 2 on 2026-09-26. This establishes the manual Depth 2 recording check for these folders. It does not establish which earlier condition caused them to be absent.

## What we tried

1. The user changed the grouping from **First folder below root** to **Chosen depth below root = 2**. The old `C:\X\2024` row remained. We confirmed the new setting was saved and explained that historical visits stored under the one-level key cannot be split into child folders retroactively. We changed grouping updates to end the active visit so a subsequent visit uses the new grouping, and clarified this in the UI (`9f32a55`).
2. The user deleted and recreated the `C:\X` root with Depth 2, then visited `240015` and `240011`. Neither appeared. At that point the recreated root was enabled, and the app log showed no Explorer probe failure. This removed historical grouping as a complete explanation.
3. We found that, while another app was foreground, the host checked Explorer every 15 seconds. A short Explorer visit might not reach the five-second threshold before the next check. We changed active, non-idle background checks to 1.5 seconds; the idle check remains 15 seconds and lock suspends the timer. A regression test covers a ten-second visit after switching apps (`fbc9e6c`). The user retested the rebuilt app and reports that the two 2024 folders still do not appear. **The timing change has not fixed this observed failure.**
4. The separate live COM probe saw `C:\X\2024\240015` in Explorer, in a different window handle from the parent folder, and marked it foreground in two samples. This shows Explorer exposed that path to the probe during that run. It does not prove the application's host sampled and recorded the visit.
5. A temporary developer host with its default `C:\` root recorded one real Explorer visit with 30 ticks and zero failures. This checked the basic host path but did not use the user's Depth 2 configuration. A later temporary `C:\X`/Depth 2 run recorded no visit because the desktop had passed the five-minute idle threshold; launching Explorer programmatically did not register as user input. That run is inconclusive for Depth 2.
6. Earlier developer-probe checks covered live Explorer events, 20,000 cached stress passes, lock/unlock, Explorer restart, and bounded shutdown. They passed their measured checks but do not establish that these two folders are recorded by the finished application.
7. The user reports `230108` appeared only after a delay, while `240011` remained missing. The Activity window previously refreshed its list every 30 seconds, so display delay is expected. The screenshot also shows Activity in front of Explorer; time spent with Activity in front cannot count as an Explorer visit. We updated the UI to state this and refresh immediately when it becomes active again, then once more two seconds later so the host has time to record Explorer's final interval. This explains possible test conditions, but does not prove why `240011` was absent.
8. The app log from the latest run showed a normal start at 2:50:07 PM and clean exit at 2:50:56 PM, with no reported probe failure. We added a path-free summary on host exit of ticks classified as no Explorer, Explorer in the background, outside the tracked roots, ambiguous foreground entries, user idle, or eligible, plus counted visits. The next run's summary can narrow the failure without recording folder names. This diagnostic has not yet been observed for the missing folder.

The earlier app log showed normal starts and exits with no reported probe failure. It did not capture why those first visits were absent. After `496aabb`, the solution built with zero warnings and 532 tests passed.

## Status

**Depth 2 manual check passed.** Both tested 2024 child folders have saved visits under the expected two-level keys, and the user saw `240011` in Activity. The path-free sample summary remains available if a missing-visit report recurs. No additional probe run is needed for this check.

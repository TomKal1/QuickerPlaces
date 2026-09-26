---
status: Unresolved — user retested and the two 2024 folders are still missing
branch: claude/phase-9-folder-activity
date: 2026-09-26
---

# Phase 9 Depth 2 tracking investigation

## Expected result and current failure

With `C:\X` tracked at **Chosen depth below root = 2**, a qualifying visit to `C:\X\2024\240015` or `C:\X\2024\240011` should be recorded under that exact two-level folder. The visit threshold is five seconds and the idle timeout is five minutes. The user has visited these folders and retested after the timing fix, but neither appears in Activity.

The current `activity.json` confirms that `C:\X` is enabled with `rollup=depth` and `depth=2`. Its stored folder keys include `C:\X\2023`, `C:\X\2024`, and **`C:\X\2023\230108`**. They do not include `C:\X\2024\240015` or `C:\X\2024\240011`. A one-level key can legitimately be recorded when the visited folder is only one level below the root. The `230108` key shows that Depth 2 can record a two-level folder in at least one case; it does not explain why the two 2024 folders are absent.

## What we tried

1. The user changed the grouping from **First folder below root** to **Chosen depth below root = 2**. The old `C:\X\2024` row remained. We confirmed the new setting was saved and explained that historical visits stored under the one-level key cannot be split into child folders retroactively. We changed grouping updates to end the active visit so a subsequent visit uses the new grouping, and clarified this in the UI (`9f32a55`).
2. The user deleted and recreated the `C:\X` root with Depth 2, then visited `240015` and `240011`. Neither appeared. At that point the recreated root was enabled, and the app log showed no Explorer probe failure. This removed historical grouping as a complete explanation.
3. We found that, while another app was foreground, the host checked Explorer every 15 seconds. A short Explorer visit might not reach the five-second threshold before the next check. We changed active, non-idle background checks to 1.5 seconds; the idle check remains 15 seconds and lock suspends the timer. A regression test covers a ten-second visit after switching apps (`fbc9e6c`). The user retested the rebuilt app and reports that the two 2024 folders still do not appear. **The timing change has not fixed this observed failure.**
4. The separate live COM probe saw `C:\X\2024\240015` in Explorer, in a different window handle from the parent folder, and marked it foreground in two samples. This shows Explorer exposed that path to the probe during that run. It does not prove the application's host sampled and recorded the visit.
5. A temporary developer host with its default `C:\` root recorded one real Explorer visit with 30 ticks and zero failures. This checked the basic host path but did not use the user's Depth 2 configuration. A later temporary `C:\X`/Depth 2 run recorded no visit because the desktop had passed the five-minute idle threshold; launching Explorer programmatically did not register as user input. That run is inconclusive for Depth 2.
6. Earlier developer-probe checks covered live Explorer events, 20,000 cached stress passes, lock/unlock, Explorer restart, and bounded shutdown. They passed their measured checks but do not establish that these two folders are recorded by the finished application.

The last inspected app log shows a normal start, one loaded tracked root, and a clean exit, with no reported probe failure. Silent failure to attribute a particular visit would not necessarily appear in that log. The solution previously built with zero warnings and 531 tests passed; those results do not override the failed manual check.

## Status and next diagnostic

**Unresolved.** The saved setting is Depth 2, and at least one two-level folder has been recorded, but the user's `240015` and `240011` visits have not. Do not mark the Depth 2 UI check passed or ask the user to delete the root again.

If investigation resumes, capture one controlled visit to a missing folder while the user is physically active, comparing the store before and after. At the same time, inspect the host's idle state and wake/tick decisions and the probe's foreground Explorer window/tab identity and path. This should locate where the visit is lost without adding observed folder paths to diagnostic logs. No such simultaneous capture has been done yet.

---
title: QuickerPlaces — Activity history kept for good, one file per month
status: step 1 (saving) implemented on branch ccr-f4ba4678-mo9ok7 (2026-10-06); the solution builds with 0 warnings and 1,419 tests pass on Linux; NOT yet run on Windows. Steps 2 and 3 not started
created: 2026-10-06
parent: ai/260914_Folder Activity Tracking Plan.md, ai/260928_PDF Project Sessions Plan.md
---

# Activity history kept for good

## 1. The request

Over 2026-10-05 and 06 the user asked for:

- recent folder and file history kept for much longer, *"potentially a whole person's career, up to 50 years"*;
- the history stored **by month**;
- **Documents** as the default place for it, not OneDrive, since not everyone has OneDrive;
- history that is **kept forever**;
- nothing the user has to think about;
- clicking a day, week or month older than what the app holds should **load that history automatically, and unload it** when the period is no longer selected;
- Recent Files using the same month files, instead of one year-long file.

## 2. Why folders and files differed

| | Kept in the working store | Decided in |
|---|---|---|
| Folder detail (time per folder per day) | 62 days | Folder Activity plan D16, 2026-09-15: "a month is the longest period", so 62 days, the smallest window that always holds a full previous month |
| Folder day totals (calendar only) | 365 days | D20, 2026-09-25, added for the year heat map |
| Recent Files opens | 365 days, max 500 per file | Sessions plan, 2026-09-28, for the Library's year strip |

The two features were designed two weeks apart with different goals, and the windows were never lined up. The user called it a miss, and it was one. This plan makes both work the same way: a short working store, and month files for everything.

## 3. The month files

`Documents\QuickerPlaces\History\2026-09 (DESKTOP-ABC).json` holds one month written by one PC (`AppDataFolders.History`). Under `--data-root` the folder is `<root>\History`. With no Documents folder, it falls back to `%LocalAppData%\…\History`. The folder is created only when the first file is written.

```json
{
  "schemaVersion": 1, "month": "2026-09", "machine": "DESKTOP-ABC",
  "roots": [ { "path": "C:\\Jobs",
               "days":   { "2026-09-01": { "folders": { "C:\\Jobs\\Acme": { "s": 3600, "v": 3, "last": "…" } } } },
               "totals": { "2026-09-01": { "s": 4500, "v": 4, "f": 2 } } } ],
  "files": [ { "path": "C:\\Jobs\\Acme\\A-101.pdf", "opens": [ "2026-09-01T01:00:00+00:00" ] } ]
}
```

- **Same shapes as `activity.json`** (`DayActivity`, `FolderTotal`, `DayTotal`).
- **Roots are matched by path**, because root ids differ between PCs.
- **Each PC writes only files with its own name.** A Documents folder synced or copied between PCs never has two writers on one file. Reading merges every PC: time and visits are added, and opens are joined.
- **Size:** roughly 30–150 KB a month, about 1–2 MB a year.

## 4. Saving: step 1, implemented

`ActivityHistory` (`Services/History/ActivityHistory.cs`, behind `IFolderHistory` and `IFileHistory`):

- **Saved before pruning.** `ActivityStore` and `RecentFilesStore` hand every day they still hold to the history *before* they prune: at load and in the first flush of each new local day. Both stores also prune at load, so a PC left off for three months still saves those months before deleting them.
- **If saving fails, nothing is pruned that day.** The data stays in the working store and is tried again the next day.
- **Once a day, everything held is saved.** This is not only the days about to expire, so a lost PC loses at most about a day, not 62 days.
- **Saving adds or replaces the given days and never deletes.**
- **A file is written only when its text changed.** Each own file is cached after its first read, so the daily save rewrites only the current month and the one before.
- **Damaged and newer files:**
  - one of this PC's files that can't be read is set aside as `….unreadable-<time>.txt` and the month starts again;
  - a file from a newer version is left alone, and the save reports failure, so the store keeps its data.
- **History can't be deleted from the app (H5).** **Delete tracked folder…** is gone from both menus; **Stop tracking** is the way to stop. **Remove from Recent Files** and **Delete Recent Files history…** clear the working list only, and the confirmation says the history is kept.
- **The log names counts and file names only, never a folder or a document**, as the stores' own log lines do.
- **`qp` and the developer ActivityProbe** build the stores without a history, so they never write one.

## 5. Reading: step 2, next

`ActivityHistory.ReadMonth(year, month)` and `Months()` exist and are tested. What remains:

- **Loading on demand.** In Recents (`ActivityViewModel`) and the Library (`LibrarySnapshot` / `LibraryQueryEngine`), choosing a day, week or month older than the working store loads just the months it touches. A week across a month boundary loads two. The data is released when the period changes, with the last 2 months kept in a small cache so arrowing day by day doesn't re-read files.
- **Year strip shading** for years past the 365 days of totals comes from the month files' `totals`. The year list already reaches 2100.
- **The "details have expired" notes** go away for any period the history covers.
- **`qp`'s `--days` limit (3650)** and its notes about each store's limits are updated.

## 6. Recent Files on the same footing: step 3

`recent-files.json` shrinks to:

- 62 days of opens;
- one line per file opened in the last year (path, last opened, total opens), for the undated Library list;
- daily counts for the year strip.

Older opens are read from the month files, as in step 2. The 500-opens-per-file cap goes. **Per CLAUDE.md, this is measured before and after** (load time and the app's private bytes with a year of opens), not assumed to help.

## 7. Decisions

| # | Decision |
|---|---|
| H1 | Month files, one per PC, in Documents\QuickerPlaces\History; kept forever (user, 2026-10-06). |
| H2 | Automatic, with no setting to turn it on: history is kept whenever Recents or Recent Files is tracking. |
| H3 | The stores save before they prune, and don't prune when saving fails. |
| H4 | The whole held window is saved daily, so a lost PC loses about a day. |
| H5 | History can't be deleted from the app (user, 2026-10-06). **Delete tracked folder…** is removed and **Stop tracking** stays. Clearing Recent Files clears its working list only. `ActivityStore.DeleteRoot` remains for its tests, and keeps history. |
| H6 | Nothing in the app ever deletes from the month files. |

## 8. Files (step 1)

| File | |
|---|---|
| `Models/History/HistoryMonthDocument.cs` | The month file |
| `Services/History/ActivityHistory.cs` | Saving, forgetting, reading and merging |
| `Services/History/HistoryFolder.cs` | The folder on disk (temp-file write, set aside) |
| `Services/AppDataFolders.cs` | `History` |
| `Services/Activity/ActivityStore.cs`, `Services/RecentFiles/RecentFilesStore.cs` | Save before prune; forget on delete |
| `App.xaml.cs`, `Services/Activity/ActivityTrackingHost.cs` | Wiring |
| `Views/ActivityWindow.xaml(.cs)`, `Views/Panels/FileShelfPanel.xaml(.cs)`, `ViewModels/ActivityViewModel.cs` | **Delete tracked folder…** removed (H5) |
| `ViewModels/LibraryViewModel.cs` | The Recent Files clear confirmation says history is kept |
| Tests | `ActivityHistoryTests`, `StoreHistoryTests` (25 tests) |

No schema changes: `activity.json` and `recent-files.json` are untouched.

## 9. Windows checklist (step 1)

1. With Recents tracking a folder, leave QuickerPlaces running past midnight, or change the clock. `Documents\QuickerPlaces\History` appears with this month's file (and last month's), named for this PC.
2. On the first run with existing data, files appear for each month `activity.json` holds totals for, up to 13. The months covered by the last 62 days have folder detail.
3. Right-clicking a tracked folder offers **Stop tracking** and no delete, in both the Folder Activity window and the workspace. **Delete Recent Files history…** empties the Library's recent files and leaves the month files unchanged.
4. Make the History folder read-only. The log says the history couldn't be saved, and the Folder Activity window still shows detail older than 62 days the next day, because nothing was pruned.
5. **Startup time and memory with 13 month files.** Measure first launch of the day (when the save runs) against a normal launch, three times each, using the steps in `261002_Performance Baseline.md`.
6. A PC name with spaces or brackets gives a safe file name.

Record each item as passed, failed or untested in `BUILD_SUMMARY.md`.

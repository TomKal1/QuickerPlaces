---
title: QuickerPlaces — Activity history kept for good, one file per month
status: steps 1 (saving), 2 (reading) and 3 (recent-files.json v2) implemented on branch ccr-f4ba4678-mo9ok7 (2026-10-06); the solution builds with 0 warnings and 1,435 tests pass on Linux; NOT yet run on Windows — the §9 checklist is open
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
- **Nothing recorded can be deleted from the app (H5).** **Delete tracked folder…**, **Remove from Recent Files** and **Delete Recent Files history…** are gone, along with their view-model and store methods (`ActivityStore.DeleteRoot` stays for its tests). **Stop tracking** and turning Recent Files off are the ways to stop recording.
- **The log names counts and file names only, never a folder or a document**, as the stores' own log lines do.
- **`qp` and the developer ActivityProbe** build the stores without a history, so they never write one.

## 5. Reading: step 2, implemented

**Reading API.** `IHistoryReader` (implemented by `ActivityHistory`, reached through `ActivityStore.HistoryReader`) offers:

- `MonthIndex()`: which months exist, and whether this PC and other PCs wrote them, from the file names alone;
- `ReadMonth(year, month, ownHeldFrom)`: one month merged across PCs, leaving out this PC's days on and after `HistoryCutoffs`. Those days are still in the stores, so nothing is counted twice.

**Which months are loaded.** `HistoryMonthCache` loads exactly the months a view asks for and lets every other month go. A month is needed when another PC wrote it, or when this PC wrote it and it starts before the cutoff the view needs:

- day totals and daily file counts for the year strip (the stores hold a year);
- folder detail for a chosen period, a search or a Saved filter (the store holds 62 days).

So with only this PC and the current year shown, nothing is read at all.

**`HistoryMerge`** turns loaded months into the shapes the views already use: folder days, day totals and file opens, matched to tracked folders by path.

**Recents (`ActivityViewModel`):**

- **Loading:** `LoadHistory()` runs on every period or year change and loads the year strip's months plus the period's.
- **Rows:** the period's rows are the store's plus history's.
- **Day totals:** add history's to the store's.
- **When tracking started:** whichever is earlier, this PC's start or the first day in the loaded history.
- **The year list** starts at the oldest history month.
- **"Folder details … have expired"** now appears only for days that have a total but no folders in the history either: days recorded before the history began.

**Library (`LibraryViewModel`, `LibrarySnapshot.WithHistory`, `LibraryQueryEngine`):**

- **Loading:** the months are loaded off the UI thread, with the query.
- **Changing year** runs the query again, so the old year's months are let go and the new year's loaded.
- **Folders that only another PC tracked** become extra, read-only roots in the query. They count in the strip, but get no root chips.
- **The list with no period** stays what the stores hold. History is listed only for a chosen period. It also always counts in the strip, and is used to tie the strip's filtered evidence to items.
- **Days counted as unknown:** in the heat and the period notes, a day counts as "folders unknown" only when history has no detail for it either.

**`ActivityCalendar.BuildYear`** takes `historyFrom`. Days back to it are no longer drawn as expired.

**Not yet:** `qp` (the CLI) still reads only the working stores. Its `--days` limit and notes are unchanged.

## 6. Recent Files on the same footing: step 3, implemented

### What changed

`recent-files.json` is now schema version 2. It keeps for files what `activity.json` keeps for folders (`RecentFilesStore`, `RecentFilesDocument`):

| | Version 1 | Version 2 |
|---|---|---|
| Each open | a year, max 500 per file | 62 days (`RecentFilesStore.DetailDays`, the same as `ActivityStore.DetailDays`), max 500 per file |
| Per file | — | `lastOpenedAt` and `openCount`, kept until a year after its last open: the Library's list with no period |
| Per day | — | opens and distinct files per kind (`days`, short names), a year (`RetentionDays`): the year strip where the opens have aged out |
| Older | deleted | in the month files (step 1) |

**Converting.** A version 1 file is converted at load: day counts and summaries are worked out from its year of opens, then all of those opens are saved to the history before pruning cuts them to 62 days. The conversion stays in memory until the next save (a recorded open or a settings change), because loading never writes (Phase 1). An older build then refuses the file as newer, and keeps Recent Files off.

**Duplicate opens.** The check that stops the same open being recorded twice now compares with `lastOpenedAt`, so it still works after a file's opens have aged out.

### The Library

`LibrarySnapshot` carries:

- **`FileSummaries`:** the list with no period;
- **`FileDayCounts`**, **`FilesDetailFrom`** and **`HistoryMonths`:** the months of history that were loaded.

Which source counts a day's file opens (`LibrarySnapshot.KnowsOpensOn`):

| The day is… | Counted from |
|---|---|
| on or after the store's 62-day cutoff | the store's opens, one by one |
| in a loaded history month | the history's opens, one by one. The day counts are ignored, so nothing counts twice |
| anywhere else | the day counts. These can be narrowed by kind, but not by a search, tag or scope |

Any filter but the kind therefore loads history months back to the 62-day cutoff, as folder detail does (`LibraryViewModel.Refresh`).

### `qp`

`files recent`, `folders recent` and `activity days` read the history too, through `CliContext.ReadHistory`. It is read-only, from `<data root>\History` under `--data-root`. `status` counts the listed files.

### Measured (CLAUDE.md)

`tools/RecentFilesLoadBench` loads `RecentFilesStore` from a synthetic year of Recent Files: 10,440 opens of about 1,500 PDF, Word and Excel files, seeded so every run is the same.

- **Measures:** load time, memory still held after a full GC, and bytes allocated while loading.
- **Each run** loads the store 9 times and reports the last 7.
- **Runs:** three for each mode.
- **Environment:** Linux cloud container, 4 cores, .NET 10.0.112, Release.

| | File | Load, median of each run (ms) | Held after GC | Allocated while loading |
|---|---|---|---|---|
| Before: v1, as the previous build keeps it (`before` on the previous build) | 463 KiB | 30.3, 38.5, 28.6 (range 27.5–54.9) | 497 KiB (one sample 509) | 8.3 MiB |
| After: v2, steady state (`after`) | 365 KiB | 22.7, 22.7, 22.1 (range 21.7–52.0) | 439 KiB | 6.1 MiB |
| One-off: v1 loaded by this build before its first save | 463 KiB | 33.3, 31.1, 32.9 (range 30.5–80.2) | 584 KiB | 9.9 MiB |

**What the numbers show.** For this workload the steady state is smaller and quicker to load:

- file: about −21%;
- held memory: about −12%;
- allocation: about −25%;
- load time: about −20 to −40%.

The gain is modest because the per-file summary still holds every file's path, and paths are most of the file. Until the first save after upgrading, each load converts the file and costs a little more.

**What they don't show.** These are not Windows numbers, and not the app's private bytes. The Windows comparison is §9 item 11.

## 7. Decisions

| # | Decision |
|---|---|
| H1 | Month files, one per PC, in Documents\QuickerPlaces\History; kept forever (user, 2026-10-06). |
| H2 | Automatic, with no setting to turn it on: history is kept whenever Recents or Recent Files is tracking. |
| H3 | The stores save before they prune, and don't prune when saving fails. |
| H4 | The whole held window is saved daily, so a lost PC loses about a day. |
| H5 | Nothing recorded can be deleted from the app (user, 2026-10-06). **Delete tracked folder…**, **Remove from Recent Files** and **Delete Recent Files history…** are removed. **Stop tracking** and turning Recent Files off stay. |
| H6 | Nothing in the app ever deletes from the month files. |
| H7 | History is loaded only for what is shown (the year strip and the chosen period) and let go after. This PC's days the stores hold are always read from the stores. |
| H8 | The Library's list with no period stays what the stores hold. History appears for a chosen period and in the strip. |
| H9 | Recent Files keeps opens for the same 62 days as folder detail, and per-day counts and per-file summaries for a year, so files and folders behave alike (user, 2026-10-06: "seems like a miss"). |
| H10 | `qp` reads the history read-only, with the same cutoffs as the app. |

## 8. Files

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
| `Services/History/HistoryMonthCache.cs`, `Services/History/HistoryMerge.cs` | Step 2: what is loaded, and merging it |
| `ViewModels/ActivityViewModel.cs`, `ViewModels/ActivityCalendar.cs` | Step 2: Recents reads history |
| `Services/Library/LibrarySnapshot.cs`, `Services/Library/LibraryQueryEngine.cs`, `ViewModels/LibraryViewModel.cs` | Step 2: the Library reads history |
| `Views/Panels/FileShelfPanel.xaml(.cs)`, `Views/Panels/RecentFilesSettings.xaml(.cs)`, `Services/RecentFiles/RecentFilesStore.cs` | H5: the Recent Files delete options removed |
| `Models/RecentFiles/RecentFilesDocument.cs`, `Services/RecentFiles/RecentFilesStore.cs` | Step 3: version 2 |
| `src/QuickerPlaces.Cli/CliContext.cs`, `ActivityCommands.cs`, `CliApp.cs` | Step 3: `qp` reads history |
| `tools/RecentFilesLoadBench/` | Step 3: the measurement |
| Tests | `ActivityHistoryTests`, `StoreHistoryTests`, `HistoryReadingTests`, `RecentFilesSummaryTests`, and one in `CliAppTests` (44 tests) |

No schema changes: `activity.json` and `recent-files.json` are untouched.

## 9. Windows checklist

1. With Recents tracking a folder, leave QuickerPlaces running past midnight, or change the clock. `Documents\QuickerPlaces\History` appears with this month's file (and last month's), named for this PC.
2. On the first run with existing data, files appear for each month `activity.json` holds totals for, up to 13. The months covered by the last 62 days have folder detail.
3. Right-clicking a tracked folder offers **Stop tracking** and no delete, in both the Folder Activity window and the workspace. **Delete Recent Files history…** empties the Library's recent files and leaves the month files unchanged.
4. Make the History folder read-only. The log says the history couldn't be saved, and the Folder Activity window still shows detail older than 62 days the next day, because nothing was pruned.
5. **Startup time and memory with 13 month files.** Measure first launch of the day (when the save runs) against a normal launch, three times each, using the steps in `261002_Performance Baseline.md`.
6. A PC name with spaces or brackets gives a safe file name.
7. **Step 2.** To fake an older month, copy a month file and change its name and dates to a year ago.
    - In Recents, the year list offers that year, and its strip is shaded.
    - Clicking a day lists its folders, with no "expired" note.
    - Going back to today, the Library and Recents memory doesn't stay higher. Compare private bytes before and after with `dotnet-counters`, three times.
8. In the Library, choose that day. Its folders and files are listed. **Clear date filter** removes the old files from the list.
9. Copy a second PC's month file (another name in brackets) into the folder. A day both PCs worked shows both PCs' time added together.
10. Type a search in the Library with a year of history in the folder. Typing stays smooth, because the months load off the UI thread.
11. **Step 3, upgrade:** back up `recent-files.json` from a version 1 build. Start this build:
    - the Library lists the same files with the same counts;
    - this year's strip looks the same;
    - the History folder has every month the old file covered.

    After opening one more file, `recent-files.json` says `"schemaVersion": 2` and is smaller. Measure the app's private bytes with the Library open, before and after the upgrade, three times each (`dotnet-counters`, as in `261002_Performance Baseline.md`), and record the results here.
12. `qp files recent --days 900` lists files from the history.

Record each item as passed, failed or untested in `BUILD_SUMMARY.md`.

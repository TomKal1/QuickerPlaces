---
title: QuickerPlaces — Project sessions, Recent Files and the Library
status: implemented on branch ccr-8d834d76-kqbdun (2026-09-28); builds with 0 warnings and 745 tests pass on Linux; NOT yet run on Windows — the §9 checklist is open
created: 2026-09-28
revised: 2026-09-28 — widened from PDF sessions to PDF, Word and Excel; Recent Files and the Library added at the user's request
parent: ai/260901_Professional Improvements Plan.md
---

# Project sessions, Recent Files and the Library

The file keeps its first name, *PDF Project Sessions Plan*, so links to it still work; it now covers three features that share code.

## 1. The requests

On 2026-09-28 the user asked for three things, in order:

1. *"A new feature that saves open PDFs to a tagable project session, that can then be seen and opened later."*
2. Whether Recents tracking could extend past folders to recent PDFs, Word and Excel files, sharing code with sessions, with a year view.
3. *"I would want the file tracking and the save open files to a session tag to be separate, just how the recent folders and current saved folders/links are now. With options to view them all together, to split them by folder, links, PDFs, word, excel, or view by tag."*

So there are three features, each separate:

| Feature | Is to files what… | Store | Switch |
|---|---|---|---|
| **Project sessions**: save the files open now as a named, tagged session; reopen it later | saved places are to folders and links | `sessions.json` (roaming) | None: sessions are saved by hand |
| **Recent Files**: record the PDF, Word and Excel files opened | Recents is to folders | `recent-files.json` (local) | Off until turned on |
| **Library**: everything together, by kind or by tag, with a year strip | — (reads the four stores) | None | — |

The user is reworking the UI in a separate branch that exists only on their machine. So the logic lives in UI-free services and view models, which that UI can bind to. The windows here are new files, and existing XAML changes are limited to two header buttons (§8).

## 2. Where it sits in the roadmap

- **It lifts one non-goal for these features only.** Roadmap §2 excluded "Tags, categories, workspaces". Sessions are tagged workspaces of files, and the Library groups by those tags. Places themselves stay untagged; in the Library they sit under "No tag".
- **It extends Phase 9's consent model to files.** Recent Files is opt-in, local-only and stated in the UI, like Recents. By default it records only files under folders the user already tracks in Recents.
- **It is not Phase 4.** A session's or Recent Files' document is not a *place*: it has no alias, never appears in the main grid, and opening one never counts as opening a place. A saved place opened from the Library does count, because it goes through `PlaceLauncher` (Phase 3 D23). Phase 4 is still next. `IShell.FileExists`, which Phase 4 planned, now exists.
- **It stays within "no PDF rendering".** No document is ever opened or read by QuickerPlaces. Only paths, and the times Windows says they were opened, are kept.

## 3. Document kinds and shared code

`Services/Documents/` holds what the three features share:

| File | What it is |
|---|---|
| `DocumentKind.cs` | PDF (`.pdf`), Word (`.docx .docm .doc .dotx .rtf`), Excel (`.xlsx .xlsm .xlsb .xls .xltx`), decided by extension only |
| `DocumentPaths.cs` | One spelling for a document path (as `RootPathMatcher` spells folders), plus the file name, stem and folder, without `Path.*` so tests run on Linux |
| `OpenDocumentResolver.cs` | Which documents are open, from the clues in §4 (pure) |
| `WindowsOpenDocumentProbe.cs` | Gathers those clues for the session scan (app-only) |
| `WindowsRecentItems.cs` | Reads Windows' Recent Items. It is shared by the session scan and Recent Files, and caches resolved shortcuts (app-only) |

`Services/JsonStoreLoader.cs` is the load classification that `activity.json`, `sessions.json` and `recent-files.json` all use: not present, unreadable, newer version, or damaged. It was pulled out of `ActivityStore`, whose tests pass unchanged. `ActivityCalendar.BuildYear` now also takes any per-day weight (`CalendarDay`), so the Library's year strip is the same code as Recents'.

## 4. Finding open files (for sessions)

No documented Windows API lists the documents another program has open. What an ordinary, unelevated program can see is combined, and the result goes to a review list — never saved unseen (D9).

| Clue | What it gives | What it misses |
|---|---|---|
| **Window titles** (`EnumWindows`) mentioning a document, and Word's and Excel's windows by class (`OpusApp`, `XLMAIN`) | The active document's name per window; some viewers show the full path. Word and Excel may leave the extension out ("Report - Word"), and that is matched only for their own windows and only to a file of their kind | Background tabs; viewers that show a PDF's embedded title |
| **Command lines** of those windows' programs | The file a program was started with | Files opened later in a single-instance program; a started-with file since closed |
| **Files held open** (`NtQuerySystemInformation` handle list, `GetFinalPathNameByHandle`) by those windows' programs — `260928_Held Files Detection Plan.md` | Every document Revu, Acrobat, Word or Excel holds, with its full path and mapped drive letter, including background tabs and files not in Recent Items | Programs that read a file and let go; an elevated program; Studio Session copies and program folders, left out on purpose |
| **Files in use** (Restart Manager) asked of every other candidate | Every document held open, including background tabs and other workbooks | Programs that read a file and let go |
| **Recent Items** (`FOLDERID_Recent` shortcuts) | Full paths and when each was last opened; how a bare name becomes a path | Programs that don't register recent documents |

Expected by program. Revu, Acrobat, Reader, Word and Excel hold their files open, so every document is found with its path from the held-files pass, whether or not it is in Recent Items. **Revu was checked with the developer probe on 2026-09-28:** several local PDFs open as tabs were all listed with full paths, in about 125 ms, without elevation. Mapped drives, DFS and Studio Sessions are not yet observed (§9). Acrobat, Word and Excel are expected to behave the same but are unchecked. Every clue's path is spelled with the user's mapped drive letter, so a file found by several clues is listed once. Edge, Chrome and SumatraPDF can show only the front tab, and only when its file is in Recent Items. A title that matches no known file is listed under the review list, for the user to add by hand.

## 5. Recent Files

- **What it records.** For each PDF, Word or Excel file: the times it was opened, taken from the last-write time of its Recent Items shortcut. It does not record time spent, the file's contents, or which program opened it. It is labelled "opened" and never "worked on".
- **When.** `RecentFilesHost` reads Recent Items once a minute while QuickerPlaces runs, and once when the Library opens. It resolves only changed shortcuts. It pauses with the tray's **Pause tracking**, flushes every five minutes and on exit, and watches nothing (no file-system watcher, no hooks).
- **Consent.** Off by default. Turning it on stamps `ResumedAt`, and nothing opened before that — or while it was off — is ever recorded (D15). The scope defaults to files under folders tracked in Recents, including their equivalent paths. **Anywhere** is a deliberate choice. Kinds can be narrowed to any of the three.
- **Recording once.** An observation counts only when it is later than that file's last recorded open, so reading the same shortcuts every minute adds nothing (D16). Two opens of one file between passes count once, because Recent Items keeps only the latest.
- **Storage.** `%LocalAppData%\QuickerPlaces\QuickerPlaces\recent-files.json`, machine-local like `activity.json`, never exported. Opens are kept 365 days, at most 500 per file. **Remove from Recent Files** forgets one file; **Delete Recent Files history…** forgets all of them and keeps the settings.

## 6. The Library

`Services/Library/LibraryIndex.cs` merges the four stores into one row per thing (D18). The merge key is kind plus location, ignoring case and separators. A folder saved as a place and visited in Recents is one row, with its alias. A file in two sessions and in Recent Files is one row, with both sessions' tags. `ViewModels/LibraryViewModel.cs` then provides the following:

- **Kind chips:** All, Folders, Links, PDFs, Word, Excel, each with its count under the other filters.
- **Show:** All, Saved (places and session files) or Recent (Recents folders and Recent Files).
- **Group by:** Type, or Tag, which makes one group per session tag. An item with two tags is in both groups, and untagged items come last under "No tag".
- **Search:** name, path, tag or session name.
- **The year strip** counts folder visits, files opened, and sessions saved and reopened, following the kind chip: Folders shows visits only, a document kind shows its opens plus sessions, and Links keep no history. Choosing a day lists only what was used that day; choosing it again clears it. Sessions now keep every reopen for a year (`OpenedAt`) for this.
- **Opening:** a saved place goes through `PlaceLauncher` and refreshes the main grid. Anything else is checked for existence and handed to Windows, and counts as nothing.
- **Recent Files' settings** sit in a panel at the bottom, because the Library is where Recent Files is seen.

## 7. Decisions

- **D1 — Sessions and Recent Files are their own stores, not places.** See §2.
- **D2 — Sessions roam; Recent Files stays on the machine.** A session is saved work; Recent Files is usage tracking, like `activity.json`.
- **D3 — All three smaller stores load alike, never asking**, through `JsonStoreLoader`. A damaged file is set aside and the store starts empty (Recent Files also starts *off*). An unreadable or newer file is left untouched and changes are refused.
- **D4 — Document paths are drive or UNC paths with a recognised extension,** normalised as folders are and compared ignoring case.
- **D5 — Session names are unique ignoring case**, 1–100 characters. **Tags** are comma- or semicolon-separated, at most 20 per session and 40 characters each, first spelling wins.
- **D6 — A title's name is matched to a known path, never guessed.** When several paths share it, the order is: started with, in use, most recent. The longest whole-word name wins. A name without an extension counts only at the start of a Word or Excel window's title, and only for that kind.
- **D7 — The session scan runs on request only.**
- **D8 — A slow share can't freeze a window.** One second per existence check and ten seconds per scan; the Recent Files pass runs on a timer thread and skips a pass rather than queue it.
- **D9 — Review before saving a session.** Files judged open start ticked; suggestions start unticked.
- **D10 — Reopening a session, or opening a file from the Library, is not a place open.** Opening a saved place from the Library is.
- **D11 — Missing files don't stop the rest.**
- **D12 — Logs carry counts only.** No names, tags, titles or paths.
- **D13 — Explorer's preview and indexing processes don't count as "in use".**
- **D14 — Recent Files and sessions are separate** (the user's direction): Recent Files never adds to a session, and saving a session records nothing in Recent Files. They only meet in the Library.
- **D15 — Nothing before consent.** Opens earlier than the last time Recent Files was turned on are ignored, although Recent Items still holds them.
- **D16 — An open is recorded once.** Only an observation later than the file's last recorded open counts.
- **D17 — Scope defaults to tracked folders.** It is the consent the user already gave Recents; **Anywhere** is opt-in.
- **D18 — One row per thing in the Library,** merging all sources, rather than one row per source.
- **D19 — Places stay untagged.** The tag view shows session tags; places appear under "No tag". Tagging places would be a separate decision (§10).
- **D20 — New UI in new files.** The two new windows copy the small styles they need rather than editing `Theme.xaml` or `ActivityWindow.xaml`, to keep the user's local UI branch mergeable.

## 8. Files touched outside the new ones

`App.xaml.cs` (stores and hosts), `Views/MainWindow.xaml(.cs)` (**Library** and **Sessions** header buttons), `ViewModels/MainViewModel.cs` (`NotePlaceOpened`), `ViewModels/ActivityCalendar.cs` (the `CalendarDay` overload), `Services/Activity/ActivityStore.cs` (uses `JsonStoreLoader`), `Services/IShell.cs`, `Services/WindowsShell.cs`, and the two `.csproj` files (links).

## 9. Manual checklist (Windows) — open

Nothing below has been done. Back up `%AppData%\QuickerPlaces` and `%LocalAppData%\QuickerPlaces` first if you want clean files.

**Sessions**
1. **Acrobat with three tabs, Word with two documents, and Excel with a workbook.** **Sessions → Save open files…** should list all six ticked. Save with two tags.
2. **Word with extensions hidden in Explorer** (title "Report - Word"): the document should still be found.
3. **Edge with a PDF** opened from Explorer: found if it's in Recent Items. A second Edge PDF tab is expected not to be found; check it's named under the list and **Add files…** it.
4. **Close everything, then Open all**: every file opens, and the session moves to the top.
5. **Move one file, then Open all**: the rest open, and the missing one is named.
6. **Explorer preview pane** on a PDF, without opening it: it should not be listed as open.

**Recent Files**

7. **Library → Recent Files → turn it on** with the default scope and no tracked folders: the status says nothing will be recorded. Add a tracked folder in Recents, open a PDF, a Word file and an Excel file under it, and reopen the Library: all three are listed as recent, and today on the year strip counts three files opened.
8. **Open a file outside the tracked folders**: it's not recorded. Switch to **Anywhere**, open it again: it's recorded.
9. **Untick Word**, open a Word file: it's not recorded. **Pause tracking** from the tray: nothing is recorded until resumed.
10. **Remove from Recent Files** on one row, then **Delete Recent Files history…**: both work, and sessions and places are untouched.
11. Does Word or Excel's **own File → Open** add to Recent Items (and so to Recent Files)? Record what you see.

**Library**

12. The kind chips, **Saved/Recent**, **Group by Tag** (a file in two tagged sessions appears under both tags), and search.
13. Click today on the year strip: only what was used today is listed; click it again to clear.
14. Open a saved place from the Library: the main grid's **Last Opened** and **Opens** update.
15. **Keyboard only** through both new windows.

**Held files** (`260928_Held Files Detection Plan.md`)

16. **Revu with three tabs, opened from Revu's own File → Open** (not from Explorer), one on a mapped drive: **Save open files…** lists all three ticked, "Open in Revu" (or Revu's program description), and the network one with its drive letter. *Local tabs passed in the developer probe on 2026-09-28; the dialog and the mapped drive are untested.*
17. **Revu with a Studio Session document open**: it is not listed.
18. **Revu closed, nothing else open**: the scan finishes with no warning and lists only recent suggestions.
19. **A PDF on a DFS path** (`net use` shows `\\company\dfs\…`): it is listed once, not twice.
20. **A document based on a network Word template**: the template isn't listed.
21. **(Only on a machine whose user folder has a short name, such as a user name over 8 characters.) A PDF opened from a zip in File Explorer**: it is not listed.

Record each item as passed, failed or untested in `BUILD_SUMMARY.md`.

## 10. Open questions

1. Should places be taggable, so the Library's tag view covers them too (D19)?
2. Should the Library remember its filters and grouping between openings?
3. Should the year strip's day selection also be offered in Recents, for folders?
4. Should Recent Files record time spent (as Recents does for folders)? It would need per-program adapters (the File Activity note), and nothing here attempts it.
5. Should reopening a session offer to close what is open first?
6. Should sessions or the Library be exportable?

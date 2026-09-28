---
title: QuickerPlaces — PDF project sessions
status: implemented on branch ccr-8d834d76-kqbdun (2026-09-28); builds with 0 warnings and 668 tests pass on Linux; NOT yet run on Windows — the §8 checklist is open
created: 2026-09-28
parent: ai/260901_Professional Improvements Plan.md
---

# PDF project sessions

## 1. The request

The user asked, on 2026-09-28: *"Could we add a new feature that saves open PDFs to a tagable project session, that can then be seen and opened later."*

So: find the PDFs open now, save them as a named session with tags, list the saved sessions (searchable and filterable by tag), and reopen a session's PDFs together.

## 2. Where it sits in the roadmap

- **It lifts one non-goal, for this feature only.** Roadmap §2 excluded "Tags, categories, workspaces". The user's request is a tagged workspace of PDFs, so that line no longer applies to sessions. It still applies to places: places are not tagged, and sessions do not add categories to the main grid.
- **It is not Phase 4.** Phase 4 (general file support) makes a file a *place*, with an alias, a row in the grid and usage counts. A session's PDFs are not places: they have no alias, never appear in the grid, and opening one never counts as opening a place (D10). Phase 4 is still next in §1.1 and is not changed by this work, except that `IShell.FileExists`, which Phase 4 planned to add, now exists.
- **It stays within "no PDF rendering".** QuickerPlaces never reads a PDF. It stores paths and asks Windows to open them with the default application, exactly as it opens a folder.
- **It is not Phase 9 and not the File Activity note.** Nothing watches in the background. The only observation is one scan, run when the user asks for it (D7), and it records nothing about time spent.

## 3. Storage

- `sessions.json` beside `places.json` in `%AppData%\QuickerPlaces\QuickerPlaces` (roaming): a session is portable user data, like a place (roadmap §3).
- Its own schema version (1) and its own file, so `places.json`, its migrations, and places export/import are untouched. Sessions are not included in **Export Places**.
- Written through `IPlacesStorage`/`FilePlacesStorage` (Phase 9 D32): temp file and replace, `sessions.bak.json`, `sessions.corrupt-*.json`.
- Document: `{ schemaVersion, sessions: [{ id, name, tags[], files[], createdAt, updatedAt, lastOpenedAt? }] }`. Timestamps are UTC `DateTimeOffset` (roadmap §3).

## 4. Finding open PDFs

No documented Windows API lists the documents another program has open. What an ordinary, unelevated program can see is combined, and the result goes to a review list — never saved unseen (D9).

| Clue | What it gives | What it misses |
|---|---|---|
| **Window titles** (`EnumWindows`) mentioning `.pdf` | The active document's *file name* per window; some viewers show the full path | Background tabs; viewers that show the PDF's embedded title instead of its file name |
| **Command lines** of those windows' programs (`NtQueryInformationProcess`, class 60) | The file a viewer was started with | Files opened later in a single-instance viewer; a started-with file that was since closed |
| **Files in use** (Restart Manager, `RmGetList`) asked of every candidate path | Every PDF held open by a viewer that keeps its files open, including background tabs | Viewers that read a file and let go |
| **Recent Items** (`FOLDERID_Recent` shortcuts, resolved with `IShellLink` without searching) | Full paths and when each was last opened; how a title's bare file name becomes a path | Programs that don't register recent documents |

Expected behaviour by viewer — **from how each is known to behave, not yet observed on the user's machine (§8)**:

- *Adobe Acrobat / Reader, Bluebeam Revu:* hold their files open, so every tab should be found through Restart Manager, and the active one through its title as well.
- *Microsoft Edge, Chrome, SumatraPDF:* only the active tab per window, through its title, and only when its file is in Recent Items or on the command line. Other tabs can't be seen; the user adds them with **Add PDFs…**.
- A title whose PDF name matches no known path is listed under the list as "Also open, but not matched to a file", so the user knows to add it by hand.

Explorer's undocumented `AutomaticDestinations` jump-list storage is **not** read (roadmap §2). Recent Items is the documented folder the File Activity note already proposed.

## 5. What was built

UI-free, linked into the test project:

| File | What it is |
|---|---|
| `Models/Sessions/ProjectSession.cs`, `SessionsDocument.cs` | The stored shape |
| `Services/Sessions/SessionStore.cs` | Load (classified as places.json is), create, update, delete, Last opened, tags in use, tag parsing, retry |
| `Services/Sessions/SessionPaths.cs` | One spelling for a PDF path, file name and folder; no `Path.*`, so it behaves the same on Linux |
| `Services/Sessions/OpenPdfResolver.cs` | Turns the four clues into the review list (§4) |
| `Services/Sessions/SessionLauncher.cs` | Reopens a session's PDFs, skipping and naming missing files; records Last opened |
| `ViewModels/SessionsViewModel.cs` | The Sessions window: rows, search, tag chips, selection, open, delete |
| `ViewModels/SessionEditorViewModel.cs` | The Save/Edit dialog: name, tags, tag suggestions, the ticked review list, save |

App-only:

| File | What it is |
|---|---|
| `Services/Sessions/WindowsOpenPdfProbe.cs` | Gathers §4's clues off the UI thread, with a 10-second budget and a 1-second limit per network existence check |
| `Views/SessionsWindow.xaml(.cs)` | **Project Sessions**: sessions on the left with search and tag chips; the selected session's PDFs on the right with **Open all**, **Open selected PDF**, **Edit…**, **Delete…**; **Save open PDFs…** in the header |
| `Views/SessionEditorDialog.xaml(.cs)` | **Save Open PDFs** / **Edit Session**: name, tags, the review list with ticks, **Find open PDFs**, **Add PDFs…**, **Remove from list**, **Tick all** / **Untick all** |
| `Views/MainWindow.xaml(.cs)`, `App.xaml.cs` | A **Sessions** button in the header beside **Recents**; the store is created at startup and retried once on exit |

`IShell` gained `FileExists`.

## 6. Decisions

- **D1 — Sessions are their own store, not places.** See §2. Keeps places.json's schema, export, and the "one launch gateway" rule intact.
- **D2 — Roaming, beside places.json.** Sessions are user data that should follow the user, unlike activity.json's machine-local tracking.
- **D3 — Load like activity.json, never ask.** A damaged file is quarantined and the list starts empty with a notice in the window; an unreadable or newer file is left untouched and every change is refused for the session. The startup prompt stays reserved for places.json.
- **D4 — PDF paths are drive or UNC paths ending in `.pdf`**, normalized as `RootPathMatcher` normalizes folders, compared ignoring case, each kept once per session.
- **D5 — Names are unique ignoring case**, like aliases, 1–100 characters. **Tags** are free text, comma or semicolon separated, up to 20 per session and 40 characters each, kept once ignoring case (first spelling wins); a leading `#` is dropped.
- **D6 — A title's file name is matched to a path, never guessed.** When several known paths share the name, the one the same program was started with wins, then one in use, then the most recently opened. The longest matching name wins within one title ("Set - A-101.pdf" over "A-101.pdf"), and a name must stand as a whole word.
- **D7 — Scan on request only.** The dialog scans when it opens for a new session and when **Find open PDFs** is pressed. Nothing polls.
- **D8 — A slow share can't freeze the window.** The scan runs on a worker; each existence check has one second, after which that server is skipped with a warning; the whole scan stops after ten seconds with what it has.
- **D9 — Review before save.** Files judged open start ticked; command-line-only and Recent Items suggestions (last 14 days, at most 30) start unticked; files added by hand start ticked. Only ticked files are saved.
- **D10 — Reopening is not a place open.** `SessionLauncher`, not `PlaceLauncher`, so Phase 3's usage counts are untouched. A session's own **Last opened** is recorded when at least one file opened, and the list is ordered by the later of Last opened and last change.
- **D11 — Missing files don't stop the rest.** Each is checked, missing ones are skipped and named, and one Windows refuses is reported without stopping the others.
- **D12 — Logs carry counts only.** No session name, tag, window title or path reaches the diagnostic log (Phase 3 D26).
- **D13 — Explorer's preview and indexing processes don't count as "in use".** `explorer`, `prevhost`, the search indexer, Defender and sync clients hold a PDF only to show, index or scan it.

## 7. Tests

121 new tests; the suite is 668. `SessionStoreTests` (validation, tags, update, delete, ordering, write failure and retry, damaged/unreadable/newer/hand-edited files, a real file with Unicode), `SessionPathsTests`, `OpenPdfResolverTests` (titles of five viewers, whole-word and longest-name matching, same-name choice, paths in titles and command lines including `file:` URLs, in-use background tabs, unmatched titles, the recent cap), `SessionLauncherTests`, `SessionsViewModelTests`, `SessionEditorViewModelTests`.

Not covered by automated tests: `WindowsOpenPdfProbe` (it calls Windows) and the two windows. They compile, XAML included, with zero warnings, but have **not been run**.

## 8. Manual checklist (Windows) — open

Nothing below has been done. Back up `%AppData%\QuickerPlaces\QuickerPlaces` first if you want to keep a clean `sessions.json`; the feature never touches `places.json`.

1. **Acrobat or Reader, several tabs.** Open three PDFs as tabs. **Sessions → Save open PDFs…** should list all three ticked ("Open in …" for the front one, "In use by an open program" for the others). Name it, add two tags, **Save**. It appears first in the list with its tags and "3 PDFs".
2. **Edge (or Chrome) with a PDF**, opened by double-clicking it in Explorer. It should be found ticked if it is in Recent Items. A second Edge tab with another PDF is expected **not** to be found: check the line under the list and **Add PDFs…** it.
3. **Bluebeam Revu**, if installed: as step 1.
4. **Close everything, reopen the session** with **Open all** (and with Enter, and a double-click on the card). All files open in their default viewer; the card shows "opened …" and moves to the top.
5. **Rename or move one of the PDFs**, then **Open all**: the rest open, and the missing one is named in red.
6. **Tags:** make a second session sharing one tag. The chips show counts; clicking one filters; **All tags** clears. Search finds by session name, tag, and PDF name.
7. **Edit…**: rename, change tags, untick a PDF, **Find open PDFs** to add what's open now, **Save**. **Delete…** asks first, and the PDFs themselves are untouched.
8. **A PDF on a network share** (and, if possible, with the share offline): the scan should finish within about ten seconds with a warning rather than hang.
9. **Explorer preview pane:** select a PDF in Explorer with the preview pane on, without opening it. It should *not* be listed as open (D13).
10. **Keyboard only:** Tab/arrows through the Sessions window and the dialog; Space ticks rows; Delete removes rows from the review list; Esc clears the search, then closes.

Record each as passed, failed or untested in `BUILD_SUMMARY.md`.

## 9. Open questions

1. Should a session also hold non-PDF files (drawings, Word, Excel)? The store and launcher would take any file with a small change; detection by title and Restart Manager is not PDF-specific. Left out because the request said PDFs.
2. Should **Save open PDFs** also be reachable without opening the Sessions window (a shortcut, or the tray menu)?
3. Should reopening a session offer to close what is open first, or to open only the files not already open? Today it opens them all; a viewer that already has one open usually just brings it forward.
4. Should sessions be exportable, like places?

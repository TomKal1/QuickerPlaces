---
title: QuickerPlaces — Session suggestions from files opened together
status: proposed (2026-10-14); not started. Open questions in §10
created: 2026-10-14
parent: ai/260928_PDF Project Sessions Plan.md
---

# Session suggestions from files opened together

## 1. The request

On 2026-10-14 the user asked: *"what if we add session suggestions based on files that the user opens together over a short period of time"*.

Today a session is made by hand: open the files, then **Save open files** (the on-demand scan), or **Save files as session** from the File shelf. People who open the same four drawings and a spec every morning still build that session themselves, or never build it. QuickerPlaces already records when each file is opened, so it can notice those groups and offer them.

## 2. What already exists

| Piece | Where | What it gives this feature |
|---|---|---|
| Recorded opens with real times | `RecentFilesStore.Record`; times come from the Recent Items shortcut's `LastWriteTimeUtc` (`WindowsRecentItems.cs`), not from the one-minute poll | When each file was opened, to within Windows' own resolution |
| 62 days of opens per file | `RecentFileRecord.Opens`, `RecentFilesStore.DetailDays`, `MaxOpensPerFile = 500` | Enough history to see a pattern repeat over weeks |
| A read of all of it | `RecentFilesStore.QueryHistory()` → `RecentFileHistory(Path, Kind, Opens)` | The only input the suggester needs |
| Path rules | `DocumentPaths.Normalize` / `DocumentPaths.Equal` (case-insensitive) | The same file identity sessions and the Library already use |
| Existing sessions | `SessionStore.Sessions` → `SessionSnapshot` | So suggestions never repeat a session the user already has |
| A review-before-save editor | `SessionEditorViewModel.ForFileSet(store, SessionFileSet)`, `SessionEditorDialog` | Save as session with the files ticked and the user free to change them |
| Store loading | `JsonStoreLoader.Load<T>` with `FilePlacesStorage` | Damaged/unreadable/newer-version handling for the one new file |

No new tracking, Windows API or background watcher is needed.

## 3. Decisions

| # | Decision | Why |
|---|---|---|
| G1 | **Suggestions come only from Recent Files' recorded opens.** No new tracking, no use of the open-documents scan. | Recent Files is already opt-in, scoped (tracked folders by default) and consented to. The scan runs only when the user asks (sessions plan §4) and stays that way. |
| G2 | **Off by default, and only offered while Recent Files is on.** The switch sits under Recent Files' own settings. | Same consent model as everything else that reads what the user opens. With Recent Files off there is no input, so the switch says why it's disabled. |
| G3 | **A group is files opened in the same burst on several different days.** A burst ends after a 15-minute gap with no open. Pairs are counted by **days**, not bursts. | One busy morning re-opening two files is not a habit; the same files together on three separate days is. |
| G4 | **Files that go with everything are left out of pairs.** A pair counts only when its days together are at least half of the rarer file's active days (share ≥ 0.5). | A daily timesheet or standards PDF would otherwise join every group. |
| G5 | **Thresholds are named constants in `SessionSuggestionOptions`, not user settings.** Defaults: burst gap 15 min, at least 3 days together, share ≥ 0.5, 2–15 files per group, show at most 5. | They need tuning against real data first; exposing them now would freeze guesses. Tests pass their own options. |
| G6 | **Suggestions are worked out on demand** (when the Sessions panel or window is shown, or the user selects Refresh), cached until Recent Files records again, and never in the tracking tick. | Keeps the one-minute tick unchanged. The input is small (§8), but there is no reason to pay for it while nobody is looking. |
| G7 | **Never suggest what the user already has.** A group equal to, or contained in, an existing session's files is dropped. A group that only adds to an existing session is not offered as "add to session" in this version. | Avoids noise. "Add to session" is a follow-up (§9). |
| G8 | **Dismiss is remembered; Save is not.** Dismissal stores a hash of the sorted, normalised paths. A dismissed group comes back only if it grows to include a file it didn't have. | A saved group is filtered by G7 anyway. Hashing keeps paths out of the new file. |
| G9 | **One new machine-local file, `session-suggestions.json`, holds the switch and the dismissals.** Nothing is added to `settings.json`, `recent-files.json` or `sessions.json`. | `sessions.json` is the user's data and is what gets shared; dismissals are local noise. Putting the switch here avoids a schema bump in `recent-files.json`. |
| G10 | **Save goes through the editor, never straight to the store.** The user sees the files ticked and a suggested name, and can untick or rename before `TryCreate`. | The suggester can be wrong; the user decides what a session is. |
| G11 | **The suggested name is the files' deepest shared folder name**, or the first project-number-like segment (5–6 digits, e.g. `240015`) if they share none, or blank. | Matches how projects are laid out (`C:\X\2024\240015\…`). `AliasSuggestion.FromFolderPath` already gives the deepest folder name. |
| G12 | **Files that no longer exist are left out at display time**, and a group that drops below 2 files is not shown. | Projects get archived; a suggestion with dead paths is worse than none. Checked through `IShell`, so tests use `FakeShell`. |

## 4. The algorithm (`SessionSuggester`)

Input: `IReadOnlyList<RecentFileHistory>`, the existing sessions' file lists, the dismissed hashes, `SessionSuggestionOptions`, the local `TimeZoneInfo`, and "now" from a `TimeProvider`.

1. **Flatten and sort.** Every `(path, openUtc)` from the history, sorted by time. Paths are already normalised by the store.
2. **Split into bursts.** Walk in order; start a new burst when the gap since the previous open exceeds `BurstGap`. A burst is the set of distinct paths in it, plus the local date of its first open. Bursts with fewer than 2 paths are dropped. A burst larger than `MaxBurstFiles` (default 40, e.g. a batch-print) is dropped as not a working set.
3. **Count active days per file**: distinct local dates with at least one open.
4. **Count days together per pair**: for each burst, every unordered pair of its paths adds its date to that pair's day set.
5. **Keep strong pairs**: `daysTogether ≥ MinDaysTogether` and `daysTogether / min(activeDays(a), activeDays(b)) ≥ MinShare`.
6. **Grow groups greedily.** Take the strongest remaining pair (most days together, then most recent day). Add any file that is a strong pair with at least `ceil(2/3 × group size)` of the group's members, strongest first, up to `MaxGroupFiles`. Mark those pairs used; repeat until no strong pair is left. A file can be in more than one group.
7. **Filter**: drop groups that are a subset of an existing session (G7), whose hash is dismissed (G8), or that are a subset of a larger group already kept.
8. **Score and rank**: days together (the minimum across the group's strong pairs), then the most recent day together. Keep the top `MaxSuggestions`.
9. **Describe**: the suggested name (G11), the number of days together, and the last day together.

Output: `IReadOnlyList<SessionSuggestion>`:

```csharp
public sealed record SessionSuggestion(
    string Key,                    // hash of sorted normalised paths (G8)
    IReadOnlyList<string> Files,   // in first-opened order within the latest burst
    string? SuggestedName,
    int DaysTogether,
    DateOnly LastTogether);
```

Days are local dates through the given `TimeZoneInfo`, matching `RecentFilesStore.LocalDate`, so a burst at 23:55–00:05 belongs to the day it started.

## 5. Data: `session-suggestions.json`

In `AppDataFolders.Local`, loaded with `JsonStoreLoader.Load<SessionSuggestionsDocument>(storage, 1, "dismissed", "Session suggestions", options)`.

```json
{
  "schemaVersion": 1,
  "enabled": false,
  "dismissed": [
    { "key": "b1946ac92492d2347c6235b4d2611184", "files": 4, "dismissedAt": "2026-10-14T09:12:00Z" }
  ]
}
```

- `key` is a SHA-256 of the sorted, upper-cased normalised paths joined by `\n`, truncated to 32 hex characters. `files` is kept only so the G8 rule ("comes back if it grows") can be checked without paths.
- Dismissals older than a year are dropped on save.
- A damaged or newer file behaves as the other stores do: left untouched, suggestions off for the session, a notice in the Sessions panel. A missing file reads as off with no dismissals.

## 6. UI

**Setting** (`Views/Panels/RecentFilesSettings.xaml`, `RecentFilesSettingsViewModel`): under "Record the documents and drawings I open", a checkbox *"Suggest sessions from files I often open together"*, enabled only while Recent Files is enabled, with the hint *"Looks for files opened within a few minutes of each other on several days. Nothing leaves this PC."* It saves with the rest of the Recent Files settings, writing to the suggestions store rather than `recent-files.json`.

**Sessions panel and window** (`SessionsPanel.xaml`, `SessionsWindow.xaml`, `SessionsViewModel`): a collapsible **Suggested** section above the list, shown only when the switch is on and there is at least one suggestion. Each card:

> **240015** · 4 files · opened together on 6 days, last Tue
> `A-101.pdf`, `A-102.pdf`, `Spec 08 71 00.docx`, `Door schedule.xlsx`
> [Save as session] [Dismiss]

- File names, not full paths, with the full path as a tooltip. More than 4 files shows "+ 3 more", which expands.
- **Save as session** opens `SessionEditorDialog` through a new `SessionEditorViewModel.ForSuggestion(store, suggestion)`: `ForFileSet`'s behaviour plus the suggested name, titled "Save Suggested Session". After a save the panel reloads and the group disappears (G7).
- **Dismiss** removes the card at once and saves the dismissal; a failed save shows the usual persistence message and the card stays gone for the session.
- Styling uses the existing card, caption and hint styles and the highlight colour; no new palette entries.

`SessionsViewModel` takes an optional `ISessionSuggestionSource` (null in existing tests and in contexts without Recent Files), so its current constructor callers and tests are unchanged.

## 7. Files

New, UI-free, linked into `QuickerPlaces.Tests` like the other services:

| File | Purpose |
|---|---|
| `Services/Sessions/Suggestions/SessionSuggester.cs` | §4, a pure static function |
| `Services/Sessions/Suggestions/SessionSuggestionOptions.cs` | G5 constants |
| `Services/Sessions/Suggestions/SessionSuggestion.cs` | The output record |
| `Services/Sessions/Suggestions/SessionSuggestionKey.cs` | G8 hashing |
| `Services/Sessions/Suggestions/SessionSuggestionStore.cs` | §5 load/save, switch, dismissals |
| `Services/Sessions/Suggestions/SessionSuggestionSource.cs` | Ties `RecentFilesStore`, `SessionStore`, the store and `IShell` together, caches until `RecentFilesHost.Recorded` fires (G6) |
| `Models/Sessions/SessionSuggestionsDocument.cs` | The JSON shape |

Changed:

| File | Change |
|---|---|
| `ViewModels/SessionsViewModel.cs` | Suggested section: rows, Save, Dismiss, Refresh |
| `ViewModels/SessionEditorViewModel.cs` | `ForSuggestion` factory |
| `ViewModels/RecentFilesSettingsViewModel.cs` | The switch |
| `Views/Panels/SessionsPanel.xaml(.cs)`, `Views/SessionsWindow.xaml(.cs)` | The section |
| `Views/Panels/RecentFilesSettings.xaml` | The checkbox |
| `Views/MainWindow.xaml.cs` | Create the store and source; pass them on |
| `QuickerPlaces.Tests.csproj` | Link the new files |

Not changed: `RecentFilesStore`, `RecentFilesHost`'s tick, `SessionStore`, `sessions.json`, `recent-files.json`, `settings.json`, the CLI.

## 8. Tests and measurement

**`SessionSuggesterTests`** (fixtures built with `ManualTimeProvider` and `TestZones`):

- Two files opened together on 3 days → one suggestion; on 2 days → none (default options).
- Gap of 14 min joins a burst; 16 min splits it.
- A file opened every day with everything (timesheet) is not in any group (G4).
- Three files strongly linked plus one weakly linked → the group has three.
- Two separate projects in the same weeks → two groups, not one.
- A group equal to, or inside, an existing session → dropped; a larger group than a session → kept.
- Dismissed key → dropped; the same group plus a new file → shown again.
- A burst of 41 files → ignored.
- Burst over local midnight counts on its first day; DST change days counted correctly.
- Ranking: more days first, then most recent.
- Suggested name: shared folder; no shared folder but a `240015` segment; neither → null.
- Paths differing only in case are one file.

**`SessionSuggestionStoreTests`**: missing file → off; round trip; damaged and newer files left untouched with a notice; dismissals older than a year pruned.

**`SessionsViewModelTests` / `SessionEditorViewModelTests`**: section hidden when off or empty; Save opens the editor with files and name; Dismiss removes the row and persists; missing files dropped (G12) via `FakeShell`.

**Performance** (per CLAUDE.md, before claiming no cost): on Windows, time `SessionSuggester` with `dotnet-counters`/Stopwatch logging against (a) the user's real `recent-files.json` and (b) a synthetic worst case of 2,000 files × 500 opens, 10 repeated runs each, recording the machine, .NET version and spread. Target: under 50 ms for (a), and under 500 ms for (b) off the UI thread. Confirm the Sessions panel's time to show is unchanged with the switch off. If (b) is slow, the pair count is the place to cap (bursts are already capped at 40 files).

Run `dotnet test src/QuickerPlaces.sln` from `C:\QuickerPlaces\src` after each step.

## 9. Order of work

Each step is one small reviewable commit:

1. `SessionSuggester`, options, record, key, with `SessionSuggesterTests`. No UI.
2. `SessionSuggestionStore` and document, with store tests.
3. `SessionSuggestionSource` (caching, G12 existence check), with tests.
4. Setting checkbox in Recent Files settings.
5. Suggested section in the Sessions panel and window; `ForSuggestion`; view-model tests.
6. Windows measurement (§8) and the checklist (§11); record results here and in `BUILD_SUMMARY.md`.

**Not in this version (possible follow-ups):**

- "Add to session" when a group extends an existing session.
- A status-line nudge when 2+ files of a suggested group are opened within the burst window.
- Using the activity history beyond 62 days.
- `qp sessions suggest` in the CLI (the suggester is UI-free, so this is cheap later).
- Counting a time saving for the rewards idea discussed the same day.

## 10. Open questions

1. **Threshold:** 3 days together (default here) or 2, so suggestions appear sooner at the cost of more noise?
2. **Placement:** Suggested section in both the Sessions panel and the Sessions window, or the window only?
3. **Burst gap:** 15 minutes fits "open my morning set"; should it be shorter (5–10 min) for people who switch projects often?

## 11. Windows checklist

- [ ] With Recent Files off, the suggestions checkbox is disabled and says why.
- [ ] Open the same 3–4 files within a few minutes on 3 different days → a suggestion appears with the right name.
- [ ] Save as session opens the editor with the files ticked and the name filled; unticking one and saving works; the suggestion disappears.
- [ ] Dismiss removes it and it stays gone after restarting QuickerPlaces.
- [ ] A suggestion whose files were moved or deleted is not shown.
- [ ] A daily file (timesheet) does not join groups.
- [ ] Turning the switch off hides the section; turning it on again shows the same suggestions and keeps dismissals.
- [ ] A damaged `session-suggestions.json` is left untouched and shows a notice; sessions still work.
- [ ] §8 measurements recorded.

---
title: QuickerPlaces — Session suggestions from files opened together
status: proposed (2026-10-14), revised the same day for the hour-long rule (§5), the Suggestions panel (§7) and user-chosen sensitivity (§6.1), then for the always-visible gear on the Sessions panel (§7.1) and per-rule sensitivity (§6.1), and on 2026-10-15 for a two-week history (G17) and non-overlapping cards (G18); not started. Open questions in §12
created: 2026-10-14
parent: ai/260928_PDF Project Sessions Plan.md
---

# Session suggestions from files opened together

## 1. The request

On 2026-10-14 the user asked: *"what if we add session suggestions based on files that the user opens together over a short period of time"*.

Later the same day they added four requirements:

- *"if files are open together at any time longer than an hour might be another rule"*.
- *"I want these session suggestions to be in a separate suggestions panel that opens by the sessions and lets the user quickly add them to a session"*.
- *"If the rules for suggested sessions can also be user adjusted in sensitivity. Giving the user some high and low sensitivity choices"*.
- *"The suggestions panel should always be accessible via the sessions panel with maybe a gear icon. They can share a sensitivity default between the both. But allow individual control as well. Balance numbers by default"*.

Today a session is made by hand: open the files, then **Save open files** (the on-demand scan), or **Save files as session** from the File shelf. People who open the same four drawings and a spec every morning still build that session themselves, or never build it. QuickerPlaces already records when each file is opened, so it can notice those groups and offer them.

## 2. What already exists

| Piece | Where | What it gives this feature |
|---|---|---|
| Recorded opens with real times | `RecentFilesStore.Record`; times come from the Recent Items shortcut's `LastWriteTimeUtc` (`WindowsRecentItems.cs`), not from the one-minute poll | When each file was opened (rule A) |
| 62 days of opens per file | `RecentFileRecord.Opens`, `RecentFilesStore.DetailDays`, `MaxOpensPerFile = 500` | More than enough: suggestions read only the last 14 days (G17) |
| A read of all of it | `RecentFilesStore.QueryHistory()` → `RecentFileHistory(Path, Kind, Opens)` | Rule A's only input |
| Window-title evidence | `WindowsOpenDocumentProbe.FindViewerWindows` (EnumWindows, Word/Excel window classes, process names) and `OpenDocumentResolver.Resolve(OpenDocumentEvidence, …)` | Which documents are open right now, from window titles matched against Recent Items (rule B) |
| Path rules | `DocumentPaths.Normalize` / `DocumentPaths.Equal` (case-insensitive) | The same file identity sessions and the Library already use |
| Sessions | `SessionStore.Sessions`, `TryCreate`, `TryUpdate` | Filtering out what the user already has; one-click create and add |
| A review-before-save editor | `SessionEditorViewModel.ForFileSet(store, SessionFileSet)`, `SessionEditorDialog` | The optional Review… path |
| Workspace panels | `PanelTypes`, `PanelInstance` (span, dock, hidden), `WorkspaceLayoutService`, `WorkspaceView` | The new Suggestions panel |
| Pause and presence | `RecentFilesHost`'s `isPaused`, `IUserPresence` | Rule B's sampler stops when tracking is paused or the user is away |
| Store loading | `JsonStoreLoader.Load<T>` with `FilePlacesStorage` | Damaged/unreadable/newer-version handling for the one new file |

## 3. Decisions

| # | Decision | Why |
|---|---|---|
| G1 | **Two rules make a pair; either is enough.** **Rule A (opened together):** opened in the same burst on several different days. **Rule B (open together):** both seen open at the same time for an hour or more without a break, on any one day (at Balanced; §6.1). | Rule A catches the morning set someone opens in a hurry. Rule B catches the files someone works across for a long stretch, even if it happened only once. |
| G2 | **Off by default, and only offered while Recent Files is on.** One switch for suggestions; under it, a second switch for rule B, ticked by default when suggestions are turned on, whose wording says it checks open windows every 5 minutes. | Same consent model as everything else that reads what the user opens. Rule B is a new kind of observation (§5), so the user sees what it does and can keep rule A alone. Both rules resolve paths through Recent Items, so neither works without it. |
| G3 | **Rule A:** a burst ends after a 15-minute gap with no open. Pairs are counted by **days**, not bursts; at least 3 days at Balanced (§6.1). | One busy morning re-opening two files is not a habit; the same files together on three separate days is. |
| G4 | **Files that go with everything are left out.** Rule A: a pair counts only when its days together are at least half of the rarer file's active days (share ≥ 0.5). Rule B: a file seen open for more than 6 hours on most days it is seen at all is "always open" and makes no rule-B pairs. | A daily timesheet or a standards PDF left open all day would otherwise join every group. |
| G5 | **The user picks a sensitivity — Low, Balanced or High — not individual thresholds.** One overall level applies to both rules; either rule can instead be set to its own level (§6.1). Each level is a fixed set of thresholds in `SessionSuggestionOptions`; Balanced is the default for all three choices. Tests can still pass any options. | Five numbers with units mean little to a user; "fewer, surer suggestions" against "more, sooner" does. Fixed levels can be tuned in code against real data without breaking anyone's saved choice. |
| G6 | **Rule B samples window titles only, never file handles, in the background.** Every 5 minutes, off the UI thread, it lists document windows and resolves them as the scan does, with an empty `HeldFiles`. | The handle list (`WindowsHeldFiles`) is system-wide, can stall on some handles (held-files plan H4), and stays on-demand only, as the sessions plan decided. Window titles are cheap and never block on a file server. |
| G7 | **Suggestions are worked out on demand** (when the Suggestions panel is shown, or on Refresh), cached until Recent Files records or the sampler closes a span, and never in a tracking tick. | Keeps the tracking ticks unchanged. Showing a count on the Sessions panel's button uses the cached result. |
| G8 | **A group that matches an existing session is not suggested. A group that overlaps one becomes an "add to session" suggestion.** Overlap: at least half of the group's files are already in the session, and at least one isn't. | The user asked to add suggestions *to* a session, so overlap is the most useful case, not noise. |
| G9 | **Dismiss is remembered; Add is not.** A dismissal stores a hash of the sorted, normalised paths. A dismissed group comes back only if it grows to include a file it didn't have. | An added group is filtered by G8 anyway. |
| G10 | **Quick actions save at once, with Undo.** **New session** creates it with the suggested name; **Add to ‹session›** adds the missing files. Both show "Saved · Undo" in the panel's status line. **Review…** opens the editor for anyone who wants to untick files or rename first. | "Quickly add them to a session." The editor stays one click away, and Undo covers a wrong guess. |
| G11 | **The suggested name is the files' deepest shared folder name**, or the first project-number-like segment (5–6 digits, e.g. `240015`) if they share none, or "Suggested session" with the date. An existing session name gets " (2)", as the store requires unique names. | Matches how projects are laid out (`C:\X\2024\240015\…`). `AliasSuggestion.FromFolderPath` already gives the deepest folder name. |
| G12 | **Files that no longer exist are left out at display time**, and a group that drops below 2 files is not shown. | Projects get archived; a suggestion with dead paths is worse than none. Checked through `IShell.FileExists`, so tests use `FakeShell`. |
| G13 | **Suggestions get their own panel type, `suggestions`, always reachable from a gear button on the Sessions panel** — whether suggestions are on, off or unavailable. It opens directly after the Sessions panel, in the same column, at the same width, without entering Arrange mode. Closing it hides it. | "A separate suggestions panel that opens by the sessions." Using the workspace's own panel model means it can still be moved, resized or added from Add panel like any other, and the layout remembers it. |
| G14 | **One new machine-local file, `session-suggestions.json`, holds the switches, the dismissals and rule B's open spans.** Nothing is added to `settings.json`, `recent-files.json` or `sessions.json`. | `sessions.json` is the user's data and is what gets shared; this is local observation. Keeping the switches here avoids a schema bump in `recent-files.json`. |
| G15 | **Sensitivity changes only how pairs are judged, never what is recorded.** The sample interval (5 min), the 14-day history (G17) and the 40-file burst cap are the same at every level. Changing it re-ranks at once from data already held, and keeps dismissals. | Background cost and what is stored don't depend on a preference, so High is no more expensive than Low, and moving the slider back and forth loses nothing. |
| G16 | **The suggestion settings live in one shared control, shown both in the Suggestions panel and in Recent Files settings.** | The gear is where people will look; Recent Files settings is where the consent sits. One control bound to one view model keeps the two in step. |
| G17 | **Suggestions look back two weeks at most.** Both rules read only the last 14 days, and rule B's spans are kept 14 days. Recent Files keeps its own 62 days for the Library and Recents, unchanged. | Thomas, 2026-10-15: *"Suggestions only need to go back two weeks maybe, at most for history."* What someone works on together changes with the project stage; an older pattern is more likely to be stale than useful. Low's 4 days together still fit easily in 14. |
| G18 | **New-session suggestions never share a file, and add suggestions for one session merge into one card.** A lower-ranked new-session suggestion that shares a file with a higher-ranked one is dropped; add suggestions with the same target session become one card with the union of their files, ranked as the best of them, and checked against dismissals again. | Found in T4's review: without it, one file linked to many others (a spec sheet) gave a row of near-identical cards ("240015", "240015 (2)", …). It is about near-duplicates, **not a promise about card counts**: greedy grouping can't guarantee Low shows fewer cards than High (a chain 1–2–3–4 is two cards at Low but one larger card at High), because at High more files merge into fewer, bigger cards. Low still asks for stronger evidence per pair. Overseer's call within §6.1's intent, 2026-10-15; Thomas may overrule. |

## 4. Rule A: opened together (from recorded opens)

Input: `IReadOnlyList<RecentFileHistory>`.

1. **Flatten and sort** every `(path, openUtc)` by time.
2. **Split into bursts**: a new burst starts when the gap since the previous open exceeds `BurstGap` (15 min at Balanced; §6.1). A burst is its distinct paths and the local date of its first open. Bursts of fewer than 2 paths are dropped, and so are bursts of more than `MaxBurstFiles` (40; a batch print is not a working set).
3. **Active days per file**: distinct local dates with at least one open.
4. **Days together per pair**: each burst adds its date to the day set of every pair in it.
5. **Strong pair (A)**: `daysTogether ≥ MinDaysTogether` and `daysTogether / min(activeDays(a), activeDays(b)) ≥ MinShare` (3 days and 0.5 at Balanced).

## 5. Rule B: open together for an hour (from window samples)

**The sampler** (`OpenDocumentSampler`, app-only like the probe; its logic in a UI-free `OpenSpanRecorder`):

- Runs every `SampleInterval` (5 min) while rule B is on, Recent Files is on, tracking isn't paused, and the user is present (`IUserPresence`).
- Lists document windows as `WindowsOpenDocumentProbe.FindViewerWindows` does (extracted so both share it) and resolves them with `OpenDocumentResolver.Resolve`, giving it the windows and Recent Items but no held files (G6). Only candidates marked `IsLikelyOpen` and in Recent Files' scope count.
- `OpenSpanRecorder.Observe(now, paths)` extends each path's open span when it was also seen in the previous sample, and starts a new span otherwise. A path not seen, or a missed sample (sleep, pause, away), closes its span.
- Spans are kept 14 days, the suggestions' history (G17), and written to `session-suggestions.json` when a span closes and at most every 15 minutes.

**The rule**: two files make a **strong pair (B)** when one of their spans overlaps for at least `TogetherOpen` on at least `TogetherOpenDays` different days (60 min on 1 day at Balanced; §6.1), and neither file is "always open" (G4). The pair's evidence is the longest overlap and its day.

**Precision and limits, said plainly in the setting's hint:**

- A 5-minute sample means overlaps are measured to within about 5 minutes, so "an hour" is 13 consecutive samples with both files open.
- Window titles show the document that is in front: every Word and Excel document has its own window, but in Revu, Acrobat and Revit only the active tab or view is seen. A background PDF tab is not "open" to rule B. The on-demand scan, which also reads file handles, sees more, and stays on-demand (G6).
- A title has to match a file in Recent Items to become a path, so a file opened in a way Windows doesn't record is missed, as with rule A.

## 6. Building and ranking suggestions (`SessionSuggester`)

Input: rule A and rule B strong pairs, the existing sessions, the dismissed hashes, `SessionSuggestionOptions`, the local `TimeZoneInfo`, and now from a `TimeProvider`. A pure static function.

1. **Grow groups greedily.** Take the strongest remaining pair (rule B pairs rank by overlap length, rule A pairs by days together; most recent breaks ties). Add any file that makes a strong pair, by either rule, with enough of the group's members (⅔ at Balanced; §6.1), strongest first, up to `MaxGroupFiles`. Repeat until no strong pair is left. A file can be in more than one group.
2. **Match against sessions** (G8): equal to, or inside, a session → dropped; overlapping a session → an add suggestion for the session with the most overlap, listing only the missing files; otherwise → a new-session suggestion.
3. **Drop** dismissed groups (G9) and groups inside a larger group already kept.
4. **Rank**: add suggestions first (they finish something the user started), then by strength, then by recency. Keep the top `MaxSuggestions`.
5. **Describe**: the suggested name (G11) and why, e.g. "opened together on 6 days, last Tue" or "open together for 1 h 40 min on Mon".

```csharp
public sealed record SessionSuggestion(
    string Key,                    // hash of sorted normalised paths (G9)
    IReadOnlyList<string> Files,   // all files in the group
    IReadOnlyList<string> NewFiles,// files not yet in TargetSessionId (all, for a new session)
    string? TargetSessionId,       // null: new session
    string? SuggestedName,         // for a new session
    SuggestionReason Reason);      // DaysTogether + LastTogether, and/or LongestOpenTogether + its day
```

Days are local dates through the given `TimeZoneInfo`, matching `RecentFilesStore.LocalDate`, so a burst at 23:55–00:05 belongs to the day it started.

### 6.1 Sensitivity levels

Three choices, each Low, Balanced or High:

- **Overall** — the default for both rules, and the level for the shared rows below (grouping, how many shown). Balanced.
- **Used together** (rule A) — *Same as overall* (default) or its own level.
- **Open together** (rule B) — *Same as overall* (default) or its own level.

So out of the box every threshold is Balanced; someone who finds the hour-long rule noisy can set just that rule to Low and leave the rest alone. `SessionSuggestionOptions.For(overall, usedTogether?, openTogether?)` builds the options from the three, a null rule level meaning the overall one. The starting values are below; step 9's Windows trial against real data may tune them, and any change is recorded here.

| Threshold | Low — fewer, surer | **Balanced** (default) | High — more, sooner |
|---|---|---|---|
| *Rule A (used together)* | | | |
| Rule A: burst gap | 10 min | 15 min | 30 min |
| Rule A: days together | 4 | 3 | 2 |
| Rule A: share of the rarer file's days (G4) | ≥ 0.6 | ≥ 0.5 | ≥ 0.35 |
| *Rule B (open together)* | | | |
| Rule B: open together | 90 min | 60 min | 30 min |
| Rule B: on how many days | 2 | 1 | 1 |
| Rule B: "always open" above (G4) | 5 h | 6 h | 8 h |
| *Shared (follow overall)* | | | |
| Grouping: links needed to join a group | all members | ⅔ of members | ½ of members |
| Suggestions shown | 5 | 10 | 15 |

Fixed at every level (G15): sample interval 5 min, 2–15 files per group, bursts over 40 files ignored, 14 days of history (G17).

Rule B at High (30 min) is 7 consecutive samples, so a single missed sample matters more; that is the accepted cost of "sooner".

Levels are stored as `"low"`, `"balanced"` or `"high"`. The overall level reads anything else as Balanced; a rule level reads anything else, or null, as *Same as overall* — the same lenient rule `settings.json` uses for its strings.

## 7. UI

### 7.1 The Suggestions panel

A new panel type, `PanelTypes.Suggestions = "suggestions"`, added to `PanelTypes.Known` after `Sessions`, so it appears in Add panel too. Not in any built-in layout by default; it is opened from the Sessions panel.

**Opening it** (G13): the Sessions panel header gets a gear button (the existing `Icon.Settings`), **always visible**, tooltip "Session suggestions". When suggestions are on and some are found, it carries a small count badge in the highlight colour ("3"). Keyboard: it is in the header's tab order, and the Sessions panel's context menu has **Suggestions…** too. Selecting it calls a new `WorkspaceLayoutService.ShowBeside(PanelTypes.Suggestions, sessionsPanelId)`:

- If the layout has no Suggestions panel, it is inserted directly after the Sessions panel, same dock, same span.
- If it is hidden, it is shown in its remembered place (the user may have moved it).
- If it is already visible, the button scrolls it into view and focuses it.
- One committed layout change with Undo, allowed outside Arrange mode (like Hide is reversible), and saved with the layout.

The panel's own **Close** button hides it. In the separate Sessions window (`SessionsWindow`), the same gear opens the panel's content as a side pane on the right of the window, since that window has no workspace.

**Each suggestion card:**

> **240015** · new session · 4 files
> opened together on 6 days, last Tue
> `A-101.pdf` · `A-102.pdf` · `Spec 08 71 00.docx` · `Door schedule.xlsx`
> **[New session]** [Review…] [Dismiss]

> **Tower B** · add 2 files · open together for 1 h 40 min on Mon
> `+ A-301.pdf` · `+ A-302.pdf`
> **[Add to Tower B]** [Add to… ▾] [Dismiss]

- File names, not paths, with the full path as a tooltip; more than 4 shows "+ 3 more", which expands.
- **New session** / **Add to ‹session›**: saves at once (G10); the card leaves; the status line says "Saved 240015 · Undo" or "Added 2 files to Tower B · Undo". The Sessions panel reloads and selects that session.
- **Add to… ▾**: a menu of every session, those sharing files listed first, so a new-session suggestion can go into an existing session too.
- **Review…**: `SessionEditorDialog` through a new `SessionEditorViewModel.ForSuggestion(store, suggestion)`, titled "Save Suggested Session" or "Add to Tower B".
- **Dismiss**: the card leaves at once and the dismissal is saved; a failed save shows the usual persistence message.
- **Drag a card** onto a session row in the Sessions panel to add its files there. The Sessions panel already handles drops for reordering (`CardDropPreview`), so this reuses that path.
- Single files can be dragged out of a card onto a session row too, for the "just this one" case.

**Settings in the panel** (G16): the panel's top strip reads, collapsed, *"Sensitivity: Balanced · Adjust"* (or *"Used together: Low · Open together: Balanced · Adjust"* when the rules differ). **Adjust** expands the same settings control as §7.2: the switches, the overall level and the per-rule levels. It starts expanded when suggestions are off, so the gear always lands somewhere useful. A change re-ranks the list at once (G15). With nothing found, the empty state adds "Try High sensitivity" when the level isn't already High; with more than 10 suggestions at High, the footer offers "Too many? Try Balanced".

**Empty and off states**: with suggestions off, the settings strip is expanded with **Suggest sessions…** unticked, and a line saying what it does. With Recent Files off, the strip says suggestions need it and has an **Open Recent Files settings** button; nothing can be ticked until it is on (G2). With none found: "No suggestions yet. They appear when the same files are opened together on a few days, or kept open together for an hour."

Styling uses the existing panel frame, card, caption and hint styles and the highlight colour; no new palette entries.

### 7.2 Settings

One control, `Views/Panels/SuggestionSettings.xaml`, shown in the Suggestions panel's strip and in `Views/Panels/RecentFilesSettings.xaml` under "Record the documents and drawings I open" (G16). Enabled only while Recent Files is on:

- ☐ **Suggest sessions from files I use together**
  - ☑ **Include files I keep open together for over an hour** — *"Checks which documents are open every 5 minutes while QuickerPlaces is running. Only the document in front is seen in Revu, Acrobat and Revit. Nothing leaves this PC."*
  - **Sensitivity:** ( ) Low ( • ) Balanced ( ) High — the overall level; three radio buttons, as the Theme choice in Settings is laid out, each with a hint:
    - *Low:* "Fewer suggestions. Files must be used together often, or open together for an hour and a half on two days."
    - *Balanced:* "Files used together on three days, or open together for an hour."
    - *High:* "More suggestions, sooner. Files used together on two days, or open together for half an hour. Expect some you'll dismiss."

  - ▸ **Adjust each rule separately** (collapsed by default; expanded when either rule has its own level):
    - **Used together:** [Same as overall (Balanced) ▾] — Same as overall · Low · Balanced · High
    - **Open together:** [Same as overall (Balanced) ▾] — disabled while the hour-long rule is off
    - **Reset to overall** sets both back to *Same as overall*.

  The rule-B hint follows its effective level ("…for over an hour" at Balanced, "…for half an hour" at High).

Saved with the rest of the Recent Files settings, written to the suggestions store, not `recent-files.json`. Turning rule B off stops the sampler and deletes the recorded spans; turning suggestions off stops everything and keeps dismissals.

## 8. Data: `session-suggestions.json`

In `AppDataFolders.Local`, loaded with `JsonStoreLoader.Load<SessionSuggestionsDocument>(storage, 1, "dismissed", "Session suggestions", options)`.

```json
{
  "schemaVersion": 1,
  "enabled": false,
  "openTogether": true,
  "sensitivity": "balanced",
  "usedTogetherSensitivity": null,
  "openTogetherSensitivity": null,
  "dismissed": [
    { "key": "b1946ac92492d2347c6235b4d2611184", "files": 4, "dismissedAt": "2026-10-14T09:12:00Z" }
  ],
  "openSpans": [
    { "path": "C:\\X\\2024\\240015\\A-101.pdf", "from": "2026-10-13T08:05:00Z", "to": "2026-10-13T09:50:00Z" }
  ]
}
```

- `key` is a SHA-256 of the sorted, upper-cased normalised paths joined by `\n`, truncated to 32 hex characters; `files` lets G9's "comes back if it grows" be checked without paths.
- `openSpans` holds paths, as `recent-files.json` does, and only while rule B is on. Spans older than 14 days (G17) and dismissals older than a year are dropped on save. A rough size: 30 documents open over a working day in 4 spans each is about 120 spans a day, about 1,700 over 14 days, under 350 KB.
- A damaged or newer file behaves as the other stores do: left untouched, suggestions off for the session, a notice in the Suggestions panel. A missing file reads as off.

## 9. Files

New, UI-free, linked into `QuickerPlaces.Tests`:

| File | Purpose |
|---|---|
| `Services/Sessions/Suggestions/OpenedTogetherRule.cs` | Rule A (§4) |
| `Services/Sessions/Suggestions/OpenSpanRecorder.cs` | Rule B's span keeping (§5) |
| `Services/Sessions/Suggestions/OpenTogetherRule.cs` | Rule B's pairs, including always-open (§5, G4) |
| `Services/Sessions/Suggestions/SessionSuggester.cs` | Groups, session matching, ranking (§6) |
| `Services/Sessions/Suggestions/SessionSuggestionOptions.cs` | Thresholds and `For(overall, usedTogether?, openTogether?)` (§6.1) |
| `Services/Sessions/Suggestions/SessionSuggestion.cs` | Output records |
| `Services/Sessions/Suggestions/SessionSuggestionKey.cs` | G9 hashing |
| `Services/Sessions/Suggestions/SessionSuggestionStore.cs` | §8 load/save, switches, dismissals, spans |
| `Services/Sessions/Suggestions/SessionSuggestionSource.cs` | Ties the stores, `SessionStore` and `IShell` together; caches (G7); quick New/Add with Undo (G10) |
| `Models/Sessions/SessionSuggestionsDocument.cs` | The JSON shape |
| `ViewModels/SuggestionsViewModel.cs` | Cards, actions, status and Undo |
| `ViewModels/SuggestionSettingsViewModel.cs` | The switches and the three sensitivity choices, shared by both places (G16) |

New, app-only:

| File | Purpose |
|---|---|
| `Services/Documents/DocumentWindows.cs` | `FindViewerWindows` moved out of `WindowsOpenDocumentProbe` so the probe and the sampler share it |
| `Services/Sessions/Suggestions/OpenDocumentSampler.cs` | The 5-minute timer (§5) |
| `Views/Panels/SuggestionsPanel.xaml(.cs)` | The panel |
| `Views/Panels/SuggestionSettings.xaml(.cs)` | The shared settings control (§7.2) |

Changed:

| File | Change |
|---|---|
| `Models/Workspace/PanelInstance.cs` | `PanelTypes.Suggestions` |
| `Services/Workspace/WorkspaceLayoutService.cs` | `ShowBeside` (G13) |
| `Services/Workspace/PanelLayoutEngine.cs` | Minimum height for the new type |
| `Views/WorkspaceView.xaml(.cs)` | Create and attach the panel |
| `Views/Panels/SessionsPanel.xaml(.cs)`, `ViewModels/SessionsViewModel.cs` | Always-visible gear and count badge; context-menu entry; accept card drops |
| `Views/SessionsWindow.xaml(.cs)` | Side pane |
| `ViewModels/SessionEditorViewModel.cs` | `ForSuggestion` |
| `ViewModels/RecentFilesSettingsViewModel.cs`, `Views/Panels/RecentFilesSettings.xaml` | Host the shared settings control |
| `Services/Documents/WindowsOpenDocumentProbe.cs` | Uses `DocumentWindows` |
| `Views/MainWindow.xaml.cs` | Create the store, source and sampler; stop the sampler on pause and exit |
| `QuickerPlaces.Tests.csproj` | Link the new files |

Not changed: `RecentFilesStore`, `RecentFilesHost`'s tick, `SessionStore`, `WindowsHeldFiles`, `sessions.json`, `recent-files.json`, `settings.json`, the CLI.

## 10. Tests and measurement

**Rule A** (`OpenedTogetherRuleTests`, fixtures with `ManualTimeProvider` and `TestZones`): two files together on 3 days → pair; 2 days → none; 14-minute gap joins, 16 splits; a daily timesheet makes no pairs; a 41-file burst is ignored; a burst over local midnight counts on its first day; DST days; paths differing only in case are one file.

**Rule B** (`OpenSpanRecorderTests`, `OpenTogetherRuleTests`): consecutive samples extend a span; a missed sample closes it; 13 samples together (60 min) → pair, 12 → none; overlap counts only while both are open, not from first to last sight; a file open 7 h on most days makes no pairs; spans older than 14 days pruned.

**Suggester** (`SessionSuggesterTests`): three strongly linked files plus one weak → three; two projects in the same weeks → two groups; one rule-A pair and one rule-B pair sharing a file → one group of three; equal to a session → dropped; half inside a session → add suggestion with only the missing files; dismissed → dropped, grown → shown again; ranking puts add suggestions first; names from shared folder, from `240015`, and the fallback; a name clash gets " (2)".

**Store** (`SessionSuggestionStoreTests`): missing file → off; round trip; damaged and newer files left untouched with a notice; turning rule B off deletes spans; the three sensitivities round-trip; a missing or unknown overall level reads as Balanced; a missing, null or unknown rule level reads as Same as overall.

**View models** (`SuggestionsViewModelTests`, `SessionsViewModelTests`, `SessionEditorViewModelTests`): New session creates and Undo deletes; Add adds and Undo restores the old file list; Dismiss persists; missing files dropped (G12) via `FakeShell`; the count on the Sessions button.

**Sensitivity** (`SessionSuggestionOptionsTests`, `SessionSuggesterTests`): each level returns §6.1's values; with no per-rule levels both rules use the overall one; a rule's own level changes only that rule's thresholds, and the shared rows still follow the overall level; setting open-together to Low leaves rule A's result unchanged; one fixture of files together on exactly 2 days and open together for 45 minutes gives a suggestion at High and none at Balanced or Low; a pair open together 70 minutes on one day appears at Balanced and High but not Low; a pair Low accepts is accepted at Balanced and High too (pairs, not card counts, are monotonic, G18; the one exception is a wider burst gap joining bursts past the 40-file cap); changing the level keeps dismissals; the sampler's interval is the same at every level (G15).

**Layout** (`WorkspaceLayoutServiceTests`): `ShowBeside` inserts after Sessions in its column at its span; shows a hidden panel in its remembered place; is a no-op when visible; is undoable; works in rows and columns layouts; a layout without Sessions puts it last.

**Performance** (per CLAUDE.md, before claiming no cost), on Windows with the same machine and build for before and after, 10 repeated runs each, recording the machine, .NET version and spread:

- **Sampler**: time one sample (window listing + resolve) with 0, 10 and 40 document windows open. Target: under 30 ms, off the UI thread. Compare `dotnet-counters` CPU and allocation rates over 30 idle minutes with rule B off and on; the difference should be within run-to-run noise.
- **Suggester**: time it against (a) the user's real data and (b) a synthetic worst case of 2,000 files × 500 opens plus 14 days of spans. Targets: under 50 ms for (a), under 500 ms for (b), off the UI thread.
- **Workspace**: the Sessions panel's time to show, and the layout's reflow when the Suggestions panel opens, unchanged with suggestions off.

Run `dotnet test src/QuickerPlaces.sln` from `C:\QuickerPlaces\src` after each step.

## 11. Order of work

Each step is one small reviewable commit:

1. Rule A, options, records, key, with tests. No UI.
2. `SessionSuggester` (groups, session matching, ranking), with tests, using rule A only.
3. `SessionSuggestionStore` and document (switches, dismissals), with tests.
4. `SessionSuggestionSource` with quick New/Add and Undo, with tests.
5. `PanelTypes.Suggestions`, `ShowBeside`, `SuggestionsViewModel`, `SuggestionsPanel`, the Sessions panel button; the gear, the shared settings control with the overall and per-rule levels. Rule A is usable end to end here.
6. Drag cards and files onto session rows; the Sessions window side pane.
7. Rule B: `OpenSpanRecorder` and `OpenTogetherRule` with tests; spans in the store.
8. `DocumentWindows` extracted (probe behaviour unchanged, its tests still pass); `OpenDocumentSampler`; the rule-B switch.
9. Windows measurement (§10) and the checklist (§13); results here and in `BUILD_SUMMARY.md`.

Rule B comes after rule A works end to end because it is the part with new background work and needs its own measurement gate.

**Not in this version (possible follow-ups):**

- A status-line nudge when 2+ files of a suggested group are opened within the burst window.
- `qp sessions suggest` in the CLI (the suggester is UI-free, so this is cheap later).
- Counting a time saving for the rewards idea discussed the same day.

## 12. Open questions

Settled on 2026-10-14: Balanced is the default everywhere; one overall level is shared by both rules, with optional per-rule levels; the Suggestions panel is always reachable from a gear on the Sessions panel.

Also settled: **three levels** (Low, Balanced, High). Each is easy to describe; five would differ by amounts too small to notice.

1. **Default layout:** should any built-in layout include the Suggestions panel, or is opening it from the Sessions panel enough?

## 13. Windows checklist

- [ ] The gear is on the Sessions panel in every state: with Recent Files off it opens the panel explaining what's needed, with an Open Recent Files settings button; with suggestions off it opens the panel with the settings expanded.
- [ ] Open the same 3–4 files within a few minutes on 3 different days → a suggestion appears with the right name and reason.
- [ ] Keep two Word documents open together for over an hour → a suggestion appears the same day, reason "open together for 1 h …".
- [ ] A PDF left open all day in Revu does not join every group.
- [ ] The gear shows a count when there are suggestions, and opens the panel right after Sessions, same column and width; Close hides it; it reopens where it was left; Undo removes it.
- [ ] New session saves at once with the suggested name; Undo deletes it.
- [ ] A group overlapping a session shows "Add n files to ‹session›"; Add works; Undo restores the old list.
- [ ] Add to… ▾ lists sessions sharing files first.
- [ ] Dragging a card, and a single file, onto a session row adds them.
- [ ] Review… opens the editor; unticking a file and saving works.
- [ ] Dismiss removes it and it stays gone after restarting QuickerPlaces.
- [ ] A suggestion whose files were moved or deleted is not shown.
- [ ] Pause tracking from the tray stops the sampler; resuming starts it.
- [ ] Turning rule B off deletes its spans; turning suggestions off hides the button and panel content and keeps dismissals.
- [ ] A damaged `session-suggestions.json` is left untouched and shows a notice; sessions still work.
- [ ] Switching the overall level Low → Balanced → High in the panel shows more suggestions each time, at once; the choice is kept after restarting; dismissals survive the change.
- [ ] The settings in the panel and in Recent Files settings stay in step.
- [ ] Setting only Open together to Low changes only the open-together suggestions; Reset to overall brings it back.
- [ ] Note how many suggestions each level gives on real data after a week; tune §6.1 if Balanced is noisy or empty.
- [ ] §10 measurements recorded.

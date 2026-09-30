# The File viewer: one tabbed panel for saved places, recents and session files — design

Date: 2026-09-29. Status: Designed. Builds on the [Desk layout design](260929_Desk%20Layout%20and%20Recents%20Panel%20Design.md) (branch `ccr-6156d37a-mo223d`).

## 1. Outcome

Saved places, Recents (with its tracked folders) and session files show in one panel, **Files**, as tabs over one place on the canvas:

```
+-------------+-----------------------------------------------+
| Favourites  | Files                                         |
|             | [Saved places] [Recent] [Sessions] [All]      |
+-------------+   one table; its columns follow the tab       |
| Sessions    |                                               |
|  card ──────┼─► right-click "View session files"            |
|             +-----------------------------------------------+
|             | Year activity                                 |
+-------------+-----------------------------------------------+
```

1. A new panel type, **Files**, with four tabs: **Saved places**, **Recent**, **Sessions**, **All**.
2. **Desk** becomes Favourites and Sessions left; Files and Year activity main.
3. Today's Desk stays, as a second built-in, **Desk · separate panels**. Trying the File viewer and going back is one pick in the layout picker (§2).
4. A session card's right-click **View session files** shows that session's files in Files.

This is a trial of the look; §2 keeps the way back one click away.

Out of scope:
- Retiring the Saved places or Recents panels outside Desk.
- Changes to the Library window (list mode).
- Full saved-place editing from the All tab: its saved-place rows offer Open, Copy and Edit place… only.
- Saving a session scope with the layout.
- Changing the Sessions panel's own file list.

## 2. Layouts

Built-ins (`Services/Workspace/BuiltInLayouts.cs`):

| Id | Name | Left | Main |
|---|---|---|---|
| `builtin.desk` | Desk | Favourites, Sessions | Files, Year activity |
| `builtin.desk-separate` | Desk · separate panels | Favourites, Sessions | Saved places, Recents, Year activity |

- Both are columns layouts.
- `builtin.desk` changes definition, so its `Version` goes up. That resets a working arrangement saved against the old definition. Desk hasn't shipped, so nothing real is lost.
- The layout picker lists both under Built-in, in the order Activity Atlas, Files First, Desk, Desk · separate panels.

## 3. The Files panel type

- `PanelTypes.Files = "files"`, display name **"Files"**. It is known and available. Its default span is full, and its minimum width is Recents'.
- **Files covers Saved places and Recents**, so a layout holds Files or those panels, never both:
  - `AddablePanelTypes` leaves out Files while a Saved places or Recents panel is shown, and leaves those out while Files is shown.
  - Hidden panels don't count: hiding Files makes Saved places and Recents addable again, and the other way round.
  - A layout file that holds both anyway (hand-edited) shows both. Nothing is dropped.
- **The chosen tab** is presentation only, like the shelf's grouping, which isn't stored either. Files opens on **Saved places**. The tab then stays as chosen while the app runs, across Arrange, and across switching layouts and back. It is not written to the layouts file, so changing it never marks a layout Modified. Remembering it across restarts is a follow-up if the trial sticks.

## 4. The tabs

Files hosts two existing views:
- **Saved places tab:** today's Saved places table (`PlacesPanel`), unchanged. It keeps its search, menu, Rename / Edit / Favourite / Remove, F2 / Ctrl+E / Ctrl+D / Delete, and Last opened / Opens.
- **Recent, Sessions and All tabs:** the Library grid (`FileShelfPanel`). Each tab sets the grid's source and its columns.

| Tab | Source | Columns | Also on the tab |
|---|---|---|---|
| Recent | Recents folders and Recent Files | Name, Type, Visits, Time, Folder, Last used | tracked-folder strip, Group by Type / Tag / Folder level, Add as place… |
| Sessions | files in any session | Name, Type, Sessions, Tags, Folder, Last used | session scope chip (§5), Group by Type / Tag |
| All | everything | Source, Name, Type, Where from, Tags, Folder, Last used | Group by Type / Tag / Folder level |

- **The Source column** shows up to three markers, left to right, for what the item is:
  - a new `Icon.Bookmark` glyph (in `Resources/Icons.xaml`, same 16×16 stroke style): a saved place;
  - `Icon.Clock`: recent, i.e. visited or opened in the period;
  - `Icon.Sessions`: in a session.

  Its tooltip is the row's *Where from* text. In the grid's automation name, the markers read as words ("Saved place, Recent, In a session").
- **Sessions column:** the names of the sessions holding the file, comma-separated.
- **The Show All / Saved / Recent segment** is hidden inside Files, because the tabs replace it. Recents panels outside Files keep it.
- **The period:** a day, week or month picked in Year activity narrows Recent, Sessions and All, as it narrows the shelf today. The Saved places tab is not narrowed.
- **Search:**
  - The workspace search box searches the Library grid, as today.
  - On the Saved places tab it searches that table.
  - Down from the search box moves into whichever tab is shown.
- **Empty tabs** say why:
  - Recent: "Nothing recent in this period.", or the tracking hint when no folder is tracked.
  - Sessions: "No session files yet. Save open files as a session from the Sessions panel."
  - All: the existing empty text.

## 5. Sessions → Files

- **The session card menu.** Session cards get a right-click menu: **View session files**, **Open all**, **Edit…**, **Delete…**.
  - The last three do what the details area's buttons do.
  - **View session files** shows only when the layout has a shown Files panel.
- **What View session files does:**
  1. Files switches to the Sessions tab.
  2. The grid is scoped to that session.
  3. A chip, "Session: *name* ×", shows above the grid.
  4. The list scrolls to the top.
  5. The status line says "Showing the files in *name*."
- **Clearing the scope:** × on the chip, choosing another tab, or choosing View session files on another card. The Sessions tab then shows every session's files again.
- **Renamed or deleted sessions:**
  - A session renamed while it is the scope keeps the scope, under its new name.
  - A session deleted while it is the scope clears the scope.
- **What the scope matches.** Session names are unique (`SessionStore` refuses a used name), so the scope is the session's name. It is not saved with the layout.

## 6. Model and view-model changes (UI-free, tested)

- `LibrarySourceFilter` gains **`Sessions`**: items in at least one session. `Saved` keeps its meaning (saved places and session files) for the Library window.
- `LibraryFilter` gains `string? Session`: only items whose sessions include it. `OnlyKind` requires it null. The year strip counts only those items' evidence, as it does for a tag.
- `LibraryViewModel`:
  - a `Tab` (Recent / Sessions / All) that sets the source and raises the column flags: `ShowsVisitsAndTime`, `ShowsSessions`, `ShowsSourceMarkers`, `ShowsWhereFrom`, `ShowsTrackedFolders`, and `ShowsFolderLevel`, which covers Folder level grouping;
  - `SessionScope` and `ScopeToSession(name)`, plus `ClearSessionScope()`;
  - on Sessions, a Folder level grouping falls back to Type.
- `LibraryRowViewModel`: `IsSavedPlace`, `IsRecent` and `IsInSession` for the markers, `MarkersText` for automation, and `SessionsText`.
- `SessionsViewModel`: `CanViewFiles`, set by the workspace while a Files panel is shown.
- `PanelTypes`: `Files`, and the rule that Files excludes Saved places and Recents, in `WorkspaceLayoutService.AddablePanelTypes`.

## 7. Views

- New `Views/Panels/FilesPanel.xaml(.cs)`: a tab strip (the app's segment style, as Group by uses) above a content area that holds the `PlacesPanel` and the `FileShelfPanel`. Only one is visible at a time. Both are created once and kept, so switching tabs keeps each one's selection and scroll.
- `FileShelfPanel`: binds each column's visibility and the Show segment's visibility to the view-model flags. It adds the Source template column and the session scope chip.
- `WorkspaceView.CreateContent`:
  - a `files` case builds the Files panel;
  - the workspace's `_shelf` and `_placesPanel` references point at its inner views, so search, the Down key, tracking and the period work as they do with separate panels;
  - it keeps the Files panel's content once made, as it does for every panel, so the tab survives Arrange and layout switches while the app runs.
- `SessionsPanel`: the card context menu, raising `ViewFilesRequested(name)`. `WorkspaceView` handles it by calling the Files panel's `ShowSession(name)`.

## 8. Testing

- **Unit (xUnit):**
  - `LibraryQueryEngine`: the Sessions source; the session filter on items and on the year strip.
  - `LibraryViewModel`: each tab's source and column flags; scope and clear; scope survives a rename and clears on delete; the Folder level fallback.
  - `LibraryRowViewModel`: markers, and markers text.
  - `WorkspaceLayoutService`: Files excludes Saved places and Recents in Add panel, both ways, hidden panels not counting; both Desk built-ins.
  - `WorkspaceViewModel`: both Desk variants are offered.
- **Windows (real clicks, scratch data root):**
  - Desk and Desk · separate panels, switching back and forth.
  - Each tab: columns, Source markers, empty texts.
  - Saved places tab actions (Rename, Toggle favourite).
  - Tracked-folder chips on Recent.
  - A calendar day narrowing Recent, Sessions and All, but not Saved places.
  - View session files from a card, the chip, ×, and another card.
  - The tab survives Arrange and a switch to another layout and back.
  - Dark and light themes.

# Desk layout and the Recents panel — design

Date: 2026-09-29. Status: approved in conversation, awaiting spec review. Builds on the [configurable canvas plan](260929_Configurable%20Canvas%20Implementation%20Plan.md) M1–M5 (branch `ccr-6156d37a-mo223d`).

## 1. Outcome

A new built-in layout, **Desk**, from the user's sketch:

```
+--------------------------------------------+
| QP header                        buttons   |   unchanged, full width
| Favourites ...                             |   unchanged, full width
+-------------+------------------------------+
| Sessions    | Saved places                 |
|             +------------------------------+
|             | Recents                      |   today's File shelf + the Recents window's features
|             +------------------------------+
|             | Year activity                |   the sketch's "Schedule"
+-------------+------------------------------+
```

To get there:

1. Layouts can be **columns** as well as **rows**: a left column a third wide and a main column two-thirds wide, each a vertical stack.
2. The File shelf becomes the **Recents** panel and takes over everything the Recents window does.
3. In the workspace, the header's Recents button goes.

Out of scope:
- A rows/columns switch for existing layouts.
- Header or favourites as panels.
- Column widths other than a third / two-thirds.
- A new "Schedule" panel.
- Changes to the list mode (no `--workspace`) or to tracking itself.

## 2. Column layouts

### Model (`Models/Workspace`)

- `LayoutPreset.Arrangement`: a string, `"rows"` (the default when absent) or `"columns"`. Any other value is kept as written and shown as rows.
- `BuiltInLayout` gets the same value in code. Save as new and Duplicate copy the arrangement from their source.
- `PanelInstance.Dock`: a string, `"left"` or `"main"`. Absent, or any other value, reads as main and is kept as written. Rows layouts ignore it.
- Working arrangements carry panels only. A layout's arrangement mode comes from its definition (built-in or preset), because nothing switches it.
- No schema-version bump: both fields are additive. An older build keeps them through `[JsonExtensionData]` and shows the layout as rows.
- `WorkspaceValidation` accepts the new fields and never repairs them away (D6).

### Engine (`Services/Workspace/PanelLayoutEngine`)

- `Pack` (rows) is unchanged.
- A new `PackColumns(panels, width)` returns `PanelPlacement`s with a new `Dock` member (`Left`, `Main`, or `None` for rows). Each placement's `Row` is its index within its own column, and its `Span` is 4 (left) or 8 (main).
- An empty column gives its width to the other: that column's panels get span 12.
- **Narrow window:** when a third of the width is below the minimum readable width (the same test `Pack` uses), every placement becomes full width with `Dock = None`, in the order main column then left column. This is presentation only; stored docks never change.
- `SamePanels` / `PanelInstance.SameAs` include `Dock`, so a column move counts as Modified and can be undone.

### View (`WorkspaceView`)

- In columns mode, the canvas holds two stack grids, `LeftStack` (4 of 12) and `MainStack` (8 of 12), each with one row per panel. Year activity's row is `Auto`; the others share the height with the existing 220px minimum. When the stacks can't fit, the canvas grows and scrolls (`FitCanvasHeight` sums per stack and takes the taller one).
- In rows mode and when narrow-stacked, `PanelCanvas` is used exactly as today.
- Frames are still made once. Moving a panel within a column only changes `Grid.Row`. Moving it to the other column, or switching mode, re-parents the frame. **Risk to verify:** panels keep their selection, scroll and focus across that Unloaded/Loaded, as M4 promises for moves.

## 3. Desk and Arrange mode

- `BuiltInLayouts.Desk`, id `builtin.desk`, version 1, arrangement `columns`. Panels:
  - Sessions, left.
  - Saved places, main.
  - Recents (`shelf`), main.
  - Year activity (`activity`), main.
- It is listed third in the picker, after Activity Atlas and Files First, with default filters. Restore built-in layout, Modified, Starts here, Save as new and Duplicate behave as for any layout.
- **Arrange mode on a columns layout:**
  - The width choice becomes a **Column** choice: *Left column* / *Main column*, with the tooltip and automation name "Column of <panel>". Changing it moves the panel to the bottom of the other column, as one step with Undo.
  - Move earlier / later become up / down (icon and name) and swap within the column.
  - Dragging the handle shows the drop line above or below the nearest panel in either column. Dropping on a panel in the other column changes its dock and position as one step. A drop into an empty column is offered by the empty column's area.
  - The right-edge resize handle is hidden.
- **Add panel** on a columns layout adds to the bottom of the main column, or shows a hidden panel where it was. Hide (×) is unchanged.
- `WorkspaceLayoutService` gains `SetDock(panelId, dock)`. `MoveBefore` also takes the target's dock in a columns layout. Every edit changes the draft only, as today.

## 4. The Recents panel

`PanelTypes.DisplayName(Shelf)` becomes **"Recents"** (panel title, Add panel, Undo labels, status lines). The stored type stays `shelf`. The Library window (list mode) keeps its "File shelf" wording.

The workspace's `FileShelfPanel` (with a new `ShowsTracking = true`, off in the Library window) gains the features below. They are driven by one shared `ActivityViewModel` over the app's `ActivityStore`, reusing its logic and the existing dialogs, not copies.

1. **Tracked folders strip**, above the kind chips:
   - A caps label "TRACKED FOLDERS", one chip per tracked root (Paused shown as in the window), a **Track a folder** button (`AddRootDialog`), and a line *Tracking 2 folders* / *Tracking paused* / *No folders tracked*, which replaces the header dot and tooltip.
   - Right-clicking a chip gives *Edit tracking settings…* (`ActivityFolderSettingsDialog`), *About folder…*, *Stop / Resume tracking* and *Delete tracked folder…*, with the same confirmations as the window.
   - Clicking a chip scopes the list to items under that root. Clicking it again clears the scope. The scope is part of the shared query, so a saved layout's filters can keep it. A root deleted later reads as no scope.
2. **Visits** and **Time** columns, always shown: the folder's visits and time in the period chosen on the calendar, blank for files and links. The existing Last used column is last visited for folders. Values come from the same `ActivityStore` period query the window's table uses, keyed by folder path.
3. **Group by: Type / Tag / Folder level.**
   - Folder level groups by depth below the item's tracked root: "Root folder", "Level 1 · directly below root", "Level 2 · below root", …, the window's own labels (`ActivityFolderRow.LevelLabel`).
   - Items outside any tracked root go under "Not in a tracked folder".
   - Grouping stays virtualized (the rows panel fix from 25277f6).
4. **Add as place…** in a folder row's context menu, when it isn't a saved place. It uses `MainViewModel.AddFolderFromActivity(folder, owner)`, then the workspace reloads the Library.
5. **Save problems:** the activity store's notice or a failed save shows in the panel with **Retry save**.

After any tracking change, the Library and the year strip reload (the existing `RequestReload`), and the tray refreshes.

## 5. Header and the Recents window

- In the workspace, `MainWindow` collapses `ActivityButton`, as it already does `LibraryButton` and `SessionsButton`.
- `UpdateActivityIndicator` keeps feeding the tray. The panel shows its own tracking line.
- List mode is unchanged. The Recents window stays for list mode and is removed with the other legacy windows in M7.

## 6. Testing

UI-free (`QuickerPlaces.Tests`):
- `PackColumns`: two stacks and their rows; empty left or main column; narrow window (main then left, full width); hidden panels skipped; unknown dock reads as main.
- Persistence: `arrangement` and `dock` round-trip; unknown values kept as written; an older file without them reads as rows; Save as new and Duplicate copy the arrangement.
- `BuiltInLayouts.Desk` definition; Restore built-in; Modified after a column move.
- `WorkspaceLayoutService` / `WorkspaceViewModel`: `SetDock`, up/down within a column, drop across columns, Add panel on a columns layout, Undo and Revert of each.
- `LibraryViewModel`: visits/time on folder rows for a period; Group by folder level (roots, levels, not tracked); the tracked-root scope, including a deleted root.

On Windows, with `--workspace --data-root <scratch>`, driven by UI Automation with real mouse clicks (see the memory note on real-click testing):
- Pick Desk: Sessions left; Saved places, Recents and Year activity stacked on the right.
- Arrange: set Sessions to Main column and back; drag Recents above Saved places; Undo; Done.
- Save as new from Desk; restart; it resumes with columns.
- Narrow the window below the threshold: one stack, main first.
- Track a scratch folder from the panel; right-click the chip to pause, resume, edit and delete; add a visited folder as a place.

## 7. Order of work

Each step is its own commit, with tests passing.

1. Model and engine: `Arrangement`, `Dock`, `PackColumns`, persistence and validation.
2. The view's two stacks, Desk, and Arrange for columns.
3. The Recents panel: rename, tracked-folders strip, Visits/Time, Folder level, Add as place, save problems.
4. Hide the header's Recents button in the workspace; `BUILD_SUMMARY.md` and user guide.

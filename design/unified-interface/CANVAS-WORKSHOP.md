# QuickerPlaces canvas: find by memory, organize by intent

This supersedes the window-consolidation recommendation in the first workshop. The user's priority is finding files faster and staying organized, with a configurable personal canvas built around the existing year activity blocks.

The year calendar was not removed from the application. The first previews put an illustrative version behind a disclosure, which hid an important part of the product's identity. The earlier “Improve activity tracking UI” chat confirms the original interaction: continuous week columns, stepped month boundaries, January 1 start, and Day/Week/Month selection that preserves activity colour beneath a separate outline. This round brings it forward as a retrieval control.

## Product idea

One Library supports three ways people remember things: **when I used it**, **what work it belongs with**, and **what I usually look for**. The canvas lets each person put those retrieval methods where they are useful. It should work well before the user customizes anything.

The following are three starter layouts for the same canvas system, not three incompatible products.

### 1. Activity Atlas — “I used it last Thursday”

The year blocks dominate the top of the canvas. Selecting a day, week or month filters a file shelf immediately; project and file-type filters refine both the shelf and the blocks. Opening, collecting or saving a document set happens directly below the selection.

The important loop is: **recognize a period → see the files → open the right one → keep the useful set**. The calendar becomes an index into work, rather than an activity report that sends the user back to another window.

This is the recommended default. It builds on the distinctive interaction already designed and requires very little setup from the user. The full year remains visible at useful widths; small panels switch to a readable month view instead of compressing the blocks beyond recognition.

### 2. Project Canvas — “It belongs with this job”

User-named collections hold references to folders, documents and links together. They can represent an active project, a reference shelf, an upcoming review or “Sort later.” Dragging a file onto a collection, or using an explicit Add to collection action, pins a reference without moving the original file.

The file shelf and activity blocks remain linked. A period selection narrows what a collection shows; the collection's total reference count stays distinct from the visible subset. Clear filter chips make this scope visible.

Collections are a proposed new concept. They are not filesystem folders and are not Sessions: collections can mix resource types and stay useful over time; Sessions are deliberately chosen sets of PDF, Word and Excel files to reopen together. Pinning to a collection must never silently add a file to a Session.

Tradeoff: intentional organization takes some user effort. Provide quick pinning and “Sort later”; avoid forcing classification before a file can be found or opened.

### 3. Personal Desk — “These are the things I always need”

Saved searches live alongside a file shelf, sessions and activity. A user can keep “All PDFs,” “Tower B documents” or “Reference folders” one click away. The saved query/type/project stays fixed while results update from the Library; the visible calendar remains a separate shared time filter.

The prototype lets users arrange panels, change their widths, hide with Undo, add available panels and restore the starter layout. The same operations are available in all three starter layouts. Arrow controls provide an alternative to dragging.

Tradeoff: customization can become work in itself. Keep Arrange mode explicit, make starter layouts useful, and keep the normal browsing surface locked against accidental panel movement.

## What the prototype demonstrates

- Year blocks with stepped month separators, separate today/selection outlines and Day/Week/Month selection.
- Date, type and project filtering that updates known-item results together.
- Search across names, paths, tags and session membership.
- Open actions simulated with status feedback.
- User-named collections and pinning, including a drag-to-collection interaction and an explicit button alternative.
- Saved searches that can be applied again.
- Turning a reviewed document-only result set into a named Session.
- Panel order, widths, addition, removal, Undo and starter-layout restoration.
- Local preview state saving and dark/light appearances.

All content and activity are illustrative. No real files are opened, moved, indexed or tracked by the preview. The prototype project associations are sample metadata, not existing inferred relationships in QuickerPlaces.

## Design boundaries worth keeping

**Start with a snapping layout.** A twelve-column canvas gives users useful spatial control while preserving reading order, resizing and keyboard access. Overlapping freeform windows introduce occlusion, empty space and lost controls. A truly freeform canvas can be an optional advanced mode later if users demonstrate a need for it.

**Keep one selection context visible.** Search, project, type, time and collection scope should appear as clear controls/chips. Avoid panels mysteriously filtering each other. Some utility panels, such as saved Sessions and favourites, can remain explicitly unfiltered shortcuts.

**Keep the meanings separate.** The unified activity view needs an honest measure and source labels. Folder active time is not document editing time. Recent Files provides recorded opens, not document duration. Sessions and places have different launch semantics. A Library-wide “known items used” count requires a defined deduplication rule; it is not interchangeable with the current activity heat measures.

**Respect real history limits.** Links have last-open metadata rather than a complete activity history; file recording is opt-in and begins when enabled; folder detail has retention limits. A production calendar must distinguish unavailable history from a day with known zero activity. The mock's heat pattern is sample data, not evidence that this history is currently available.

**Organize references, not the filesystem.** A collection should never imply a move, copy or rename. Folder/URL places, mixed collections, dynamic saved searches and document Sessions need clear names and predictable actions.

**Keep recovery simple.** Hide panel removes a view, not its underlying data. Undo restores it; starter-layout restoration changes arrangement only. Deleting a real collection or session is a separate action from removing its panel.

## Suggested implementation order

### User-saved presets (requested after preview review)

Keep Activity Atlas, Project Canvas and Personal Desk as built-in starting presets alongside a user's named presets. A layout picker should group Built-in and My layouts. Arrange mode offers Save changes, Save as new layout and Revert; editing a built-in uses Save as new so the original remains recoverable. Support rename, duplicate, delete and Set as startup layout for personal presets. Remember the current working arrangement across restarts without silently overwriting a named saved preset.

A preset records stable panel instance IDs, panel types, order, column spans, visibility and panel-specific view choices. Save layout alone by default; optionally include the current project and filters. A relative range such as This week stays relative when restored. Sessions and collections are referenced by stable IDs rather than copied into every preset; switching layouts changes views, never content. Missing references should be visible and repairable, and newly introduced panels should not rearrange existing saved layouts.

Native WPF implementation is feasible using reusable UserControls and a shared workspace view model, with an ItemsControl/custom snapping Panel for arrangement and drag/drop plus keyboard move controls. The existing LibraryViewModel already aggregates the relevant sources and supplies a year strip; extracting reusable views can retain that logic. Resizing requires explicit minimum widths and reflow rules, including month mode when a full year is no longer readable.

Persist named presets in a versioned layouts store with atomic replacement, backup/recovery and visible save errors. The current SettingsService silently falls back to defaults and swallows save failures, which is appropriate for incidental window state but insufficient for user-authored layouts. AppSettings can retain the selected/startup preset ID. The current HTML prototype remembers one working arrangement per starter layout; named preset management is a requested next capability, not implemented in that preview yet.

1. Build the Activity Atlas retrieval flow over the current Library and calendar. Preserve existing date/period selection semantics and source-aware launching.
2. Separate a shared query/selection model from panel rendering. Each panel receives an explicit scope; pinned utilities declare that they ignore date filtering.
3. Add panel layout preferences: stable panel IDs, ordering, span, visibility and named layout presets. Keep preferences outside the content stores.
4. Add saved searches as reusable query definitions, then user collections as resource references. Define project membership deliberately before introducing any automatic grouping.
5. Connect the reviewed Library document set to the existing Session editor and store. Do not treat mixed search results as directly launchable sessions.
6. Test the practical outcome: can a person recover a file from an approximate date, pin it for next time, and reopen the related documents with fewer steps than today?

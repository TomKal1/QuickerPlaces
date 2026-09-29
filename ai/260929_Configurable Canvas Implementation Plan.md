---
title: QuickerPlaces — configurable canvas and saved layouts
status: in progress; M0–M3 implemented on ccr-6156d37a-mo223d (see BUILD_SUMMARY.md)
created: 2026-09-29
branch: codex/configurable-canvas-plan
baseline: main at 6ba577d22cd38ac28424233b6e238f4a81fa4d81
---

# Configurable canvas and saved layouts

## 1. Outcome and scope

Make QuickerPlaces one workspace where people recover files by remembering when they used them, what work they belong to, or a search they use repeatedly. Keep the custom year activity blocks prominent and let users arrange useful panels and save named layouts alongside three built-in presets.

The user approved the canvas direction and requested personal presets. This document plans implementation; it does not authorize treating an HTML prototype as production-ready code. The preview's files and activity are illustrative. Product rationale and the three concepts are in [the canvas workshop](../design/unified-interface/CANVAS-WORKSHOP.md). This is a new UI/product workstream following the [Sessions and Library plan](260928_PDF%20Project%20Sessions%20Plan.md), not a claim that the other roadmap phases are complete.

Success is this complete loop: **select a remembered period → find a file → open it or keep it in a collection → save the workspace arrangement → restore it after restarting.** A user can also review a document set and save it through the existing Session editor.

Deliver in two increments on this branch: a usable Activity Atlas with rearrangement and personal presets first; then Collections and Saved searches so all three built-in layouts have their intended behavior. Complete both increments before calling the full plan done.

### In scope

- One native WPF main workspace with global search, favourites and a layout picker.
- Reusable Year activity, File shelf, Sessions, Collections, Saved searches and Saved places management panels.
- Shared, visible query scope; Day/Week/Month date selection and clear filters.
- Snapping panel arrangement, width changes, hide/add, Undo, keyboard alternatives and responsive reflow.
- Immutable built-in presets, user-named presets, startup preference and recovery of the working arrangement.
- Real data integration, truthful history labels and existing launch behavior.
- Collections of references and reusable saved searches, stored independently of layouts.

### Deferred

Overlapping freeform windows, floating/dockable windows, third-party widgets, multiple copies of a panel, cross-device sync, preset import/export, automatic project inference, new file tracking, document editing duration and application-window restoration. No WebView dependency or new UI framework is required. Existing opt-ins stay in force.

## 2. Current code and integration seams

Paths below are relative to `src/QuickerPlaces/` unless stated otherwise. Verify this inventory again if main advances before implementation.

| Existing code | Reuse and required change |
|---|---|
| `App.xaml.cs` | Owns service construction, single-instance gate, tracking hosts and shutdown. Construct workspace stores/view model here, after the single-instance gate; do not create a tracking host per panel. |
| `Views/MainWindow.xaml` and `.xaml.cs` | Currently bind `MainViewModel` and open Activity, Sessions and Library windows. Retain the window identity, tray/hotkey and bounds handling while replacing its content with the workspace shell. |
| `ViewModels/MainViewModel.cs` | Preserve place CRUD, favourites, usage updates, Recently Deleted, import/export and persistence feedback through a places panel/command adapter. Do not lose these behind a simplified file list. |
| `ViewModels/LibraryViewModel.cs` | Already combines places, sessions, folder activity and recent files. Currently uses a nullable selected day and reloads source data. Extend to an explicit period/query model without duplicating its launch and deduplication rules. |
| `Services/Library/LibraryIndex.cs` | Pure merge/search logic. Reuse canonical resource matching and source provenance. Extract its identity rules into a shared helper before storing collection references. |
| `ViewModels/ActivityCalendar.cs` | Already accepts a selected start/end range and builds year cells/stepped month geometry. Reuse it; share the duplicated calendar presentation in Activity and Library views. |
| `Views/ActivityWindow.xaml`, `LibraryWindow.xaml`, `SessionsWindow.xaml` | Extract useful content into UserControls. Keep window wrappers during migration until workspace parity is verified. |
| `Services/Sessions/*`, `ViewModels/SessionsViewModel.cs`, `SessionEditorViewModel.cs` | Reuse capture, review, launch and save behavior. Sessions remain document sets containing PDF, Word and Excel files. |
| `Services/SettingsService.cs`, `Models/AppSettings.cs` | Good for incidental window preferences; currently silently reset unreadable settings and swallow save errors. Do not use this failure policy for named layouts. |
| `Services/FilePlacesStorage.cs`, `JsonStoreLoader.cs`, `Models/PersistenceResult.cs` | Existing atomic write/backup and load-outcome seams. Reuse without weakening other stores' recovery behavior. |
| `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj` | Plain `net10.0` project links UI-free application sources. Add new model/service/view-model files explicitly; keep WPF types out of them. |

## 3. Product decisions

### D1. A snapping canvas with an explicit Arrange mode

Use a twelve-column layout with stable ordered panels and spans of 4, 6, 8 or 12 columns. The layout engine packs panels in order without overlap. Dragging previews an insertion point; dropping commits a single reorder. Width handles snap to allowed spans; labelled width choices provide an alternative. Pointer movement does not write to disk.

Normal browsing is locked against panel rearrangement. Arrange mode provides drag handles, Move earlier/later, width controls, Add panel and Hide panel. Escape cancels an active drag first, then leaves editing only through the normal Revert/Done action. Hide affects presentation only and offers Undo. An empty canvas still exposes Add panel and the layout picker.

Reflow derives from actual available width and minimum panel sizes. Narrow windows stack panels without changing stored desktop spans or order. No stored absolute screen coordinates. A year view changes to a legible month view when its full-year width cannot fit; it does not shrink day targets indefinitely. Calendar selection survives this switch.

### D2. Presets are saved definitions; working arrangements are separate

The layout picker has **Built-in** and **My layouts** groups. Built-ins are code-defined and cannot be overwritten or deleted. Editing one offers Save as new layout. User presets support Save changes, Save as new, Rename, Duplicate, Delete and Set as startup layout. Names are trimmed, nonempty, at most 80 characters and unique ignoring case within My layouts; IDs, not names, identify references.

Entering Arrange captures a transaction snapshot. Done keeps and persists the working arrangement; Revert restores the entry snapshot. Save changes explicitly replaces a user preset's saved definition, while Save as new creates another preset and selects it. Show a Modified indicator when a working arrangement differs from its definition. Provide Restore saved layout separately from Revert, with Undo for that restoration.

Changing presets first preserves the current working arrangement, then restores the destination's working arrangement if present, otherwise its definition. Built-in working arrangements can therefore be personalized without altering the factory definition. Restore built-in layout clears only that preset's arrangement changes.

Startup defaults to Resume last workspace. A user may instead select a specific preset; startup restores that preset's working arrangement when available. Deleting the selected or startup user preset falls back to Activity Atlas and clears dangling selection references in the same store transaction. Offer Undo for deletion; deleting a preset never deletes Sessions, collections, searches or files.

### D3. Save layout first; save filters by choice

A preset saves panel IDs, types, order, spans, visibility and per-panel view choices. The Save dialog offers **Include current filters**, off by default. Included filters capture query, kind, source, collection/tag scope and date rule. Never capture selected result rows, scroll position, transient errors or file contents in a named preset.

Date rules are typed: All recorded time, This week, This month, or an explicit inclusive range. Relative rules resolve using `TimeProvider` and local culture on activation and across midnight; This week never becomes a permanently saved pair of dates. When activating a layout without saved filters, clear query scope to defaults; a recovered working arrangement may restore its separately remembered query. Make these semantics testable and visible rather than allowing hidden filters to leak between layouts.

### D4. One explicit query context

The workspace owns search text, kind, source, collection/tag scope, date rule and selection. File shelf and activity use this context. The calendar's heat is computed across its displayed year with all applicable non-date filters; selecting a date changes the shelf and the selection outline, not the year's heat distribution.

Favourites and saved Session shortcuts remain unfiltered utilities, labelled accordingly. A collection's total reference count remains distinct from matches in the current scope. Saved searches apply their stored query/kind/source/scope while retaining the shared time filter by default; date behavior is stated beside the search action.

There is no reliable generic Project entity in the current app. For this release use explicitly selected collection membership or existing Session tags, with those accurate labels. The prototype's Project selector must not become an inferred project database or treat arbitrary path segments as projects.

### D5. Calendar evidence must match what the stores know

Preserve continuous week columns, stepped month boundaries, culture-specific weeks, separate today/selection outlines and clickable future/untracked dates. A selection on an unavailable date produces an explanatory empty state.

Current Library heat adds folder visits, recorded file opens, session saves and session reopens; it is not a deduplicated count of files used. Retain a clearly labelled **Recorded activity** measure with a source breakdown for the first delivery. Folder duration is available only in its appropriate activity view. Do not use the prototype's “known items used” legend until an exact, tested per-resource measure is available.

Folder totals can outlive retrievable folder detail; links have last-open metadata rather than a full timeline; tracking starts independently and can be paused. Represent source coverage as Available, Partial or Unavailable with reasons. Do not silently equate an empty result with zero activity. For filtered heat that cannot be reconstructed from retained detail, show unavailable/partial coverage instead of showing unfiltered totals as filtered results. Preserve current Session-event evidence and identify it as such rather than claiming every member file was observed open.

### D6. Content survives views

Collections pin references to folders, links and supported documents; they never move originals. Saved searches store query definitions. Sessions continue to save reviewed document sets. Removing a panel, changing a preset or resetting a layout cannot modify any of those content stores.

One panel of each type is enough for the first version. Use panel instance IDs anyway so a later multi-instance design does not require changing every reference. Newly added panel types appear in Add panel without changing the user's saved arrangement. Unknown panel types are preserved as unavailable placeholders so an older build cannot silently erase newer configuration.

## 4. Proposed architecture and persistence

Names below are implementation targets, not files claimed to exist today.

| Area | Proposed files and responsibility |
|---|---|
| Layout definitions | `Models/Workspace/WorkspaceDocument.cs`, `LayoutPreset.cs`, `PanelInstance.cs`, `WorkspaceQuery.cs`: versioned, UI-free records with stable IDs and validated values. |
| Preset behavior | `Services/Workspace/BuiltInLayouts.cs`, `WorkspaceLayoutService.cs`: defaults, copy/rename/delete/save, draft transactions, Undo and dirty comparison. |
| Layout calculation | `Services/Workspace/PanelLayoutEngine.cs`: pure placement calculation using ordered spans and available width; independently testable. |
| Storage | `Services/Workspace/WorkspaceStore.cs`: load, validate, migrate, save and recover named definitions plus working arrangements. |
| Shared workspace | `ViewModels/WorkspaceViewModel.cs` and `WorkspacePanelViewModel.cs`: query coordination, selection, commands and panel registry; adapters reuse existing feature view models/services. |
| WPF shell | `Views/WorkspaceView.xaml`, `Views/Panels/*View.xaml`, `Controls/WorkspacePanel.cs`, `Controls/PanelFrame.xaml`: presentation, WPF measure/arrange, drag/resize adorners and keyboard actions. |
| Preset management | `Views/SaveLayoutDialog.xaml`, `ManageLayoutsDialog.xaml`: naming, optional scope, startup choice and recovery feedback. |
| Organization | `Models/Organization/*`, `Services/Organization/OrganizationStore.cs`, `CollectionsViewModel.cs`, `SavedSearchesViewModel.cs`: references and reusable queries shared by layouts. |

Use `%LocalAppData%/<publisher>/<app>/workspace-layouts.json` for named layouts, active/startup selection and working arrangements. Keep them together for atomic updates when deleting or switching presets; window size/theme remain in existing settings. This refines the earlier workshop suggestion of storing a preset ID in AppSettings and avoids a two-file consistency dependency. Use a separate versioned `organization.json` in the same local directory for collections and saved searches; existing Places/Sessions stores and their locations remain intact.

A layout document contains schema version, user presets, working arrangements keyed by preset ID, active preset ID and startup mode/ID. Built-ins are supplied by code and copied into working records when edited. Version their definitions so upgrading factory defaults does not overwrite existing personalizations.

Validate duplicate IDs, spans, panel types and reference integrity on load. Repair isolated invalid values with visible notice while retaining usable content; preserve unknown fields/types when required for forward compatibility. A newer document version or unreadable file is left untouched and disables writes; built-in layouts remain usable in memory. For corrupt files, follow established quarantine/recovery conventions and offer the last backup instead of quietly resetting personal layouts. Reuse `FilePlacesStorage` atomic replacement and backups with explicit paths.

Explicit saves return persistence results and show a Retry banner on failure. Do not claim a preset was saved until the write succeeds. Failed saves retain pending state in memory. Persist working state after completed layout actions and debounce search/filter changes; flush pending writes on normal close. Integrate unsaved changes with the existing close flow before disposing trackers/services. Arrange-mode pointer movement and window reflow produce no writes.

Collection resources need their own stable reference IDs plus kind and normalized location, and optional existing Place ID. Reuse current document/path and Library identity rules, not a new filesystem scan. Retain display data for unavailable files so references remain intelligible; opening reports the normal missing-file error and can offer relinking. Saved search and collection IDs referenced by layouts survive renames; removed references show a repairable unavailable state rather than silently broadening a filtered search.

## 5. Implementation milestones and intended commit sequence

### M0 — Baseline and feature inventory

- Recheck main changes against this plan; inspect existing feature tests and UI behavior.
- Record baseline `dotnet test` and Windows application build results before changing code.
- Inventory MainWindow, Recents, Library and Sessions commands, dialogs and shortcuts; use this as the migration parity checklist.
- Run development UI checks against isolated test stores, never the user's live AppData. Add an injectable development data-root/composition seam if required, without changing the normal default paths.

**Exit:** reproducible baseline and a concrete parity checklist; no functional change.

### M1 — Layout domain, preset state and reliable persistence

- Implement models, built-in registry, working-versus-saved state, validation and store recovery.
- Define Activity Atlas (activity 12; shelf 8; sessions 4), Project Canvas (collections 12; shelf 8; activity 4), Personal Desk (shelf 8; searches 4; activity 8; sessions 4).
- Keep presets requiring panels not yet implemented out of the user-facing picker until M6; do not ship dead placeholders as completed features.
- Implement all preset operations and startup fallback in UI-free services with storage-failure tests.

**Exit:** save/reload/rename/duplicate/delete/Undo/startup behavior is proven independently of WPF.

### M2 — Shared period/query model and reusable existing panels

- Extract calendar, Library shelf and Sessions content into controls with shared resources; preserve existing window wrappers.
- Extend Library queries from selected day to an inclusive date interval. Keep deduplication and all source annotations when a resource appears in multiple stores.
- Introduce shared query scope, filtered year heat and coverage states; reuse `ActivityCalendar` range geometry.
- Implement refresh coordination after launches, content changes and tracking updates. Snapshot/query services off the UI thread where necessary; apply results on the Dispatcher and discard stale query generations. Dispose event subscriptions when views are removed.
- Retain virtualized file lists and stable selection by resource identity rather than recreating every row for every layout move.

**Exit:** selecting a day/week/month returns the correct real-source results and does not mislabel unknown history; old wrappers still work.

### M3 — Activity Atlas in the main shell

- Add `WorkspaceViewModel` and host the reusable panels in MainWindow behind a temporary internal development switch.
- Keep global search/layout controls in the shell and local controls within panels; retain fonts, themes, highlight choices and racing-stripe/heat styling from shared resources.
- Adapt favourites and places management commands. Provide an Add-panel entry for the Saved places manager; expose existing maintenance/settings actions in a predictable menu.
- Connect the shelf's “Save file set as session” to the existing review editor, excluding folders/links and deduplicating documents before review. Keep detection/check states and partial-open feedback.

**Exit:** find-by-date → open → create/reopen a Session works from one main window with current data semantics.

### M4 — Arrange mode and responsive layout

- Implement the pure packing engine and WPF panel adapter; add drag insertion preview and snapping width handles/options.
- Add keyboard Move earlier/later, add/hide/Undo, Done/Revert and built-in restore.
- Preserve focus, row selection and query when moving or resizing a panel. Announce meaningful changes through automation peers/status text.
- Support stacked layouts, readable month fallback, large text and DPI changes without rewriting stored spans.

**Exit:** rearrangement is reversible and keyboard usable, with no overlaps, offscreen controls or per-pointer-event persistence.

### M5 — User-facing saved presets

- Add grouped picker, Modified indicator, Save changes/Save as, rename/duplicate/delete with Undo, startup preference and Restore saved layout.
- Implement Include current filters and relative date semantics in the save dialog.
- Surface load/save failures and retry actions; validate recovery after restart and accidental deletion.

**Exit:** a user can create two distinct personal layouts, switch between them, recover working changes, and restart into the chosen layout without modifying the built-in definitions. This completes the first delivery increment.

### M6 — Collections, Saved searches and the other two presets

- Implement organization storage, stable references and missing-reference handling.
- Collections support create/rename, pin/unpin by button or drag, reorder references and delete with Undo. Pinning never changes an original file or Session.
- Saved searches support create/update/rename/delete and applying explicit query scope; distinguish them visually from manually pinned collections.
- Let explicit collection/tag scope refine shelf and calendar where underlying history permits. Explain partial coverage.
- Activate Project Canvas and Personal Desk after their panels are real, with useful empty states for people who have no collections/searches yet.

**Exit:** all three presets work with real user data, share the same content, and remain independently customizable.

### M7 — Switch the default and close parity gaps

- Make the workspace the default MainWindow content after completing the parity checklist. Consolidate obsolete launch buttons into workspace actions; remove temporary wrappers only when no callers depend on them.
- Retain settings/edit/capture/recovery dialogs where a focused dialog is appropriate. Preserve tray start/close behavior, global hotkey search focus, second-instance activation, multi-monitor bounds and normal shutdown.
- Update user-facing documentation, `ai/BUILD_SUMMARY.md` and this plan's status with actual results and explicitly untested checks.

**Exit:** acceptance checks below pass; no dummy data or prototype-only controls remain.

## 6. Verification

Add meaningful tests alongside each milestone. Follow the repository's UI-free linked-source convention; do not construct WPF Windows in the portable test suite.

| Test area | Required evidence |
|---|---|
| Layout engine | Stable ordering, legal spans, no overlap, narrow reflow preserving saved widths, empty layout, minimum width and drag cancellation. |
| Presets | Built-ins immutable; copies independent; IDs stable through rename; draft/saved distinction; switch/restart/startup behavior; deletion fallback and Undo; unknown panels preserved. |
| Storage | Atomic save/backup, malformed input, newer version write protection, failed write/retry, missing references, migration and pending changes at close. Use temporary/injected storage. |
| Query/calendar | Inclusive day/week/month bounds, culture week starts, leap day/year crossing, DST/local date handling, rolling relative periods, source deduplication and missing/partial history. Heat honors non-date filters without collapsing to selected dates. |
| Organization | Pin deduplication, no file mutation, content shared across presets, collection rename/delete references, saved-query/time interaction and empty results. |
| Launch regression | Correct gateway for saved places, existing usage count semantics, document-only Sessions, missing-file/partial-launch behavior and capture checkbox defaults. |

From the repository root on Windows:

```powershell
dotnet test src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj
dotnet build src/QuickerPlaces/QuickerPlaces.csproj -c Release
```

Run focused tests during implementation, then the full suite and Release build at integration gates. For this documentation-only planning change, these commands are planned checks, not tests claimed to have run.

Manual Windows acceptance, using test stores:

1. Recover a known file from an approximate week, refine by type, open it, and keep it in a collection without opening another browsing window.
2. Exercise every main command in the M0 parity inventory, including tracking settings and Recently Deleted.
3. Rearrange using both mouse and keyboard; resize, hide/Undo, cancel a drag, and revert an Arrange session.
4. Save two personal presets, include filters in one, restart, switch between them and restore a built-in. Confirm content is unchanged.
5. Test This week across a controlled date rollover, a missing resource, expired history and disabled tracking.
6. Test dark/light/system themes, highlight choices, 100/150/200% DPI, Windows text scaling, a normal 1000×650 window, maximized and narrow supported widths. No clipped controls or inaccessible year cells.
7. Test hotkey focus, tray lifecycle, second-instance activation and monitor removal. Confirm one tracking host per feature throughout.
8. Use a fixed large fixture (10,000 Library items) to measure warm search, period filtering, preset switching and dragging. Record machine/data size and timings. Target warm query/switch response within 200 ms and no sustained UI-thread stalls during dragging; profile missed targets before release rather than claiming performance from visual inspection.
9. Simulate locked/full-disk writes and corrupt/newer-version layout files. Verify visible feedback, retry and recovery without overwriting protected bytes or blocking access to built-in layouts.

## 7. Completion and handoff

- [x] M0 baseline and parity inventory recorded ([M0 document](260929_Configurable%20Canvas%20M0%20Baseline%20and%20Parity.md); Windows Release build still to run).
- [x] M1 reliable preset domain/store implemented (UI-free; 60 tests).
- [x] M2 shared query and reusable existing panels implemented (engine, periods, coverage, panels; not yet run on Windows).
- [x] M3 real-data Activity Atlas integrated behind `--workspace` (shell, Saved places panel, shared search, Save as session; not yet run on Windows).
- [ ] M4 arrangement and accessibility verified.
- [ ] M5 personal preset UX and restart behavior verified.
- [ ] M6 Collections, Saved searches and all built-in presets complete.
- [ ] M7 default workspace, regression checks and documentation complete.

Implementation can proceed without another architecture-choice round: native WPF, snapping panels, immutable built-ins plus personal presets, explicit query scope and separate content stores are the agreed direction. Revisit only a concrete conflict discovered in code or testing. No source-code implementation, build result or deployment is implied by this planning branch.

# Graph view: folder clusters, file exploration and calendar filtering

Date: 2026-10-14. Status: Planned; no graph implementation has started. This records the agreed direction and the proposed implementation sequence. Performance figures below are acceptance targets to measure, not results.

## 1. Outcome and scope

Add **Graph** beside Saved places, Recent, Sessions and All in the Files panel. It is a native WPF map of the filesystem resources QP knows about. At overview scale, users see large folder clusters. Clicking a cluster or zooming into it reveals subfolders and files. Users can inspect connections and open individual files or folders.

The graph follows the existing calendar, search, kind, tag and tracked-root filters. It belongs to the workspace and follows its Dark, Light and Match Windows themes and chosen highlight colour.

### Included in the first release

- Stable folder clusters, nested subfolders, individual file nodes and progressive detail.
- All recorded history, with incremental loading and honest coverage reporting.
- Existing day/week/month/range calendar filtering and shared search/filter behaviour.
- Containment relationships and session connections for the selected item; optional shared-tag connections.
- Pointer and keyboard exploration, file/folder opening, an accessible results list, details, and theme-aware rendering.
- Loading, empty, unavailable-history, missing-resource and large-folder states.

### Deferred

Document-content indexing, inferred semantic similarity, manual relationship editing, continuous force simulation, independently published NuGet packages, a whole-disk crawler, and cross-device path reconciliation beyond QP's existing rules. No new tracking mechanism is needed for this view.

## 2. Existing integration points

Paths below are relative to the repository root.

| Existing code | Use in this feature |
|---|---|
| `src/QuickerPlaces/Views/Panels/FilesPanel.xaml` and `.xaml.cs` | Add the Graph segment, graph host and graph toolbar; retain current tabs and lifecycle. |
| `src/QuickerPlaces/ViewModels/LibraryViewModel.cs` | Shared query, resolved period, calendar, current filter state, refresh generations, opening behaviour and coverage. |
| `src/QuickerPlaces/Models/Workspace/WorkspaceQuery.cs` | Existing saved query and `DateRule` semantics, including relative periods and local-day resolution. |
| `src/QuickerPlaces/Services/Library/LibrarySnapshot.cs`, `LibraryQueryEngine.cs`, `LibraryIndex.cs` | Existing source aggregation, resource identity, source provenance, Revit identity resolution and query rules. |
| `src/QuickerPlaces/Services/History/ActivityHistory.cs`, `HistoryMonthCache.cs`, `HistoryMerge.cs` | Historical reads, machine/store cutoffs and merge rules. |
| `src/QuickerPlaces/Views/WorkspaceView.xaml.cs` and `Views/MainWindow.xaml.cs` | Composition, shared search routing, focus and workspace lifecycle. |
| `src/QuickerPlaces/Services/ThemeManager.cs`, `Resources/Palette.Dark.xaml`, `Palette.Light.xaml`, `Styles.xaml`, `Icons.xaml` | Current runtime colour resources, typography, controls and icons. |
| `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj` | UI-free tests; keep this project runnable without the Windows desktop framework. |

The current Library's no-period item list uses current store summaries, even when older history is loaded for its calendar. Do not assume `Rows`, or simply loading more months, provides every historically known file. The all-history graph needs an explicit history projection. The current query result also contains items before the kind filter is applied; the graph must apply the actual kind filter just as `BuildRows` does.

## 3. Product and data decisions

### G1. Known resources, with provenance

Graph uses the All-source scope while active, matching the All tab's meaning for saved places, session files, folder activity and recorded file opens. It adds archived filesystem resources when All recorded time is selected. Reuse current source eligibility rules rather than inventing different tracking consent or file-type rules.

Files with recorded opens are normal file nodes. Saved/session-only files may also appear, but details must say **In a saved session** or **Saved place**; they must never claim a recorded open. A session saved or reopened during a selected period can make its files eligible under existing Library rules; that is session evidence, not proof that every file opened successfully.

URL places do not become filesystem nodes. With Links selected, show an explanation and an action to view those results in All. Calendar heat can include link/session events under the shared query; explain that calendar activity counts and graph file counts measure different things.

### G2. Folders are both resources and structure

Create folder ancestry from known paths without enumerating disk contents. An ancestor added to connect matching descendants is a structural node, not a recorded folder visit. Retain folder-only resources when folder activity or saved-place evidence matches the query.

Use existing Windows path identity rules for case, separators, UNC shares and resource merging. Prefer the deepest containing tracked root when roots overlap, assign each file once, and preserve the physical hierarchy between nested roots. Files outside tracked roots remain browsable under their drive/share branches. Respect existing confirmed Revit central/local identities; never merge resources by similar filenames.

Do not resolve symlinks, check existence for every node, walk network folders or read document contents during graph construction. Check availability lazily for the selected resource or on Open. Historical paths remain useful even if the file has moved or disappeared.

### G3. Stable hierarchy supplies the layout

Use deterministic hierarchical cluster packing, with ordered children and reserved cluster bounds. Avoid an all-pairs force layout. Root and folder positions come from the known hierarchy, not the current day's activity. Keep a position cache by resource identity and topology revision, and reuse positions for surviving nodes.

Top-level clusters start at tracked roots or useful drive/share branches; avoid an overview consisting only of one giant `C:` node. Compress chains containing a single branch into a breadcrumb, retaining every actual folder in the model. Clicking the breadcrumb allows intermediate folders to be inspected and opened.

Cluster area reflects known descendant file count using a bounded, sublinear scale. Calendar changes update matching counts and visibility without resizing/repacking the world. A genuinely new hierarchy may require local expansion; stage the result with a short transition and preserve the focused item. Fit is an explicit action after the first usable result.

### G4. Connections remain understandable

Containment is always available and visually quiet. For the selected file/folder, optionally reveal shared-session connections, then shared-tag connections. Session membership comes from saved sessions, not inferred co-opening. Represent shared sessions/tags through relationship hubs or indexed adjacency, avoiding a clique between every pair of members.

Only draw edges whose endpoints are visible at the current detail level. Aggregate relationships to visible folder clusters when descendants are collapsed; deduplicate edges. A matching file connected to an excluded file does not pull that file into the current filter. Details may say that additional related files are outside the filters.

## 4. Calendar and filter contract

**The existing calendar is the date authority. There is no independent graph date picker.** Graph consumes the accepted shared query revision, resolved local date interval, scope and coverage. Browsing to another calendar year/month does not itself select that period; preserve existing calendar behaviour.

| Calendar/query change | Required graph behaviour |
|---|---|
| Select a day, week, month or inclusive range | Show eligible resources with evidence in that interval and their necessary folder ancestors. |
| This week / This month | Resolve using the existing `TimeProvider`, local timezone and culture's week start. Refresh after date rollover/resume as the calendar does. |
| Clear the period / All recorded time | Restore all eligible known filesystem resources, including archived ones as their index becomes available. |
| Select a period with no matches | Show a precise empty state and Clear date filter action; retain the camera for return. |
| Change search, kind, tag or tracked-root scope | Apply the same shared filters and keep structural ancestors as navigation context. |
| A selected file is filtered out | Clear its actionable selection and explain why; do not retain an invisible launch target. Restore folder focus to the nearest visible ancestor if useful. |
| Change filters rapidly during history/layout work | Cancel superseded work where possible and reject stale completions by revision. |

Default filtering hides nonmatching file nodes rather than leaving thousands of faded dots. Empty branches are omitted; the underlying layout coordinates stay cached. A structural ancestor has an **Ancestor folder** detail label and no invented opens/visits.

Cluster badges report **matching distinct descendant files** for the current query, not an inflated sum of sources or session memberships. Folder visit counts, file opens and session events stay separate. At All recorded time, show a total only when coverage is complete; while indexing say, for example, `1,240 files found · loading history`.

Preserve the calendar invariant: date selection changes the graph's membership, but does not narrow the year's heat to the selected dates. Graph focus, camera, connection toggles and selection do not modify the shared query or the calendar's heat. Graph follows the same non-date filters used by the calendar. Folder focus is local navigation; a separate explicit filter action would change shared scope.

For a selected period, match the All tab's filesystem membership and provenance when given the same sources and history, apart from structural ancestors. For All recorded time, Graph deliberately includes older archived resources beyond the current All table; state that distinction in help text and test it. Changing the All table's retention behaviour is outside this feature.

## 5. Visual specification

The result should feel like a finished QP workspace: restrained, spacious, crisp and readable in both themes. Establish these rules in the local design-system documentation before implementing new styles. Reference `design/design-system-export/QuickerPlaces-Design-System.md`; record the graph as an extension to its visual vocabulary.

### Composition

- Keep the current calendar visible and usable alongside Graph in layouts which contain it.
- One compact graph toolbar: current period/scope summary, Connections menu, Fit, and zoom controls. Keep the workspace search as the search entry point.
- A flat, lightly bordered graph surface; no decorative grid, heavy glow, glass effect or deep shadows.
- Folder regions have softly rounded boundaries, a short folder heading and count. File nodes are compact points with a type glyph/label at sufficient zoom. Small circular graph points are an explicit graph-specific exception to the current design system's circle restriction; ordinary controls keep existing shapes.
- Details appear in a right-hand pane when space allows; at narrow widths use a dismissible overlay/drawer. Do not squeeze the graph into an unusable strip.
- Respect the Files panel's minimum usable height and existing workspace scrolling. Handle a narrow tab row by wrapping/overflowing predictably; never clip Graph or existing tabs.
- Provide a compact List action exposing the current graph results with standard virtualized controls. This is also a keyboard/screen-reader route to every matching item.

### Colour and typography

| Graph role | QP resources / treatment |
|---|---|
| Canvas, folder regions, details | `Bg.Base` / `Bg.Panel` / `Bg.Raised` with subtle `Border.Default` outlines. |
| Main and secondary labels | `Text.Primary` and `Text.Secondary`; existing TASA Orbiter font. |
| Full paths | Existing IBM Plex Mono style, primarily in tooltip/details. |
| Hover | Restrained `Highlight.Text` outline; no layout movement. |
| Selection | `Highlight.Soft` fill with `Highlight.Text` outline; filled action controls use `Highlight` with `On.Highlight`. |
| Keyboard focus | Existing `Signal` focus ring, distinct from hover and selection. |
| Ordinary edges | Theme-appropriate neutral border/text resources, quiet but visible. |
| Selected connections | `Highlight.Text`; use line pattern/hub labels to distinguish sessions and tags. |
| Activity emphasis | Optional small `Heat.*` indicator, separate from selection; do not colour entire clusters by arbitrary rainbow categories. |
| Unavailable resources / errors | A labelled unavailable state; `Danger` only for an actual failed action or error. |

Default cluster identity comes from position, boundaries and labels. Theme colour reinforces state and hierarchy. Preserve the meaning of QP's existing accent, heat, warning and error colours.

Use 8/12/16/24 DIP spacing, existing compact control sizes, and 6–10 DIP corners on controls/panels. Target readable on-screen labels around 12.5–15 DIP with bounded scaling. Hide low-priority labels before they overlap; always prioritise the hovered, focused and selected item. Show the full name/path in details and tooltip.

New `Graph.*` tokens are allowed only where an existing role is insufficient. Add equivalent keys to both palettes and extend palette parity/contrast checks. Do not hardcode application colours inside the reusable graph libraries. Test label contrast on composited cluster/selection fills, not only on the base canvas.

### Motion and detail levels

- Animate focus/expansion for approximately 160–220 ms, with a simple easing curve and no bounce. Honour `SystemParameters.ClientAreaAnimation` and avoid animation when disabled.
- At overview scale show folder headings/counts; at intermediate scale show subfolder regions and file points; at close scale show file labels and type glyphs.
- Thresholds depend on projected screen size and available label space, with hysteresis to prevent flicker. Provide click/keyboard expansion even when automatic zoom expansion is limited.
- Hover and selection appear immediately. Do not animate the whole map continuously or run a simulation while idle.

## 6. Interaction specification

| Input/action | Result |
|---|---|
| Click folder cluster | Focus and expand the cluster, preserving shared filters. |
| Click file | Select it and show details plus its allowed connections. |
| Double-click file / Enter on selected file | Open through QP's existing launch behaviour; one request per gesture. |
| Enter on focused folder | Focus/expand; opening Explorer is a separate named action. |
| Open folder action / context menu | Open the actual folder through QP's shell/launch services. |
| Wheel over graph | Zoom around the pointer with bounded scale. Do not intercept wheel input over calendar, toolbar, details or lists. |
| Drag background / middle-button pan | Pan with a drag threshold and pointer capture; releasing a drag must not open/select a resource. |
| Fit / zoom buttons | Fit current matching content or adjust zoom; Fit leaves filters unchanged. |
| Breadcrumb / Back | Return to an ancestor or earlier graph focus, preserving the calendar selection. |
| Arrow keys in graph | Move focus predictably among visible nodes; Tab reaches toolbar, graph, details and results list. |
| Escape | Close menu/drawer first, then clear selection; do not unexpectedly clear the calendar or search. |
| Ctrl+F | Existing workspace search. Route search Enter/Down deliberately while Graph is active so a hidden grid cannot open an unrelated row. |

Details include display name, path, kind, evidence/source, matching-period open/visit statistics where known, last recorded use, sessions/tags, Open and Open folder. Do not label a session timestamp as a file's last recorded open. Opening Revit resources preserves central/local selection, cancellation and status. Folder opens must not invent dwell time; recorded tracking remains the tracker's responsibility.

Keep camera, expanded branches and selected identity during tab switches within the run. No new persisted workspace fields are required for release one. Switching layouts or disposing the view detaches subscriptions, cancels graph work and stops rendering. Re-entering applies the latest shared query before actions are enabled.

## 7. Architecture and proposed files

Use two small class-library projects to keep layout testable on plain .NET and WPF rendering isolated. Both stay in this solution; neither is published initially.

| Project / proposed files | Responsibility |
|---|---|
| `src/QuickerPlaces.Graph/QuickerPlaces.Graph.csproj` (`net10.0`) | Generic immutable graph records, geometry, deterministic hierarchy layout, visible-set projection, label priorities and spatial index. No WPF or QP store dependency. |
| `src/QuickerPlaces.Graph/GraphSnapshot.cs`, `GraphLayoutEngine.cs`, `GraphVisibility.cs`, `GraphSpatialIndex.cs` | Node/edge identity, bounds, expansion, collision/query operations and cancellation points. Use UI-free geometry records, not `System.Windows.Point`/`Rect`. |
| `src/QuickerPlaces.Graph.Wpf/QuickerPlaces.Graph.Wpf.csproj` (`net10.0-windows`, `UseWPF`) | References Graph; reusable rendering/interaction host. No reference to the QP app. |
| `src/QuickerPlaces.Graph.Wpf/GraphSurface.cs`, `GraphTheme.cs`, `GraphAutomationPeer.cs` | Drawing, camera transforms, hit testing, dependency-property theme inputs and accessibility. Raises selection/focus/open requests; never launches a process. |
| `src/QuickerPlaces/Services/Graph/QpGraphProjection.cs`, `GraphHistoryIndex.cs` | Translate accepted QP query/snapshot evidence into graph records; source deduplication, archive indexing and coverage. |
| `src/QuickerPlaces/ViewModels/GraphViewModel.cs` | Query revision, background work, camera/focus state, loading/coverage, details and action routing. UI-free where practical, following current test-link conventions. |
| `src/QuickerPlaces/Views/Panels/GraphPanel.xaml` and `.xaml.cs` | QP toolbar, surface bindings, details/list hosts, standard menus and focus bridging. |

The application references Graph and Graph.Wpf; Graph.Wpf references Graph. Existing plain `net10.0` tests may reference Graph and continue linking the app's UI-free adapter/view-model sources. Do not make that test project reference the WPF app or renderer. Any necessary WPF-specific automation checks belong in a separate Windows-only harness/project.

### Shared query handoff

Introduce a narrow immutable query-completion contract at the existing accepted-result boundary in `LibraryViewModel.Refresh`, or a small shared coordinator extracted from that boundary. Include revision, data revision, effective filters, resolved period, projection evidence and coverage. `QueryChanged` alone is insufficient: `ApplyQuery`, store refresh and rollover can change results without a user edit event.

Publish only accepted generations. Graph work uses the same shared query state and existing filtering/identity helpers; it does not scrape visible rows or rerun independent calendar rules. History indexing can enrich graph-only All-time membership without changing the All table. Integrate Graph's filesystem projection without replacing the calendar's existing source coverage with a file count.

Add `LibraryTab.Graph` with All-source mapping, graph-specific visibility and explicit keyboard routing. Audit enum switches and tab-dependent booleans rather than assuming the new enum's default branch is enough. Preserve session scope clearing, Saved places calendar behaviour and hidden-panel semantics.

### History loading

1. Show an initial map from current stores while the history index builds in background.
2. Enumerate history metadata using existing readers. For All time, process months incrementally into compact per-resource/per-month summaries; release full monthly documents after processing.
3. For a selected period, load only the months intersecting it plus the calendar's own requirements; preserve `HistoryCutoffs` and cross-machine merge/deduplication rules.
4. Invalidate affected index entries on live store updates, history reload/change and local-day rollover. Never reuse a month cache that can evict the calendar's required months unexpectedly; use a separate bounded reader/cache or coordinated ownership.
5. Keep progress and unavailable/partial months visible. Do not present incomplete results as a final zero. Reader APIs that currently collapse unreadable and absent months into the same outcome need a narrow diagnostic outcome extension.
6. The first release may use an in-memory compact index; no duplicate authoritative activity store. Add a rebuildable on-disk cache only if measured startup/archive cost justifies it.

Store capture must honour current thread affinity. Copy mutable place fields needed by graph background work into immutable records; an existing `LibrarySnapshot` shares `Place` instances and is not by itself a deep immutable graph snapshot.

### Rendering and resources

Use a `FrameworkElement` host with layered `DrawingVisual` content for regions/edges, nodes/labels, and transient selection. Keep ordinary WPF controls for toolbar, menus, details and virtualized results. Clip to the viewport and draw only visible detail. Maintain a spatial index for hit testing/culling; convert pointer coordinates through the inverse camera transform and use screen-space hit tolerances.

Pass theme brushes/fonts through dependency properties bound with `DynamicResource` in the QP view. A palette swap replaces brushes, so caching `FindResource` once or relying on old brush references is insufficient. Property changes invalidate the relevant drawings and cached text. Freeze owned derived resources where safe, but never freeze/mutate shared dynamic inputs. DPI changes refresh text measurement and device alignment.

Maintain an accessible host name, announced counts/selection and automation peers for the realised interactive set. The virtualized List route exposes every matching resource by standard keyboard/automation semantics. Avoid creating an invisible WPF control for every graph node. Attach frame callbacks only during active motion and detach on completion/unload.

## 8. Delivery milestones

Each milestone leaves a buildable increment. Use focused review, meaningful tests and a visual pass as appropriate; do not mark a live Windows check passed unless performed. Record results and limitations in the plan/BUILD_SUMMARY as implementation lands.

| Milestone | Work and exit condition |
|---|---|
| M0 — contracts and visual specimen | Capture existing tab/calendar/search behaviour, write membership fixtures, document the graph design extension and create a representative WPF specimen in both themes. Confirm usable composition with the calendar at normal and narrow widths. |
| M1 — graph model and layout | Add Graph library and pure projection/layout logic. Deterministic geometry, distinct counts, structural ancestors, UNC/overlapping-root/Revit identity cases and stable filtering pass focused tests. |
| M2 — shared query and history | Add accepted-query handoff, cancellable graph work and incremental archive summaries. Period membership parity, no-period archive expansion, cutoff deduplication, coverage and stale-result handling pass tests. Existing tabs/calendar retain behaviour. |
| M3 — WPF surface and exploration | Add Graph.Wpf, pan/zoom, detail levels, spatial index, label collision handling, theme inputs and keyboard focus. Inspect the actual specimen at overview/folder/file scales before integrating the tab. |
| M4 — QP tab and actions | Wire GraphPanel into FilesPanel, shared search/calendar, details/list, opening routes, Revit flow, lifecycle and session/tag connections. Verify a complete explore/filter/open journey and tab-switch parity. |
| M5 — polish and performance | Tune spacing, edge density, transitions, empty/loading states, narrow layouts, accessible navigation and DPI. Run benchmark matrix and fix measured bottlenecks. |
| M6 — release verification and documentation | Run solution build/tests, perform the Windows checklist below, update USERGUIDE/README/BUILD_SUMMARY and record remaining limitations. Feature is done when required behaviour and visual acceptance are demonstrated. |

## 9. Verification and performance gates

### Behaviour tests

- The same file from recent, history, saved and session sources is one node; file counts remain distinct across overlapping roots and sessions.
- Inclusive ranges, local midnight/DST, culture-specific weeks, relative-rule rollover, cross-year ranges and periods predating tracking match current calendar rules.
- Calendar heat remains across the displayed year; graph selection/focus/connection changes do not alter the query or heat.
- Period-filtered membership matches All's filesystem membership on the same evidence; structural ancestors are distinguishable. All-time Graph additionally includes archive-only resources.
- Historical/live overlap and multi-machine records preserve existing merge semantics; missing detail is unknown, not zero. Session events never become fabricated file opens.
- Old query/history/layout results cannot replace a new selection; failure, cancellation, reload and view disposal leave consistent state.
- Deterministic layout, finite geometry, nonoverlapping sibling bounds, camera transforms, zoom anchoring, boundary hit tests and label priority work on adversarial long/deep/flat trees.
- Launch requests use the intended identity and existing services once; missing paths and Revit cancellation report the real outcome.

### Measured workload matrix

Use deterministic synthetic data plus an anonymised representative QP dataset where available. Include deep trees, one folder with many files, long paths, UNC paths, many sessions/tags, and multi-year archives. Report total nodes, visible nodes/edges, machine/DPI/build, repeated timings and memory deltas.

| Workload | Initial target |
|---|---|
| 1,000 / 10,000 / 100,000 known files | All are reachable; visible work remains bounded through aggregation/culling. No silent dropping or hard truncation. |
| Warm filter change at 10,000 files | Accepted graph update within 200 ms at p95, excluding disk reads; controls remain responsive. |
| Pan/zoom | Aim for 60 Hz with bounded detail; p95 frame interval at or below 33 ms on the recorded reference PC. Reduce label/edge density before missing this budget. |
| Opening Graph with archive I/O | Show loading feedback immediately and target a usable current-store overview within 500 ms; archive progress remains cancellable. |
| Rapid calendar changes while indexing | Latest selection wins, with no visible UI-thread stalls of 100 ms or more attributable to graph work. |
| Repeated open/filter/switch/close cycles | No continuing frame loop while hidden/idle; retained memory stabilises after caches warm and work is released. |

These are starting gates. If the reference workload cannot meet one, record measurements and the design adjustment before calling performance complete. Do not advertise a maximum supported dataset without evidence.

### Windows visual and interaction checklist

- [ ] Dark and Light: overview, nested folder, selected file, connections, loading, empty and failed-open states look finished.
- [ ] Every highlight preset, Windows accent and Match Windows update the open graph live, including cached drawings/labels; no stale colours remain.
- [ ] Text/selection contrast, discernible cluster/edge boundaries, focus indicators and non-colour source distinctions are verified.
- [ ] Calendar day/week/month/range and Clear period immediately govern the graph; older archive periods and partial coverage are exercised.
- [ ] Zoom expansion/collapse is smooth, labels do not flicker/overlap, and changing dates does not rearrange surviving clusters.
- [ ] Deep and very broad folders remain discoverable through expansion/list/search; incomplete and aggregate counts are labelled accurately.
- [ ] Shared search Enter/Down, Ctrl+F, Tab, arrows, Enter, Escape, context menus, Fit and Back act on visible graph state.
- [ ] Narrator/automation can reach every result through List and announce graph selection; keyboard focus is visible.
- [ ] Open file, Open folder, missing paths, UNC availability and applicable Revit central/local flows preserve existing behaviour.
- [ ] Existing Saved/Recent/Sessions/All tabs, session scoping and calendar heat retain behaviour after switching through Graph.
- [ ] Minimum supported window size, collapsed workspace column, resized panels and 100/150/200% DPI are readable; switching monitors updates rendering.
- [ ] Reduced animation, app resume/day rollover, hide/unload and repeated tab/layout switches behave correctly and stop unnecessary work.

## 10. Definition of done

Graph is complete when a user can select a calendar period, recognise meaningful folder clusters, zoom or click to find a known file, understand its recorded/session provenance and connections, and open it or its folder without losing context. Both themes and every highlight choice must feel native to QP. Build/tests, measured workloads and the required visual/manual checks have recorded outcomes; incomplete coverage and remaining limitations are stated plainly.

Implementation references: [WPF DrawingVisual](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/using-drawingvisual-objects), [visual-layer hit testing](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/hit-testing-in-the-visual-layer), and the existing [File viewer design](260929_File%20Viewer%20Design.md), [activity-history plan](261006_Activity%20History%20Plan.md) and [design-system export](../design/design-system-export/QuickerPlaces-Design-System.md).

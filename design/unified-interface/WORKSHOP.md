# QuickerPlaces: one interface, three directions

Design exploration against main at `6ba577d`, including the Sessions, Recent Files and Library additions pulled during this workshop. These are interactive proposals, not production changes. The previews use sample content and simulated actions, not the user's live stores.

## Recommendation

Use **Library workspace** as the main application. It gives the existing Library a clear role as the shared catalogue, keeps Places and Sessions recognizable, and brings their actions into the same window. Later, offer the **Expanding launcher** as the hotkey presentation of that same shell. Choose **Session desk** instead if assembling and reopening sets of documents is the dominant daily task.

The important change is shared context, not simply putting four window titles in a tab bar. A selected Library item should explain its origin and offer the next useful action: open its saved place, inspect its folder activity, navigate to a containing session, or deliberately add the document to a session.

## 1. Library workspace

**Organizing principle:** one catalogue, several focused views.

A persistent left rail contains Library, Places, Sessions and Activity. Favourite Places remain available above every view. The main region changes in place; the detail panel follows the selected item. Settings, capture and edit forms open as panels inside the shell.

- Library is the default view. It retains the current kind filters, Saved/Recent distinction and source attribution.
- Places becomes a focused saved-folder-and-link view with its existing open counts, stars and recovery behaviour.
- Sessions gets the existing session list, tag search and file details inside the shell.
- Activity retains the detailed folder metrics and period controls; its calendar is secondary to finding and acting on folders.
- The detail panel makes relationships visible. A PDF can belong to multiple sessions and have recorded Recent Files activity without appearing as several unrelated copies.

**Try:** select the floor-plan PDF, follow its session button, reopen the files, then return to Library. Save Dynamo Revit and tick “Pin to favourites.” Capture a new session and observe its name and tags appear in the Library.

**Strength:** clearest overall information architecture; builds directly on `LibraryIndex`.

**Tradeoff:** more permanent chrome than today's launcher. At the current 700px minimum width, the inspector needs to become a lower panel or an optional drawer; it should not squeeze the list into unusable columns.

**Implementation effort:** medium relative to these three directions. Extract reusable view content from the four Windows and introduce navigation/selection state. Existing stores and launchers can stay intact.

## 2. Session desk

**Organizing principle:** choose a session, resume or assemble its working set.

Sessions occupy the left side. The selected session's PDF, Word and Excel files occupy the right, with an explicit “Open all” action. A small Library section below offers other files to add deliberately. Places and Activity remain accessible in the same shell; favourite folders and links remain visible.

- The session is the working context, not a new inferred project entity.
- “Add from Library” avoids repeatedly opening an editor, invoking a file picker and finding a known file again.
- The file's containing folder, session tags and recorded history remain distinguishable.
- Existing session capture remains a review step: detected-open files checked, recent candidates unchecked.

**Try:** add Concrete specification.pdf to Tower B, browse Library and inspect its updated session membership. Select Coordination review and reopen it to see the missing-file report.

**Strength:** fastest for jobs that revolve around drawings, specifications and spreadsheets used together.

**Tradeoff:** makes a simple “open this folder” task feel more like a document workspace. A session is currently a named set of file paths; it does not restore viewer tabs, page numbers, window positions or an entire project environment.

**Implementation effort:** medium–high. Reuse SessionsViewModel and the editor; add an explicit Library-to-session command and shared refresh notifications. Do not automatically assign folders or links to sessions: the current session model supports document files only.

## 3. Expanding launcher

**Organizing principle:** find and open first; expand when you need to organize.

The hotkey brings up favourites and a unified search. Results distinguish sessions from individual Library items. Sessions have an explicit “Open N” action so a broad search cannot accidentally launch a whole set. Expanding reveals Library, Places, Sessions and Activity in the same window.

- The compact state is optimized for returning to work.
- Search covers names, destinations, session names and tags; results retain their source labels.
- Capture and edit panels live inside the expanded shell, retaining the search context on return.
- Expansion keeps advanced filters, history and settings out of the quick-launch path while remaining discoverable.

**Try:** search “RFI,” open an individual document or its session, then expand to inspect Library sources and activity. Use Settings to compare Light and Dark appearances.

**Strength:** closest to the original lightweight QuickerPlaces purpose.

**Tradeoff:** requires careful focus, result ranking and compact/expanded state management. Users may miss tools hidden behind expansion; expansion must be clearly labeled and remember the user's preference.

**Implementation effort:** highest if implemented first. It needs mixed-result keyboard navigation, distinct session and individual-item launch semantics, window-size restoration and consistent focus handling. It is a good second-stage presentation of option 1.

## Existing behaviour versus proposed additions

**Already implemented in main:** deduplicated Library items; folder/link/PDF/Word/Excel filters; Saved/Recent filters; session tags; session capture and editing; batch reopen with missing-file handling; opt-in Recent Files; folder visits and active time; place favourites; import/export of places; Recently Deleted for places.

**Proposed:** one application shell, a contextual inspector, inline drawers, direct Library-to-session navigation, an explicit Add to session command from Library, pinning while saving a recent folder, and a unified compact search with session results.

The preview's content, paths, counts and heat intensity are illustrative. Period controls demonstrate changing scope, not calculated activity from the real application. The heatmap is an illustrative collapsed overview. Production must preserve the current calendar's year selection, day filtering, keyboard navigation and correct history aggregation. Preview grouping and density are for layout review; they are not a replacement for the production collection views.

## Rules to preserve across every direction

1. Keep the stores separate. Library is a projection over Places, Sessions, Folder Activity and Recent Files, not a fifth editable copy of the same records.
2. Count a saved-place launch only when the PlaceLauncher path succeeds. Opening a session must not increase Place opens. Distinguish Explorer visits/time, recorded file opens and session use in labels and heatmap legends.
3. Preserve explicit capture review. Never silently treat every recent file as currently open or add it to a session without selection.
4. Keep folder tracking and Recent Files as separate opt-ins. Surface both statuses. Session capture must not silently enable either.
5. Preserve recovery semantics. A removed place has Undo and seven-day recovery; deleting a session is a different action with confirmation and leaves source files intact.
6. Scope export clearly to Places. Do not imply sessions or recorded history are included.
7. Keep existing keyboard behaviour and distinguish selection from launching. Global hotkey targets search; in mixed results, Enter should follow the visibly selected result, with explicit session launch affordances.
8. Retain honest save-error reporting for each store. An embedded panel must not close as though data were persisted when a save failed.

## Visual grounding

The checked-in `Images/main ui.png` predates the current refresh. The preview therefore follows the current source: TASA Orbiter, IBM Plex Mono, the Hull green action/selection colours, leather favourites and session tags, dark/light palettes, 6–8px control corners, thin stroke icons, the QP monogram and racing stripe. The fonts are bundled from the application's own font files. Browser icons approximate the corresponding WPF stroke icons.

Source anchors:

- `src/QuickerPlaces/Views/MainWindow.xaml`
- `src/QuickerPlaces/Views/LibraryWindow.xaml`
- `src/QuickerPlaces/Views/SessionsWindow.xaml`
- `src/QuickerPlaces/Views/SessionEditorDialog.xaml`
- `src/QuickerPlaces/Views/ActivityWindow.xaml`
- `src/QuickerPlaces/Services/Library/LibraryIndex.cs`
- `src/QuickerPlaces/ViewModels/LibraryViewModel.cs`
- `src/QuickerPlaces/ViewModels/SessionsViewModel.cs`
- `src/QuickerPlaces/Resources/Styles.xaml`
- `src/QuickerPlaces/Resources/Palette.Dark.xaml` and `Palette.Light.xaml`
- `USERGUIDE.md`, Project Sessions / Recent Files / The Library

## Implementation sequence if option 1 is chosen

1. Extract each main Window's content into a reusable view. Keep existing view models, stores and modal safety prompts. Rework owner-specific callbacks, activation refresh, timers and disposal explicitly: moving content into a shell changes those lifecycles.
2. Add shell navigation, selected-item identity and per-view search/filter state. Preserve each view's position when navigating back.
3. Route item details and launch commands by source and type. Connect session updates and place updates to the existing Library reload mechanism.
4. Embed capture/edit/settings panels and add the explicit cross-tool commands. Preserve validation, review, unsaved-error reporting, keyboard focus and cancellation behaviour.
5. Validate the whole journey: recent folder → saved place → favourite; detected documents → reviewed session → reopen; Library document → containing session; missing file → partial reopen report; remove place → undo/restore.
6. Add the compact launcher presentation only after the shared shell interactions are stable.

## Preview verification

Browser-checked all three layouts at desktop width and at a narrow 360px browser viewport. Fixed an inspector wrapping issue; the final narrow check showed no horizontal overflow in any variant, including the expanded launcher. JavaScript syntax checking passed and the final browser check reported no console errors.

Exercised session capture and the resulting Library source update; recent-folder saving with favourite pinning; adding a Library document to a session; batch reopen with a missing-file report; search across session metadata and Library items; workspace expansion; and changing appearance. These validate the prototype interactions only. No production application code was changed or application test suite run.

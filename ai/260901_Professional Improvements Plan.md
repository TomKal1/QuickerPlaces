# QuickerPlaces — Professional Improvements Implementation Plan

**Status:** planned for a later release  
**Created:** 2026-09-01  
**Scope:** improve reliability, recovery, retrieval, and distribution without turning QuickerPlaces into a general-purpose file manager

## 1. Product direction

QuickerPlaces should remain a small, deliberate launcher for remembered destinations. The next release should make it safer and more useful as a daily professional tool while keeping the main window simple.

The planned release adds:

- Reliable, visible persistence failure handling
- Protection and recovery for corrupt data files
- A seven-day Recently Deleted area with immediate Undo
- Last-opened and open-count usage information
- Support for files, including PDFs, alongside folders and URLs
- Explicit multi-folder import with a review step
- Simple search across aliases, types, and destinations
- Remembered grid sorting
- A self-contained, single-file Windows release

## 2. Explicit non-goals

The following are intentionally excluded:

- Drag-and-drop folder or file creation. It is too easy to miss as a feature and can make accidental drops ambiguous.
- Reading Explorer's undocumented Quick Access or `AutomaticDestinations` storage.
- Automatic favourites or automatic reordering based on usage.
- Tags, categories, workspaces, cloud sync, or accounts.
- File previews, PDF rendering, or document editing.
- Complex usage analytics.
- Retargeting the application to classic .NET Framework.
- CI as a prerequisite for the feature release. Focused automated tests come first; CI may be added later as a small follow-up.

## 3. Technical direction

- Keep WPF on .NET 10 LTS.
- Continue using `System.Text.Json` and the existing lightweight MVVM structure.
- Introduce abstractions only where they enable reliable persistence or focused tests.
- Preserve compatibility with existing `places.json` and `settings.json` files through explicit schema migration.
- Prefer deliberate, visible commands and review dialogs over hidden interactions.
- Keep all user data local.

## 4. Recommended implementation order

### Phase 1 — Persistence reliability and recovery

This phase must precede features that add more stored state.

#### 4.1 Make saves transactional and observable

Current mutations update memory before `SaveToDisk` silently ignores write failures. Replace this with an explicit result-based persistence flow.

Requirements:

- A mutation is reported as successful only after its new state is safely written.
- If a save fails, restore the previous in-memory state or keep the proposed state clearly marked as unsaved.
- Show a persistent, non-modal error in the main window with at least **Retry** and **Show Data Folder** actions.
- Do not dismiss the error merely because another command was attempted.
- Preserve the previous valid data file until the replacement succeeds.
- Use a unique temporary file name so stale files or another process cannot collide with it.
- Log or retain enough error detail for troubleshooting without exposing a raw exception as the only user message.

#### 4.2 Protect corrupt files

Requirements:

- If `places.json` cannot be read, do not allow the first new mutation to overwrite it silently.
- Preserve the damaged file with a timestamped name such as `places.corrupt-20260901-143000.json` before a fresh store is created.
- Offer a clear recovery choice: reveal the file, continue with a new empty store, or exit.
- Never label data as safely saved while recovery is unresolved.

#### 4.3 Prevent concurrent writers

QuickerPlaces should be a single-instance desktop application.

Requirements:

- Use a named mutex or equivalent single-instance mechanism.
- A second launch should activate the existing window and then exit.
- Verify that the mechanism works when the first instance is minimized or behind another window.

#### 4.4 Make storage testable

- Allow the places service to receive a storage location or small storage abstraction.
- Production continues to use the existing AppData locations.
- Tests use isolated temporary directories and never touch real user data.

### Phase 2 — Seven-day Recently Deleted

#### 4.5 Data model

Add an optional `DeletedAt` timestamp to a place. Use `DateTimeOffset` in UTC for new persisted timestamps.

- `DeletedAt == null`: active place
- `DeletedAt != null`: recently deleted place

Increment the places schema version and migrate existing records with `DeletedAt = null`.

#### 4.6 Removal workflow

- Rename the destructive action from **Remove** to **Move to Recently Deleted** or retain **Remove** with explanatory confirmation text.
- Removing a place immediately removes it from the grid and favourites.
- Show a short-lived confirmation with an **Undo** action.
- Undo restores the exact place, including favourite status and ordering where possible.

#### 4.7 Recently Deleted dialog

Add a small dialog accessible from the main window's secondary menu.

It should show:

- Alias
- Type
- Path/URL
- Deleted date
- Days remaining

Actions:

- Restore selected
- Delete selected permanently
- Empty Recently Deleted

Permanent deletion must require confirmation.

#### 4.8 Expiration and conflicts

- Purge records after seven full days, preferably on successful startup and before a normal save.
- Never purge from a store that failed to load or migrate correctly.
- Deleted records do not block aliases or resources used by active records.
- If restore conflicts with a newer active record, do not silently skip it. Explain the conflict and let the user edit the alias or destination before restoring.

### Phase 3 — Usage tracking and sorting

#### 4.9 Data model

Add:

- `DateTimeOffset? LastOpenedAt`
- `int OpenCount`, defaulting to zero

Migrate existing records with null/zero defaults.

#### 4.10 Recording an open

- Increment usage only after Windows accepts the shell launch request.
- A failed or cancelled open does not change the statistics.
- Statistics describe launches initiated through QuickerPlaces, not all Windows activity for that destination.
- Persist the usage update immediately through the reliable save path.
- Do not automatically change favourite state or favourite order.

#### 4.11 Grid presentation

- Add sortable **Last Opened** and **Opens** columns.
- Replace **Date Added** with **Last Opened** in the default visible layout.
- Keep Date Added available if optional column visibility is implemented; otherwise omit it from the default grid without deleting the stored value.
- Never-opened destinations should sort consistently and display an em dash or `Never`.

#### 4.12 Remember sorting

Persist the active sort column and direction in `settings.json`.

Supported sort fields should include at least:

- Alias
- Type
- Path/URL
- Last Opened
- Opens
- Date Added, if visible

Path/URL sorting already works through the DataGrid; this work makes the selected sort survive restarts.

### Phase 4 — General file support

#### 4.13 Place type

Extend `PlaceType` with `File`. Update labels from **Path / URL** to **Destination** where that produces clearer UI.

Files include PDFs, Office documents, images, text files, and other user-selected documents. QuickerPlaces launches them through the Windows default application; it does not inspect or render them.

#### 4.14 Add workflow

Replace the two prominent add buttons with one **Add...** control that offers:

- Folder
- File
- URL

The file option uses the native Windows open-file dialog and defaults the alias to the filename without its extension. The user can edit the alias before saving.

#### 4.15 Validation and opening

- Folder destinations must be fully qualified paths.
- File destinations must be fully qualified file paths.
- URL destinations should accept only explicitly supported schemes, initially `http` and `https`.
- A missing file or folder produces a clear message and does not increment usage.
- Imports must validate enum values and destination formats before presenting candidates.
- Imported JSON must not be able to disguise an executable or custom protocol as a URL.

#### 4.16 Import/export compatibility

- Export includes the new type and schema version.
- Existing Folder/URL exports continue to import.
- Decide explicitly whether personal backup imports retain usage statistics. Recommended default: preserve metadata for a QuickerPlaces backup, but reset usage when importing a share-oriented subset only if the product later distinguishes those operations.

For this release, preserve exported metadata to keep export/import round trips lossless.

### Phase 5 — Explicit multi-folder import

Add an **Import Folders...** command. This is separate from JSON import and does not attempt to read Explorer's internal pin database.

#### 4.17 Selection

- Use the native `Microsoft.Win32.OpenFolderDialog` with multiselect enabled.
- Nothing is added immediately after selection.
- If practical, initialize the dialog at the user's last folder-import location.

#### 4.18 Review dialog

Show every selected folder before committing it.

Each row contains:

- Include checkbox
- Editable alias, defaulted from the folder name
- Full path
- Validation/conflict status

Requirements:

- Duplicate aliases and paths are clearly marked.
- Users can fix an alias directly in the review dialog.
- Invalid or conflicting rows cannot be imported until corrected or unchecked.
- The final message reports imported, skipped, and failed counts accurately.
- Commit all selected valid rows through one persistence operation where possible.

This provides a deliberate way to bring across folders currently visible in Explorer or Quick Access without depending on undocumented Windows data.

### Phase 6 — Search and retrieval polish

#### 4.19 Search

Add one compact search field above the grid.

- Filter as the user types.
- Match alias, destination, and type label case-insensitively.
- Do not add query syntax, tags, indexing, or search history.
- Clear the search with Escape or a visible clear button.
- Start each launch unfiltered; do not persist search text.
- Search affects the main grid only, not the favourite bubbles.

#### 4.20 Small professional refinements

- Add **Copy Destination** to row and bubble context menus.
- Show the full destination in a tooltip when grid text is truncated.
- Add sensible keyboard access keys and shortcuts for Add, Open, Search, Rename, Favourite, and Remove.
- Ensure every mouse-only action has a keyboard-accessible alternative.

### Phase 7 — Distribution

Keep the application on .NET 10 LTS and add repeatable publish profiles.

#### 4.21 Recommended release artifact

Publish a Windows x64 artifact that is:

- Release configuration
- Self-contained
- Single-file
- Unpacked/portable

The result should be a single `QuickerPlaces.exe` that runs without a separately installed .NET runtime.

Also consider an optional smaller framework-dependent x64 download for users who already have the .NET 10 Desktop Runtime.

#### 4.22 Verification

- Test the published executable on a clean supported Windows environment without the .NET 10 runtime installed.
- Verify icon, startup time, AppData paths, import/export dialogs, and shell launching.
- Do not enable trimming unless the complete WPF application is tested for XAML, reflection, serialization, and resource regressions.
- Consider code signing before broad public distribution; changing to .NET Framework would not eliminate Windows reputation warnings for an unsigned download.

## 5. Automated test plan

Tests should be added alongside the relevant phases. CI can be introduced later once the tests provide useful coverage.

Minimum service-level coverage:

- Successful atomic save and reload
- Save failure does not falsely report success
- Previous valid file survives a failed replacement
- Corrupt-file recovery preserves the original bytes
- Existing schema migration
- Unsupported future schema handling
- Alias and resource duplicate rules
- Fully qualified folder/file validation
- Allowed and rejected URL schemes
- Invalid enum values in imported JSON
- Favourite ordering and restoration
- Soft deletion, undo, restore conflicts, and seven-day expiry
- Open-count and last-opened updates
- Failed opens do not update usage
- Multiple incoming folder collisions within the same import batch
- Lossless export/import of the current schema
- Single-instance coordination where it can be tested reliably

Manual WPF verification:

- Keyboard-only completion of every main workflow
- Sorting and sort restoration
- Search filtering and clearing
- Recently Deleted dialog behavior
- Multi-folder review and validation
- Missing/offline file and folder messages
- High-DPI and multi-monitor window restoration
- Published single-file execution on a clean machine

## 6. Documentation updates

When implementation begins:

- Update the README status and correct references to the actual SI filename.
- Update the user guide for Recently Deleted, files, usage columns, search, and folder import.
- Document the exact meaning of Open Count: launches initiated by QuickerPlaces.
- Document the seven-day retention rule and restore-conflict behavior.
- Document the portable self-contained release and optional smaller runtime-dependent release.
- Update `BUILD_SUMMARY.md`; it currently states that the project was not compiler-verified, which is no longer true.

## 7. Definition of done

The planned release is complete when:

- No successful-looking mutation can be silently lost because a save failed.
- A corrupt store cannot be overwritten without being preserved.
- Only one application instance can write the store.
- Deleted places are recoverable for seven days.
- Usage statistics are accurate for successful QuickerPlaces launches and sortable.
- Folders, files, and HTTP(S) URLs can be added and opened safely.
- Users can deliberately import multiple folders through a visible review flow.
- The grid can be searched and remembers its sort.
- Existing user data migrates without loss.
- Core persistence and migration behavior has automated coverage.
- A self-contained single-file Windows x64 build has been verified on a clean environment.


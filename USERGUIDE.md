# QuickerPlaces — User Manual

QuickerPlaces is a small always-on-top-of-your-workflow window for storing folder paths and URLs under a short, memorable name (an **alias**), so you can get back to them in one click instead of digging through File Explorer or your bookmarks. This guide covers everything you can do in the app itself. For what's under the hood, see `ai/BUILD_SUMMARY.md`.

## The window, at a glance

When QuickerPlaces opens you'll see, top to bottom:

- A header with the app name and six buttons: a gear icon for **Settings**, a folder icon that opens your data folder, **Import...**, **Export...**, **Add Folder**, **Add URL**.
- A row of **favourite bubbles** — your pinned places, one click away. Empty at first, with a hint telling you how to add one.
- The **All Places** header with a count, a **search box**, and a **Hide List** / **Show List** button that collapses or restores everything below it.
- The **All Places** grid — every place you've saved, one row each, each with a folder or globe icon showing its type.

Nothing here needs a save button. Every add, edit, favourite toggle, reorder, or removal is written to disk the moment it happens.

## Adding a place

Click **Add Folder** or **Add URL** in the header. Either opens the same small dialog:

1. **Alias** — the name you'll use to recognize this place. Must be unique; "Docs" and "docs" count as the same alias, so you'll be blocked (with a clear message) if you try to reuse one.
2. **Path or URL** — for a folder, either type the path or use the **Browse...** button to pick it; for a URL, type it in (e.g. `https://wiki.example.com`). This also has to be unique — you can't save the same path or URL twice. Note that this check is exact: `C:\Projects` and `C:\Projects\` are treated as different values, as are `http://` and `https://` versions of the same site, so use whichever form you actually want to keep.

Click **Save**, and the new place appears immediately at the bottom of the grid.

If you enter a folder path, QuickerPlaces checks that it's a syntactically valid, *full* path. It has to start from a drive (`C:\Projects`) or a network share (`\\server\share`); a relative path like `Projects` is rejected. It does not require the folder to already exist on disk, so you can save a place for a folder you're about to create. A URL is checked for being well-formed but is never contacted or pinged when you save it.

## Working with a place in the grid

Every row in the **All Places** grid shows the Alias, Type (Folder or URL), the Path/URL, whether it's a Favourite, and the date it was added.

- **Double-click a row** to open it — a folder opens in File Explorer, a URL opens in your default browser.
- **Right-click a row** for the full menu:
  - **Open** — same as double-click.
  - **Copy Path/URL** — puts the folder path or URL on the clipboard, ready to paste into another app.
  - **Rename Alias** — change just the name; the same uniqueness check from adding applies.
  - **Edit Path/URL** — change just the destination; the same duplicate check applies.
  - **Toggle Favourite** — pin it to (or unpin it from) the bubble row above the grid.
  - **Remove** — deletes it, after asking. A bar under the list confirms it with an **Undo** button, and **Ctrl+Z** does the same. Undo puts the place back exactly where it was, including its spot among your favourites. You can undo several removals in a row, most recent first, for as long as QuickerPlaces stays open. If you've since reused that alias or path/URL for another place, undo explains why it can't restore it.

If a place can no longer be opened — the folder's been deleted, or the URL is malformed — you'll get a clear message instead of the app crashing or silently doing nothing.

## Searching

Type in the **Search places** box above the grid (or press **Ctrl+F** to jump there) and the grid narrows as you type. A place matches when every word you type appears somewhere in its alias or its path/URL, ignoring case. So `wiki prod` finds an alias "Prod Wiki", and it also finds an alias "Wiki" that points at `https://prod.example.com`. The header shows how many places match, e.g. **All Places (3 of 12)**.

The search box works as a quick launcher:

- **Enter** opens the top result.
- **Down arrow** moves into the grid so you can pick a different row (then **Enter** opens it).
- **Esc** clears the search. Pressing it again with the box already empty moves you into the grid.

The ✕ button beside the box also clears it. Searching only filters the grid; your favourite bubbles always stay visible. If the list is hidden, typing a search brings it back. If you add or import a place that the current search would hide, the search is cleared so the new place is visible.

## Favourites

Any place can be a favourite. Toggling **Favourite** (from the grid's right-click menu, or from a bubble's own right-click menu) adds or removes it from the bubble row above the grid.

- **Click a bubble** to open that place — identical to double-clicking its row.
- **Drag a bubble** left or right to reorder the row. The order you leave them in is remembered.
- **Right-click a bubble** for a shortcut menu: **Open**, **Copy Path/URL**, or **Remove from Favourites** — you don't need to go back to the grid just to unpin something.
- **Hover over a bubble** to see where it points.
- **Ctrl+1** to **Ctrl+9** open the first nine bubbles, in their left-to-right order.

## Bringing QuickerPlaces up from anywhere

While QuickerPlaces is running, press **Ctrl+Alt+Space** in any app. QuickerPlaces comes to the front (restoring it if it was minimized) with the search box focused, so you can type part of a name and press **Enter** to open it. The subtitle under the app name shows the shortcut when it's active.

Starting QuickerPlaces again while it's already running does the same thing. You only ever get one copy, which also keeps two copies from overwriting each other's saved places.

**Changing the shortcut:** click the gear icon at the top of the window to open **Settings**. Click the shortcut box and press the keys you want, for example Ctrl+Alt+Q. It needs at least one of Ctrl, Alt or Win, plus one other key. **Reset to Default** goes back to Ctrl+Alt+Space, and **Turn Off** (or Backspace in the box) disables it. Click **Save** and the new shortcut works straight away. If another app already uses that combination, Settings tells you and stays open so you can pick another.

If the saved shortcut can't be used when QuickerPlaces starts (another app has taken it since), you'll get a message saying so, and everything else works normally. Pick a new one in Settings.

The shortcut is stored as `"globalHotkey"` in `%LocalAppData%\QuickerPlaces\QuickerPlaces\settings.json`, so you can also edit it there while QuickerPlaces is closed.

## Hiding the grid

If you only want the favourite bubbles visible, click **Hide List**. The grid collapses and the window shrinks to make room; click **Show List** to bring it back. This state is remembered between sessions, along with the window's size and position.

## Keyboard shortcuts

| Keys | What it does |
|---|---|
| **Ctrl+Alt+Space** (from any app) | Bring QuickerPlaces to the front, ready to search |
| **Ctrl+F** | Jump to the search box |
| **Ctrl+N** | Add Folder |
| **Ctrl+U** | Add URL |
| **Ctrl+H** | Hide / show the list |
| **Ctrl+Z** | Undo the last Remove (repeat to undo earlier ones) |
| **Ctrl+1** … **Ctrl+9** | Open favourite bubble 1–9 |
| **Enter** (in search box) | Open the top result |
| **Down** (in search box) | Move into the grid |
| **Esc** (in search box) | Clear the search, or move into the grid if it's already empty |

These act on the selected grid row, when the grid has keyboard focus:

| Keys | What it does |
|---|---|
| **Enter** | Open |
| **F2** | Rename Alias |
| **Ctrl+E** | Edit Path/URL |
| **Ctrl+D** | Toggle Favourite |
| **Ctrl+C** | Copy Path/URL |
| **Delete** | Remove (asks first) |

## Exporting places

Click **Export...** to open a checklist of every place you've saved, all checked by default. Uncheck anything you don't want to include, then choose where to save the resulting `.json` file. Saving over an earlier export is safe: the new file is written completely before it replaces the old one, so an interrupted export never leaves a half-written file behind. This is the way to back up your list or hand a set of places to someone else running QuickerPlaces.

## Importing places

Click **Import...** and pick a `.json` file that was previously created with Export. QuickerPlaces compares every item in that file against what you already have and silently drops anything that would collide — same alias (case-insensitive) or same path/URL (exact match) as something you already saved. You're never shown or asked about those; there's nothing to decide.

What's left — the items that don't collide with anything — is presented as a checklist, all checked by default, exactly like Export. Uncheck anything you don't want, click **Import**, and the selected items are added and written to disk immediately. A short summary tells you how many were brought in.

If everything in the file collides with what you already have, you'll see an empty (or very short) list — that's expected, not an error.

## Where your data lives

QuickerPlaces keeps two small JSON files, both plain text and safe to open in a text editor if you're curious or want to back them up manually:

- **Your places:** `%AppData%\QuickerPlaces\QuickerPlaces\places.json` — written the instant anything changes.
- **Window layout** (size, position, whether the grid is collapsed) and the **global hotkey**: `%LocalAppData%\QuickerPlaces\QuickerPlaces\settings.json` — saved when the window closes.

You never need to touch either file by hand, but if you ever want to move your places to another machine, copying `places.json` across is all it takes. The folder icon at the left of the header opens the folder that holds `places.json` in File Explorer, with the file selected. Any backup copies of an unreadable places file (see below) are in the same folder.

## If something goes wrong

- **First launch, or a missing places file:** QuickerPlaces just starts with an empty list — this is normal, not an error, and your first **Add Folder**/**Add URL** creates the file.
- **A places file that can't be read** (corrupted, edited by hand and broken, etc.): QuickerPlaces tells you once, on startup, that it couldn't load your saved places, and starts you with an empty list rather than crashing. Before anything else happens, it saves a copy of the unreadable file next to the original, named like `places.corrupt-20260925-181500.json`, and the startup message tells you exactly where. Your next change overwrites `places.json` itself, but the copy stays put, so if you know your way around JSON you can fix the copy and bring it back with **Import...**, or rename it back to `places.json` while QuickerPlaces is closed.
- **A change that can't be saved** (disk full, or the file is locked by another program such as a backup or sync tool): you'll get a warning the first time it happens. The change stays in the app, and QuickerPlaces tries again with every later change and once more when you close the window. If it still can't save when you close, it asks before closing, so you can fix the problem first instead of losing those changes.
- **A place that won't open:** you'll get an on-screen message explaining why (folder no longer exists, URL is malformed, etc.) rather than the app freezing or closing.

If you hit anything not covered here, or something that looks like an actual crash, that's worth reporting rather than working around — see `ai/BUILD_SUMMARY.md` for the project's current known-issues status.
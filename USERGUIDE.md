# QuickerPlaces — User Manual

QuickerPlaces is a small always-on-top-of-your-workflow window for storing folder paths and URLs under a short, memorable name (an **alias**), so you can get back to them in one click instead of digging through File Explorer or your bookmarks. This guide covers everything you can do in the app itself. For what's under the hood, see `ai/BUILD_SUMMARY.md`.

## The window, at a glance

When QuickerPlaces opens you'll see, top to bottom:

- A header with the app name and seven buttons: a gear icon for **Settings**, a folder icon that opens your data folder, a bin icon for **Recently Deleted**, **Import...**, **Export...**, **Add Folder**, **Add URL**.
- A row of **favourite bubbles** — your pinned places, one click away. Empty at first, with a hint telling you how to add one.
- The **All Places** header with a count, a **search box**, and a **Hide List** / **Show List** button that collapses or restores everything below it.
- The **All Places** grid — every place you've saved, one row each, each with a folder or globe icon showing its type.

Nothing here needs a save button. Every add, edit, favourite toggle, reorder, removal, or restore is written to disk the moment it happens — and if that write can't complete for some reason, a banner appears above the favourite bubbles to tell you so (see "The unsaved-changes banner" below).

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
  - **Remove** — moves it to **Recently Deleted** straight away, without asking first, because nothing is lost yet. The row and its bubble disappear, and a bar under the list says **Moved "Docs" to Recently Deleted.** with an **Undo** button. **Ctrl+Z** does the same as Undo. Either one puts the place back exactly where it was, including its spot among your favourites. You can undo several removals in a row, most recent first, for as long as QuickerPlaces stays open. After that, including after a restart, the place waits in Recently Deleted for seven days (see "Recently Deleted" below).

    The bar stays for about 10 seconds (other messages there stay about 8), and it waits while your mouse pointer is over it or you've tabbed into it, so reaching for **Undo** never races it. It never takes the keyboard focus away from what you were doing. Ctrl+Z still works after it has gone.

    If you've since given that alias or path/URL to another place, Undo opens a **Restore Place** dialog instead. It says which place is in the way and shows the alias and path/URL filled in, with the one that's in the way selected so you can type a new value. Click **Restore** to bring the place back under the new values, or **Cancel** to leave it in Recently Deleted. If the place's seven days in Recently Deleted have run out, Undo tells you it's gone.

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

## Recently Deleted

Places you remove aren't deleted straight away. They go to **Recently Deleted**, which you open with the bin icon in the header. They don't appear in the grid, the bubbles or search while they're there.

**How long they stay.** At least seven full days: 7 × 24 hours from the moment you removed the place. After that it's deleted for good the next time QuickerPlaces starts or saves a change, so if QuickerPlaces isn't running, a place can stay a little longer than seven days, never less. The **Days remaining** column counts down from **7 days**. It rounds up, so **1 day** means less than 24 hours are left. **Expiring** means the seven days are up but QuickerPlaces hasn't started or saved since, and you can still restore the place until it does. Hover over a **Days remaining** cell to see the exact date and time.

**What it shows.** Alias (with the folder or globe icon), Type, Path / URL, **Deleted** (the date and time you removed it, in your local time) and **Days remaining**. The most recently removed place is at the top. Click a column header to sort. Select one place with a click, or several with Shift or Ctrl.

**What you can do:**

- **Restore selected** (**Alt+R**) — puts the selected places back in the grid, each in its old spot in the list, and favourites back in their old place among the bubbles.
- **Delete selected permanently** (**Alt+D**) — deletes the selected places for good.
- **Empty Recently Deleted** (**Alt+E**) — deletes every place in Recently Deleted for good.
- **Close** — Esc closes the dialog too.

The two permanent actions can't be undone, so both ask first. In that question, **Cancel** is the default: Enter, Space and Esc all back out, and so does closing the question window. Only clicking the button labelled **Delete permanently** or **Empty Recently Deleted** (or tabbing to it and pressing it) goes ahead. Pressing **Delete** in the Recently Deleted list does nothing, so nothing here is one keystroke away from being lost.

**Restore conflicts.** Places in Recently Deleted never block an alias or path/URL: as soon as you remove "Docs", you can add a new place called "Docs". If you later restore the old one, it can't come back as it was. **Restore selected** first brings back every selected place that doesn't clash with anything, then shows the **Restore Place** dialog for each one that does, one at a time. That dialog explains which place now has its alias or path/URL, and lets you change either before clicking **Restore**. **Cancel** leaves that place in Recently Deleted and moves on to the next. If two selected places have the same alias, the more recently removed one is restored and the other one goes to Restore Place.

If a change made here can't be saved, a line in the Recently Deleted dialog says why, and the unsaved-changes banner is waiting in the main window when you close it. When everything has been removed from the grid, its empty-list hint also reminds you that your removed places are in Recently Deleted.

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
| **Ctrl+Z** | Undo the last Remove (repeat for earlier ones, this session) |
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
| **Delete** | Remove (moves it to Recently Deleted; the next row is then selected) |

## The unsaved-changes banner

If QuickerPlaces can't write a change to disk — the file's permissions changed, another program is holding it open, the disk is full, and so on — a banner appears at the top of the window instead of the app silently pretending everything is fine.

The banner means exactly one thing: **the change you just made is on your screen but not yet on disk.** Nothing is lost — whatever you added, renamed, edited, favourited, reordered, removed, restored, or permanently deleted stays exactly as you left it in the app. It just hasn't been written to `places.json` yet.

Three buttons on the banner:

- **Retry** — tries the save again. If whatever was blocking it has cleared up (permission restored, the other program closed the file, disk space freed), the banner disappears and your change is now safely on disk.
- **Show Data Folder** — opens File Explorer to your `places.json` file, in case you want to check permissions, free up disk space, or see what's going on yourself.
- **Show Log** — opens the diagnostic log (see below) with whatever program you have associated with `.log` files, so you or someone helping you can see exactly what QuickerPlaces tried and what went wrong.

The banner stays up until a save actually succeeds. Doing something else in the app — adding another place, editing a different one — does **not** make it go away; only a real, successful write to disk clears it. If you're not sure whether a change made it to disk, the banner is the answer: no banner means everything is saved.

If you close QuickerPlaces while the banner is showing, it tries the save once more first. If that still fails, it asks before closing, so you can sort out the problem instead of losing those changes.

## The startup recovery prompt

Occasionally, when QuickerPlaces starts up, it finds that your `places.json` file isn't in a state it can just load normally. When that happens, you'll see a prompt before the main window opens, and it's worth knowing which of three situations you're in, because they're different and QuickerPlaces treats them very differently on purpose:

- **"...couldn't read your saved places. The file appears to be damaged."** The file opened, but what's in it doesn't make sense as a places file — it may have been edited by hand and broken, or corrupted some other way. You get three choices: **Show me the file** (opens Explorer with it selected, then asks again), **Start with an empty list** (only offered here — see below), and **Exit**.
- **"...couldn't open your saved places. Another program may be using the file, or it may not have permission to read it. Your data is most likely fine."** This is a different, much less worrying situation: QuickerPlaces couldn't even get to your data to check it, most likely because a sync tool, antivirus, or another program briefly has the file open, or a permissions setting is blocking it. Your choices are **Try again** (re-attempts the load — often all you need if you just wait a second and click it), **Show me the file**, and **Exit**.
- **"These saved places were written by a newer version of QuickerPlaces."** Your file is completely intact — it was just saved by a version of the app newer than the one you're currently running, and this build doesn't know how to safely read fields it doesn't recognize. **Exit** and update QuickerPlaces is the recommended path; **Show me the file** is also offered.

The important thing to notice is that **only the "damaged" prompt ever offers to start fresh.** When QuickerPlaces says it *couldn't open* your file, rather than that it's damaged, your data is most likely completely fine, and QuickerPlaces will not touch, rename, or replace that file no matter which button you click — there is deliberately no "start with an empty list" option for that case, because doing so could throw away data that was never actually at risk. The same is true if the file was written by a newer version: it's intact, this build just can't read it yet, so nothing is offered that would overwrite it.

If you do choose to start fresh from a damaged file, QuickerPlaces doesn't delete the original — it renames it to something like `places.corrupt-20260902-143022.json` right next to where `places.json` normally lives, and tells you (in the log) where it put it. If you or someone technical wants to try to recover data from it by hand later, it's still there.

## Where the log lives

QuickerPlaces keeps a small diagnostic log at `%LocalAppData%\QuickerPlaces\QuickerPlaces\logs\quickerplaces.log`, a plain text file. It records things like startup, whether your places file loaded normally, save failures and why, and recovery choices — the kind of detail useful for figuring out what went wrong if something did.

It's capped at 256 KB and rolls over to a second file (`quickerplaces.1.log`) once it fills up, so it can never grow without bound even if something keeps failing while you leave the app running. It never contains any of your aliases or the folder paths/URLs you've saved — only counts and file paths — so it's safe to share with someone helping you troubleshoot without handing over your actual data.

## Only one QuickerPlaces at a time

QuickerPlaces only allows one running copy per Windows user on a machine. If you try to launch it again while it's already running — from a shortcut, from Explorer, however — the existing window is brought to the front instead of a second copy opening, with the search box ready, just like the global shortcut. (If the window happens to be minimized, it's restored first.) This is what keeps two copies from ever fighting over the same `places.json` file and one silently overwriting the other's changes.

## Exporting places

Click **Export...** to open a checklist of every place in your list, all checked by default. Places in Recently Deleted are never exported. Uncheck anything you don't want to include, then choose where to save the resulting `.json` file. Saving over an earlier export is safe: the new file is written completely before it replaces the old one, so an interrupted export never leaves a half-written file behind. This is the way to back up your list or hand a set of places to someone else running QuickerPlaces.

## Importing places

Click **Import...** and pick a `.json` file that was previously created with Export. QuickerPlaces compares every item in that file against what you already have and silently drops anything that would collide — same alias (case-insensitive) or same path/URL (exact match) as something you already saved. You're never shown or asked about those; there's nothing to decide.

What's left — the items that don't collide with anything — is presented as a checklist, all checked by default, exactly like Export. Uncheck anything you don't want, click **Import**, and the selected items are added and written to disk immediately. A short summary tells you how many were brought in.

If everything in the file collides with what you already have, you'll see an empty (or very short) list — that's expected, not an error.

A few more rules:

- Only places in your list count as collisions. A place in the file whose alias or path/URL matches only something in your Recently Deleted is still offered. (Restoring that old one later then goes through the Restore Place dialog.)
- Removed places in a file are skipped. Exports never contain them, but a copied `places.json` can.
- Exports made by older versions of QuickerPlaces still import.
- A file exported by a newer version of QuickerPlaces is refused with "That file was exported by a newer version of QuickerPlaces. Update QuickerPlaces to import it." Nothing is offered from it.

## Where your data lives

QuickerPlaces keeps a few small plain-text files, all safe to open in a text editor if you're curious or want to back them up manually:

- **Your places:** `%AppData%\QuickerPlaces\QuickerPlaces\places.json` — written the instant anything changes. It holds Recently Deleted too.
- **A backup of the previous version:** `%AppData%\QuickerPlaces\QuickerPlaces\places.bak.json` — QuickerPlaces keeps the previous contents of `places.json` every time it saves, automatically, right next to it. You don't need to do anything to get this; it's just there as an extra safety net.
- **Window layout** (size, position, whether the grid is collapsed) and the **global hotkey**: `%LocalAppData%\QuickerPlaces\QuickerPlaces\settings.json` — saved when the window closes, and straight away when you change the shortcut in Settings.
- **The diagnostic log:** `%LocalAppData%\QuickerPlaces\QuickerPlaces\logs\quickerplaces.log` — see "Where the log lives" above.

You never need to touch any of these files by hand, but if you ever want to move your places to another machine, copying `places.json` across is all it takes (Recently Deleted goes with it). The folder icon at the left of the header opens the folder that holds `places.json` in File Explorer, with the file selected. The backup and any set-aside damaged files (see "The startup recovery prompt") are in the same folder.

**Upgrading from a version without Recently Deleted.** This version stores `places.json` in a new format: dates are kept in UTC (the grid still shows your local date), and removed places are marked rather than deleted. The first time it starts, it converts your existing file in memory and writes nothing. The converted file is saved with your first change, and right after that save `places.bak.json` is your pre-upgrade file, until the next save replaces it. From then on, an older version of QuickerPlaces that has the startup recovery prompt says the file was written by a newer version, and leaves it untouched. A version from before the recovery prompt existed doesn't check: it would show the places in Recently Deleted as ordinary places, and forget they were removed the next time it saved. Don't go back to one of those with this file.

## If something goes wrong

- **First launch, or a missing places file:** QuickerPlaces just starts with an empty list — this is normal, not an error, and your first **Add Folder**/**Add URL** creates the file.
- **A places file that can't be loaded** (damaged, held open by another program, or from a newer version of the app): see "The startup recovery prompt" above — QuickerPlaces asks you what to do rather than guessing, and it never touches your file except when you explicitly choose to start fresh from a genuinely damaged one.
- **A save that doesn't go through:** see "The unsaved-changes banner" above — your change stays visible and nothing is lost; the banner tells you and lets you retry.
- **Removed something by mistake:** press Ctrl+Z (while QuickerPlaces is still open), or restore it from Recently Deleted within seven days.
- **A place that won't open:** you'll get an on-screen message explaining why (folder no longer exists, URL is malformed, etc.) rather than the app freezing or closing.

If you hit anything not covered here, or something that looks like an actual crash, that's worth reporting rather than working around — see `ai/BUILD_SUMMARY.md` for the project's current known-issues status.
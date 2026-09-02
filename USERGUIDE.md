# QuickerPlaces — User Manual

QuickerPlaces is a small always-on-top-of-your-workflow window for storing folder paths and URLs under a short, memorable name (an **alias**), so you can get back to them in one click instead of digging through File Explorer or your bookmarks. This guide covers everything you can do in the app itself. For what's under the hood, see `ai/BUILD_SUMMARY.md`.

## The window, at a glance

When QuickerPlaces opens you'll see, top to bottom:

- A header with the app name and four buttons: **Import...**, **Export...**, **Add Folder**, **Add URL**.
- A row of **favourite bubbles** — your pinned places, one click away. Empty at first, with a hint telling you how to add one.
- A **Hide List** / **Show List** button that collapses or restores everything below it.
- The **All Places** grid — every place you've saved, one row each.

Nothing here needs a save button. Every add, edit, favourite toggle, reorder, or removal is written to disk the moment it happens — and if that write can't complete for some reason, a banner appears above the favourite bubbles to tell you so (see "The unsaved-changes banner" below).

## Adding a place

Click **Add Folder** or **Add URL** in the header. Either opens the same small dialog:

1. **Alias** — the name you'll use to recognize this place. Must be unique; "Docs" and "docs" count as the same alias, so you'll be blocked (with a clear message) if you try to reuse one.
2. **Path or URL** — for a folder, either type the path or use the **Browse...** button to pick it; for a URL, type it in (e.g. `https://wiki.example.com`). This also has to be unique — you can't save the same path or URL twice. Note that this check is exact: `C:\Projects` and `C:\Projects\` are treated as different values, as are `http://` and `https://` versions of the same site, so use whichever form you actually want to keep.

Click **Save**, and the new place appears immediately at the bottom of the grid.

If you enter a folder path, QuickerPlaces checks that it's a syntactically valid path — it does not require the folder to already exist on disk, so you can save a place for a folder you're about to create. A URL is checked for being well-formed but is never contacted or pinged when you save it.

## Working with a place in the grid

Every row in the **All Places** grid shows the Alias, Type (Folder or URL), the Path/URL, whether it's a Favourite, and the date it was added.

- **Double-click a row** to open it — a folder opens in File Explorer, a URL opens in your default browser.
- **Right-click a row** for the full menu:
  - **Open** — same as double-click.
  - **Rename Alias** — change just the name; the same uniqueness check from adding applies.
  - **Edit Path/URL** — change just the destination; the same duplicate check applies.
  - **Toggle Favourite** — pin it to (or unpin it from) the bubble row above the grid.
  - **Remove** — deletes it. There's no undo, so double-check before confirming.

If a place can no longer be opened — the folder's been deleted, or the URL is malformed — you'll get a clear message instead of the app crashing or silently doing nothing.

## Favourites

Any place can be a favourite. Toggling **Favourite** (from the grid's right-click menu, or from a bubble's own right-click menu) adds or removes it from the bubble row above the grid.

- **Click a bubble** to open that place — identical to double-clicking its row.
- **Drag a bubble** left or right to reorder the row. The order you leave them in is remembered.
- **Right-click a bubble** for a shortcut menu: **Open**, or **Remove from Favourites** — you don't need to go back to the grid just to unpin something.

## Hiding the grid

If you only want the favourite bubbles visible, click **Hide List**. The grid collapses and the window shrinks to make room; click **Show List** to bring it back. This state is remembered between sessions, along with the window's size and position.

## The unsaved-changes banner

If QuickerPlaces can't write a change to disk — the file's permissions changed, another program is holding it open, the disk is full, and so on — a banner appears above the favourite bubbles instead of the app silently pretending everything is fine.

The banner means exactly one thing: **the change you just made is on your screen but not yet on disk.** Nothing is lost — whatever you added, renamed, edited, favourited, reordered, or removed stays exactly as you left it in the app. It just hasn't been written to `places.json` yet.

Three buttons on the banner:

- **Retry** — tries the save again. If whatever was blocking it has cleared up (permission restored, the other program closed the file, disk space freed), the banner disappears and your change is now safely on disk.
- **Show Data Folder** — opens File Explorer to your `places.json` file, in case you want to check permissions, free up disk space, or see what's going on yourself.
- **Show Log** — opens the diagnostic log (see below) with whatever program you have associated with `.log` files, so you or someone helping you can see exactly what QuickerPlaces tried and what went wrong.

The banner stays up until a save actually succeeds. Doing something else in the app — adding another place, editing a different one — does **not** make it go away; only a real, successful write to disk clears it. If you're not sure whether a change made it to disk, the banner is the answer: no banner means everything is saved.

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

QuickerPlaces only allows one running copy per Windows user on a machine. If you try to launch it again while it's already running — from a shortcut, from Explorer, however — the existing window is brought to the front instead of a second copy opening. (If the window happens to be minimized, it's restored first.) This is what keeps two copies from ever fighting over the same `places.json` file and one silently overwriting the other's changes.

## Exporting places

Click **Export...** to open a checklist of every place you've saved, all checked by default. Uncheck anything you don't want to include, then choose where to save the resulting `.json` file. This is the way to back up your list or hand a set of places to someone else running QuickerPlaces.

## Importing places

Click **Import...** and pick a `.json` file that was previously created with Export. QuickerPlaces compares every item in that file against what you already have and silently drops anything that would collide — same alias (case-insensitive) or same path/URL (exact match) as something you already saved. You're never shown or asked about those; there's nothing to decide.

What's left — the items that don't collide with anything — is presented as a checklist, all checked by default, exactly like Export. Uncheck anything you don't want, click **Import**, and the selected items are added and written to disk immediately. A short summary tells you how many were brought in.

If everything in the file collides with what you already have, you'll see an empty (or very short) list — that's expected, not an error.

## Where your data lives

QuickerPlaces keeps a few small plain-text files, all safe to open in a text editor if you're curious or want to back them up manually:

- **Your places:** `%AppData%\QuickerPlaces\QuickerPlaces\places.json` — written the instant anything changes.
- **A backup of the previous version:** `%AppData%\QuickerPlaces\QuickerPlaces\places.bak.json` — QuickerPlaces keeps the previous contents of `places.json` every time it saves, automatically, right next to it. You don't need to do anything to get this; it's just there as an extra safety net.
- **Window layout** (size, position, whether the grid is collapsed): `%LocalAppData%\QuickerPlaces\QuickerPlaces\settings.json` — saved when the window closes.
- **The diagnostic log:** `%LocalAppData%\QuickerPlaces\QuickerPlaces\logs\quickerplaces.log` — see "Where the log lives" above.

You never need to touch any of these files by hand, but if you ever want to move your places to another machine, copying `places.json` across is all it takes.

## If something goes wrong

- **First launch, or a missing places file:** QuickerPlaces just starts with an empty list — this is normal, not an error, and your first **Add Folder**/**Add URL** creates the file.
- **A places file that can't be loaded** (damaged, held open by another program, or from a newer version of the app): see "The startup recovery prompt" above — QuickerPlaces asks you what to do rather than guessing, and it never touches your file except when you explicitly choose to start fresh from a genuinely damaged one.
- **A save that doesn't go through:** see "The unsaved-changes banner" above — your change stays visible and nothing is lost; the banner tells you and lets you retry.
- **A place that won't open:** you'll get an on-screen message explaining why (folder no longer exists, URL is malformed, etc.) rather than the app freezing or closing.

If you hit anything not covered here, or something that looks like an actual crash, that's worth reporting rather than working around — see `ai/BUILD_SUMMARY.md` for the project's current known-issues status.
# QuickerPlaces — User Manual

QuickerPlaces is a small always-on-top-of-your-workflow window for storing folders and links under a short, memorable name, so you can get back to them in one click instead of digging through File Explorer or your bookmarks. This guide covers everything you can do in the app itself. For what's under the hood, see `ai/BUILD_SUMMARY.md`.

## The window, at a glance

When QuickerPlaces opens you'll see, top to bottom:

- A header with the app name, **Recents**, **Library**, **Sessions**, **Add folder**, **Add link**, and **Settings**. Recents keeps the activity icon and shows a dot while folder tracking is on. The **Options** menu beside **Hide list** holds **Recently Deleted**, **Places file**, **Import places**, and **Export places**.
- A row of **favourite cards** — your pinned places, one click away. Empty at first, with a hint telling you how to add one.
- The **All Places** header with a count, a **search box**, the hamburger **Options** menu, and a **Hide list** / **Show list** button that collapses or restores everything below it.
- The **All Places** grid — every place you've saved, one row each, each with a folder or globe icon showing its type.

Nothing here needs a save button. Every add, edit, favourite toggle, reorder, removal, or restore is written to disk the moment it happens — and if that write can't complete for some reason, a banner appears above the favourite cards to tell you so (see "The unsaved-changes banner" below).

## Adding a place

Click **Add folder** or **Add link** in the header. Either opens the same small dialog:

1. **Name** — the name you'll use to recognize this place. Must be unique; "Docs" and "docs" count as the same name, so you'll be blocked (with a clear message) if you try to reuse one.
2. **Folder path** (when adding a folder) or **Link** (when adding a link) — for a folder, either type the path or use the **Browse...** button to pick it; for a link, type it in (e.g. `https://wiki.example.com`). This also has to be unique — you can't save the same folder or link twice. Note that this check is exact: `C:\Projects` and `C:\Projects\` are treated as different values, as are `http://` and `https://` versions of the same site, so use whichever form you actually want to keep.

For a folder, the **Name** fills itself in with the folder's own name as soon as you pick or paste the path: `C:\Users\Thomas\Downloads\UFGS_M` suggests **UFGS_M**. Change it if you want something else. Once you've typed your own name, changing the path leaves it alone.

Click **OK**, and the new place appears immediately at the bottom of the grid.

If you enter a folder path, QuickerPlaces checks that it's a syntactically valid, *full* path. It has to start from a drive (`C:\Projects`) or a network share (`\\server\share`); a relative path like `Projects` is rejected. It does not require the folder to already exist on disk, so you can save a place for a folder you're about to create. A link is checked for being well-formed but is never contacted or pinged when you save it.

## Working with a place in the grid

Every row in the **All Places** grid shows the Name (with a folder or globe icon showing its type), the Folder or link, when you **Last Opened** it, and how many **Opens** it has (see "Last Opened and Opens" below). The date you added a place is still kept, and exported, but no longer has a column. An orange star after a name means that place is a favourite. Point at any other row, or select it, and a faint star appears after its name: click it to make the place a favourite, or click an orange star to stop it being one.

- **Double-click a row** to open it — a folder opens in File Explorer, a link opens in your default browser.
- **Right-click a row** for the full menu:
  - **Open** — same as double-click.
  - **Copy folder or link** — puts the folder path or link on the clipboard, ready to paste into another app.
  - **Rename** — change just the name; the same uniqueness check from adding applies.
  - **Edit folder or link** — change just the destination; the same duplicate check applies.
  - **Toggle favourite** — pin it to (or unpin it from) the card row above the grid.
  - **Remove** — moves it to **Recently Deleted** straight away, without asking first, because nothing is lost yet. The row and its card disappear, and a bar under the list says **Moved "Docs" to Recently Deleted.** with an **Undo** button. **Ctrl+Z** does the same as Undo. Either one puts the place back exactly where it was, including its spot among your favourites. You can undo several removals in a row, most recent first, for as long as QuickerPlaces stays open. After that, including after a restart, the place waits in Recently Deleted for seven days (see "Recently Deleted" below).

    The bar stays for about 10 seconds (other messages there stay about 8), and it waits while your mouse pointer is over it or you've tabbed into it, so reaching for **Undo** never races it. It never takes the keyboard focus away from what you were doing. Ctrl+Z still works after it has gone.

    If you've since given that name or folder or link to another place, Undo opens a **Restore place** dialog instead. It says which place is in the way and shows the name and folder or link filled in, with the one that's in the way selected so you can type a new value. Click **Restore** to bring the place back under the new values, or **Cancel** to leave it in Recently Deleted. If the place's seven days in Recently Deleted have run out, Undo tells you it's gone.

If a place can no longer be opened — the folder's been deleted, or the link is malformed — you'll get a clear message instead of the app crashing or silently doing nothing.

## Sorting the grid

Click a column header to sort by it. **Last Opened** and **Opens** start with the most recent and the most used. The other columns start A to Z. Click the same header again to reverse it; each click flips it between up and down. The sorted column's header has an arrow showing the direction and a coloured line along its bottom edge. Until you first click a header, the grid is in the order you added your places.

Places with the same value, such as several that have never been opened, are listed by name. A place you've never opened always counts as the oldest, so it's at the bottom when Last Opened shows the newest first.

The sort is remembered on this computer, and is in place the next time QuickerPlaces starts.

Sorted by Last Opened, newest first, the grid keeps itself in order: a place you open moves straight to the top. Combined with the search box this is a quick way back to recent work. Type a few letters, press **Enter**, and the most recently opened match opens.

## Searching

Type in the **Search places** box above the grid (or press **Ctrl+F** to jump there) and the grid narrows as you type. A place matches when every word you type appears somewhere in its name or its folder or link, ignoring case. So `wiki prod` finds a place named "Prod Wiki", and it also finds one named "Wiki" that points at `https://prod.example.com`. The header shows how many places match: a count such as **3 of 12** appears beside **ALL PLACES**.

The search box works as a quick launcher:

- **Enter** opens the top result.
- **Down arrow** moves into the grid so you can pick a different row (then **Enter** opens it).
- **Esc** clears the search. Pressing it again with the box already empty moves you into the grid.

The ✕ button beside the box also clears it. Searching only filters the grid; your favourite cards always stay visible. If the list is hidden, typing a search brings it back. If you add or import a place that the current search would hide, the search is cleared so the new place is visible.

## Favourites

Any place can be a favourite. Click the star after its name in the grid (it appears when you point at the row), or press **Ctrl+D** with the row selected, or choose **Toggle favourite** from its right-click menu. An orange star means it's a favourite, and it gets a card in the row above the grid. Clicking the orange star, or **Remove from favourites** on the card's own right-click menu, takes it off again.

- **Click a card** to open that place — identical to double-clicking its row.
- **Drag a card** to reorder favourites. The dragged card dims and a highlighted insertion marker shows where it will land. Use the upper/lower half of a stacked card, or the left/right half of a card in a row, to place it before/after that card. Release to save the new order, or press **Esc** to cancel.
- **Right-click a card** for a shortcut menu: **Open**, **Copy folder or link**, or **Remove from favourites** — you don't need to go back to the grid just to unpin something.
- **Hover over a card** to see where it points.
- **Ctrl+1** to **Ctrl+9** open the first nine cards, numbered in their left-to-right order.

## Last Opened and Opens

The grid's **Last Opened** column shows the date and time you last opened a place from QuickerPlaces, in your regional date format. **Opens** shows how many times you have. A place you've never opened shows **—** and 0.

What counts, precisely: an open counts when QuickerPlaces asks Windows to open the place and Windows accepts. That's the same whether you double-click a row, press Enter, use the right-click **Open**, click a card, press Ctrl+1 to Ctrl+9, or press Enter in the search box. Some things don't count:

- Opening the same folder or website some other way, such as from File Explorer, a browser bookmark or another program. QuickerPlaces only knows about what it opened.
- A folder that no longer exists. You get the "no longer exists" message, and nothing changes.
- A place Windows refuses to open. You get the "Couldn't open" message, and nothing changes.

Windows accepting the request is all QuickerPlaces can see. If the program that receives it then fails, for example a browser that can't reach the site, the open has still been counted.

These numbers never change your favourites or their order. Removing a place to Recently Deleted and restoring it keeps them.

## Folder Activity

**Folder Activity is off until you choose a folder to track.** Click **Recents** in the header, then **Track a folder**, choose a folder, and read the confirmation before clicking **Start tracking**. QuickerPlaces records folder paths under that tracked folder, visit counts, and time when one of those folders is shown in the foreground in File Explorer while you are active. It does not read file names or contents, watch other apps, or track folders outside folders you added. The dot on Recents shows when tracking is on.

Choose a tracked folder card, then use **Week**, **Month**, **Day**, **Previous**, or **Next** in the year activity header to navigate the activity table. The year strip starts on January 1; its day squares stay aligned by week, with lines stepping between days at each month boundary. An orange outline always marks today, while outlines in your highlight colour show the week, month, or day currently selected in the table. These outlines leave the activity colours unchanged. Choose any day to view its week, month, or individual day according to the active view. The arrow keys move the selected period: in Day view, up/down moves one day and left/right moves one week; in Week and Month views, each arrow moves one week or month. Click the year in the header to choose from a scrollable list covering 2026 through 2100. A stronger leather colour means more recorded time relative to the other nonzero days shown; faint outlined squares are days outside tracked history, including future days, and remain selectable. The table's columns are Folder, Visits, Time, Last visited, and In places. Click a heading to sort, or check **Group by folder level** to group rows by their depth below the tracked folder. **Add as place** opens the normal Add folder dialog with that folder filled in; it does not add anything until you save there.

Visits have a five-second threshold by default, so quickly passing through a folder does not count. Time starts counting after that threshold. Switching to another app and back to the same Explorer folder continues the visit, without crediting the time away. Leaving a folder open while you are idle, locking the PC, or suspending it does not add time. A visit to a folder through Explorer can count even if you opened it from QuickerPlaces. The figures describe foreground Explorer time, not the amount of work completed.

Right-click a tracked folder card and choose **Edit tracking settings…** to change how new visits are recorded: **First folder below root** combines everything under each immediate child, **All subfolders** keeps each visited folder, and **Chosen depth below root** groups at a chosen number of levels. You can also change the visit threshold and idle timeout, or add equivalent paths to the same folder. The same menu has **About folder…** and **Stop tracking** or **Resume tracking**. For a mapped drive, the Add folder confirmation offers its network path when Windows supplies one; selecting it lets either route count toward the same folder row.

Changing the grouping affects **new visits only**. Earlier rows keep the grouping used when they were recorded, because the finer folder paths needed to split them later were not saved. For example, after changing a `C:\X` root from Depth 1 to Depth 2, an earlier `C:\X\2024` row stays visible; a new qualifying visit to `C:\X\2024\240015` adds a separate row. Keep Explorer in the foreground past the visit threshold, then return to the Folder Activity window (**Recents**); its view refreshes about every 30 seconds.

**Stop tracking** keeps the tracked folder and its recorded data, and **Resume tracking** starts it again. A tracked folder can't be deleted, so its history is never lost. Before tracking began, or on days recorded before your history began keeping folder detail, the app does not present missing detail as zero activity: it says the detail has expired.

**Your folder activity is kept for good** in your activity history (see *Activity history* below), so Recents can show any day you have tracked, however long ago.

The last 62 days are stored on this computer in `%LocalAppData%\QuickerPlaces\QuickerPlaces\activity.json`, separately from your Places and their export; older days are in your activity history. It is buffered and saved about every five minutes, and on idle, lock, and exit; a crash can lose up to the last five minutes. If a tracked folder's setting cannot be saved, the Folder Activity window shows an error and **Retry save**. Explorer restarts can cause a brief gap before tracking resumes. After you have been idle past the root's timeout, QuickerPlaces checks for your return every 15 seconds; a short test visit made immediately on return may not count until that check and the visit threshold have both elapsed.

### Keeping tracking on in the background

In **Settings**, **Keep running in the tray when I close the window** makes closing the main window hide it while QuickerPlaces continues running. The tray icon's menu has **Open QuickerPlaces**, **Pause tracking** or **Resume tracking**, and **Exit**. Pausing stops sampling until you resume. The global shortcut or starting QuickerPlaces again also reopens the hidden window. **Start with Windows in the tray** starts it at sign-in; turning this off removes its per-user startup entry. Both switches are off by default. You can turn either off in Settings at any time.

## Project Sessions

A **session** is a named, tagged set of files you had open together — the drawings, specs and spreadsheets for one job, say — saved so you can see it later and open them all again in one go. Sessions hold PDF, Word and Excel files.

**Saving what's open.** Open the files you're working on, then click **Sessions** in the header and **Save open files…**. QuickerPlaces looks for the PDF, Word and Excel files open right now and lists them for you to check: the ones it believes are open are ticked, and files you opened recently are listed below them, unticked, in case it missed one. Give the session a name, add tags if you like (separate them with commas — *Tower B, markups, RFI 12*; tags you've used before are one click away under the box), tick exactly the files you want, and click **Save**. Nothing is saved until you do, and only ticked files are saved.

- **Add files…** picks files by hand. **Find open files** looks again, keeping your ticks. **Remove from list** (or Delete) drops the selected rows from the list — the files themselves are never touched. Space ticks or unticks the selected rows.
- **What QuickerPlaces can see.** Windows has no list of the documents other programs have open, so QuickerPlaces pieces it together from window titles, the files a program was started with, the files programs are holding open, and Windows' own list of recent files. Acrobat, Reader, Bluebeam Revu, Word and Excel keep their files open, so every tab and document should be found — Word and Excel even when Explorer hides extensions and the title just says "Report - Word". A web browser or SumatraPDF shows only the front tab in its title, so other tabs can't be seen — add those with **Add files…**. If a window shows a document name that couldn't be matched to a file, the line under the list names it so you know to add it. Always glance down the list before saving. Files are listed with your mapped drive letter. Copies a program keeps for itself (such as Revu Studio Session files) and Word and Excel templates aren't listed, and a program run as administrator can't be seen, so its files may be missing.

**Finding and reopening a session.** The **Project Sessions** window lists your sessions, most recently used first, each with its tags, how many files it has, and when it was saved and last opened. Type in the search box to find a session by its name, a tag, or the name of a file in it; click a tag chip to show only sessions with that tag (**All tags** shows everything again). Select a session to see its files on the right, then:

- **Open all** (or Enter, or double-click the session) opens every file in it with its usual program. A file that has since been moved, renamed or deleted is skipped and named in red; the rest still open.
- **Open selected file** (or double-click a file, or Enter on it) opens just that one.
- **Edit…** renames the session, changes its tags, or adds and removes files (**Find open files** adds what's open now).
- **Delete…** deletes the session after asking. Only the session goes; its files stay where they are.
- **Share…** saves the session as a file to send to another QuickerPlaces user (see *Sharing a session* below).

Opening a session doesn't count as opening a place: Last Opened and Opens in the main grid are unaffected, and a session's files never appear there.

Drag a session card to reorder it. The card dims while you drag, and a highlighted insertion marker shows its destination. Hover over the upper or lower half of another card to insert before or after it. Dropping saves the order and updates the **Ctrl+Shift+1…9** shortcuts; **Esc** cancels the drag.

### Sharing a session

**Sending one.** Select a session and choose **Share…** (also on the card's right-click menu). The list shows each file and where it lives:

- **SharePoint or Teams**: in a library you sync with OneDrive. Others with access to the library can use it, even though it syncs to a different folder on their PC.
- **Personal OneDrive**: in your own OneDrive. Others can open it only if you've shared it with them in OneDrive.
- **Network share**: on a `\\server\share` path or a mapped drive.
- **This PC only**: anywhere else. No one else can reach these, so they start unticked. Move them to a shared library first if others need them.

Tick the files to share and click **Save shared file…** to save a `.qpsession` file. Send it any way you like: email, Teams or a shared folder. The file lists each file's full path on your PC (which can include your Windows user name) and its web or network address. It doesn't contain the files themselves, or when you opened them.

**Opening one.** Click **Open shared…** beside **Save files** and choose the `.qpsession` file, or drag the file onto the list of sessions. QuickerPlaces looks for each file on your PC and says what it found:

- **Found in your synced library**: the file is in your own synced copy of the same SharePoint or Teams library, wherever that is on your PC.
- **Found at the same path**: the same path as on the sender's PC.
- **Library not synced here**: right-click the file and choose **Open online**, or **Open folder online** and click **Sync** in SharePoint. When OneDrive has synced it, click **Check again**.
- **On \\server, not checked yet**: QuickerPlaces doesn't contact a server named in someone else's file until you say so. If you know the server, click **Check network files**.
- **Not found**: select it and click **Locate…** to point at it. Other files from the same folders are then found too.

Found files are ticked. Change the name or tags if you like, then click **Save session**. Only files found on your PC can be saved, because a session holds paths on your PC; a session with the same name as one of yours is given "(shared)" on the end.

Sessions are saved in `%AppData%\QuickerPlaces\QuickerPlaces\sessions.json`, beside your places, the moment you save, edit, reopen or delete one. They aren't part of **Export places**. If a save fails, the Sessions window says so in red; the change is kept and tried again with the next change and when QuickerPlaces closes.

## Recent Files

**Recent Files is off until you turn it on.** It is to files what Folder Activity (Recents) is to folders: when on, it notes which PDF, Word and Excel files you open, and when, so you can find them again in the **Library**. It is separate from sessions — it never adds anything to a session, and saving a session records nothing here.

Turn it on in the **Library** (the **Library** button in the header), in the **Recent Files** panel at the bottom: tick **Record the PDF, Word and Excel files I open**. From then on, while QuickerPlaces is running (in the window or the tray), it reads Windows' own Recent Items once a minute — and whenever you open the Library — and records the files opened since. It records which file and when; never what's in it, how long you had it open, or which program opened it. Nothing opened before you turned it on, or while it was off, is recorded.

- **Kinds:** untick PDF, Word or Excel to stop recording that kind from now on.
- **Where:** by default, only files under folders you track in Recents are recorded — if you track none, nothing is. Choose **Anywhere** to record files wherever they are.
- **Pause tracking** in the tray menu pauses Recent Files along with folder tracking.
- What Recent Files records is kept for good as part of your activity history (see *Activity history* below); it can't be deleted from QuickerPlaces. Turn Recent Files off to stop recording.

It is best effort: it sees what Windows' Recent Items sees, which covers files opened from Explorer and from most programs' Open dialogs, but a program can skip it, a policy can turn it off, and two opens of the same file within a minute count once. `%LocalAppData%\QuickerPlaces\QuickerPlaces\recent-files.json`, on this computer only and never exported, holds each open for 62 days, and for a year the files you opened (with how many times and when last) and how many opens there were each day. Every open is also kept for good in your activity history.

## Activity history

QuickerPlaces keeps what Folder Activity (Recents) and Recent Files record for good, so you can look back over months and years — a whole career, if you like. Nothing needs turning on: whenever either is recording, its history is kept.

**Where it is.** One file per month in `Documents\QuickerPlaces\History`, named for the month and this PC, such as `2026-09 (DESKTOP-ABC).json`. Each holds that month's folder activity (each tracked folder's time and visits per day, and each day's totals) and the PDF, Word and Excel files opened. The files are plain text, small (well under a megabyte a month, a year in a few megabytes), and are created only once there is something to keep.

**How it is kept.** QuickerPlaces itself keeps only recent activity: the last 62 days of folders and file opens, and a year of daily totals. Once a day, and always before it clears anything older, it saves every day it still holds into the month files, so a day is in your history long before it leaves the app. If the history can't be saved (the folder is read-only, say), nothing is cleared that day, and it tries again the next. A PC that wasn't used for months still saves those months the next time QuickerPlaces starts.

**It can't be deleted from QuickerPlaces.** There is no command that removes history. To stop recording, use **Stop tracking** on a tracked folder, or turn Recent Files off; what was recorded stays. QuickerPlaces never deletes or shortens a month file.

**Looking back.** The year list in Recents and the Library goes back as far as your history does.
- Choose an older year: its strip is shaded from the history.
- Choose a day, week or month in it: its folders (and, in the Library, the files you opened) are listed as for any recent day.
- The Library's list with no date chosen shows recent activity: folders from the last 62 days and files opened in the last year. Older ones appear when you choose their period.
- Days recorded before your history began keeping folder detail (the history started with this version) know only their totals; for those days the app says the folder detail has expired, rather than showing nothing.

QuickerPlaces reads only the months it needs, when you look at them, and lets them go when you move on, so years of history don't make it slower or larger.

**More than one PC.** Each PC writes only the files with its own name, so a History folder shared between PCs (copied across, or synced by OneDrive) never has two PCs writing one file. QuickerPlaces reads every PC's files: a day worked on both shows the time from both, and a folder tracked only on the other PC still counts in the Library's year strip.

**A new PC.** Copy `Documents\QuickerPlaces\History` across (or let OneDrive do it if it backs up your Documents), and your history is there when you track folders again.

**From the command line.** `qp folders recent`, `qp files recent` and `qp activity days` read the history too, so `--days 3650` reaches back ten years.

## The Library

The **Library** (in the header) shows everything QuickerPlaces knows about in one list: your saved places, the files in your sessions, the folders from Recents, and the files from Recent Files. The same thing appears once — a folder you saved and also visit shows as one row, with the name you gave it; a file in two sessions shows once, with both sessions' tags. The **Where from** column says what each row is: *Saved place*, *In Tower B*, *Visited 3 times*, *Opened once*, or several of these.

- **Kind chips** — All, Folders, Links, PDFs, Word, Excel — show one kind, with how many of each there are.
- **Show All, Saved or Recent** — Saved is your places and session files; Recent is Recents' folders and Recent Files.
- **Group by Type, Tag or Folder** — by tag, each session tag gets its own group, a file with two tags appears under both, and anything untagged (including places, which don't have tags) is under **No tag**. Folder grouping uses the selected absolute path level: level 3 puts `C:\X\2023\230108\Plans.pdf` and the `C:\X\2023\230108` folder under **C:\X\2023\230108**. Paths that don't reach that level go under **Shallower than level 3**; links go under **No folder**. **Folder level** remains available for grouping by depth below a tracked Recents folder.
- **Tag** narrows to files in sessions with one tag; **Any tag** shows everything again.
- **Search** matches names, paths, tags and session names.
- **The location pin button below D/W/M** selects the current local day, week or month according to the active **D/W/M** choice and returns the calendar to it. Its tooltip says **Jump to Today**, **Jump to This week**, or **Jump to This month**. Other filters stay in place; clicking it again keeps the selection. Week and month use the current period even when a saved layout is reopened later.
- **The year strip** at the top shades each day by the activity QuickerPlaces recorded — folder visits, files opened, and sessions saved or reopened — for the kind, Show, Tag and Search choices below it. It is a count of those records, not of files used: reopening a session counts once, not once per file. Choose **Day**, **Week** or **Month** beside the year, then click a day to list only what was used that day, week or month. A highlighted **Date filter active** bar above the results shows the selected period. Press **Esc**, click **Clear date filter**, click the same period again, or use the **×** beside it to show all dates while keeping the other filters. With focus in the calendar, **Up/Down** moves one day and **Left/Right** one week in Day mode; in Week or Month mode, every arrow moves one whole period. The calendar follows the selection across months and years. Choosing a period changes the list, not the shading. Links keep no history, only when each was last opened.
- **What the strip can't count** is said under it rather than shown as an empty day. Days recorded before your history began keeping folder detail only know how many visits there were, so while you search or show only Saved, those days show as outlined squares whose tooltip says the folder visits for that filter are no longer known. If Recents tracks no folders, or Recent Files is off or was off for a while, a line says so.
- **An empty list says why:** the period is still to come, nothing was being recorded yet, or nothing was recorded then. A line under the list says what a period can't include — a saved place shows in a period only if that's when you last opened it, and folders on days recorded before your history began keeping folder detail can't be listed.

The Library keeps itself up to date while it's open: new Recent Files opens and Recents activity appear within about half a minute, without losing your place in the list.

Double-click a row, or press Enter, to open it. Opening a saved place here counts exactly as opening it from the main grid (Last Opened and Opens update); opening anything else just opens it. Right-click for **Copy path or link**.

## Recently Deleted

Places you remove aren't deleted straight away. They go to **Recently Deleted**, which you open from **Options** beside **Hide list**. They don't appear in the grid, the cards or search while they're there.

**How long they stay.** At least seven full days: 7 × 24 hours from the moment you removed the place. After that it's deleted for good the next time QuickerPlaces starts or saves a change, so if QuickerPlaces isn't running, a place can stay a little longer than seven days, never less. The **Days remaining** column counts down from **7 days**. It rounds up, so **1 day** means less than 24 hours are left. **Expiring** means the seven days are up but QuickerPlaces hasn't started or saved since, and you can still restore the place until it does. Hover over a **Days remaining** cell to see the exact date and time.

**What it shows.** Name (with the folder or globe icon), Folder or link, **Deleted** (the date and time you removed it, in your local time) and **Days remaining**. The most recently removed place is at the top. Click a column header to sort. Select one place with a click, or several with Shift or Ctrl.

**What you can do:**

- **Restore selected** (**Alt+R**) — puts the selected places back in the grid, each in its old spot in the list, and favourites back in their old place among the cards.
- **Delete selected permanently** (**Alt+D**) — deletes the selected places for good.
- **Empty Recently Deleted** (**Alt+E**) — deletes every place in Recently Deleted for good.
- **Close** — Esc closes the dialog too.

The two permanent actions can't be undone, so both ask first. In that question, **Cancel** is the default: Enter, Space and Esc all back out, and so does closing the question window. Only clicking the button labelled **Delete permanently** or **Empty Recently Deleted** (or tabbing to it and pressing it) goes ahead. Pressing **Delete** in the Recently Deleted list does nothing, so nothing here is one keystroke away from being lost.

**Restore conflicts.** Places in Recently Deleted never block a name or folder or link: as soon as you remove "Docs", you can add a new place called "Docs". If you later restore the old one, it can't come back as it was. **Restore selected** first brings back every selected place that doesn't clash with anything, then shows the **Restore place** dialog for each one that does, one at a time. That dialog explains which place now has its name or folder or link, and lets you change either before clicking **Restore**. **Cancel** leaves that place in Recently Deleted and moves on to the next. If two selected places have the same name, the more recently removed one is restored and the other one goes to Restore place.

If a change made here can't be saved, a line in the Recently Deleted dialog says why, and the unsaved-changes banner is waiting in the main window when you close it. When everything has been removed from the grid, its empty-list hint also reminds you that your removed places are in Recently Deleted.

## Bringing QuickerPlaces up from anywhere

While QuickerPlaces is running, press **Ctrl+Alt+Space** in any app. QuickerPlaces comes to the front (restoring it if it was minimized) with the search box focused, so you can type part of a name and press **Enter** to open it. The subtitle under the app name shows the shortcut when it's active.

Starting QuickerPlaces again while it's already running does the same thing. You only ever get one copy, which also keeps two copies from overwriting each other's saved places.

**Changing the shortcut:** click **Settings** in the header. Click the shortcut box and press the keys you want, for example Ctrl+Alt+Q. It needs at least one of Ctrl, Alt or Win, plus one other key. **Reset to default** goes back to Ctrl+Alt+Space, and **Turn off** (or Backspace in the box) disables it. Click **Save** and the new shortcut works straight away. If another app already uses that combination, Settings tells you and stays open so you can pick another.

If the saved shortcut can't be used when QuickerPlaces starts (another app has taken it since), you'll get a message saying so, and everything else works normally. Pick a new one in Settings.

The shortcut is stored as `"globalHotkey"` in `%LocalAppData%\QuickerPlaces\QuickerPlaces\settings.json`, so you can also edit it there while QuickerPlaces is closed.

## Appearance

Click **Settings** in the header. Under **Appearance**:

- **Theme:** **Light**, **Dark**, or **Match Windows**, which follows the light or dark setting in Windows and changes when you change it there.
- **Highlight colour:** the colour of the Recents, Add folder and Add link buttons, the main button in each dialog, the QP badge in the header, selected rows, switches, and the arrow and underline on the sorted column. Choose **Hull green** (the default), **Steel blue**, **Tail red**, **Cognac**, or **Windows accent**, which follows your Windows accent colour. QuickerPlaces adjusts an accent that would be hard to read.

Your choice shows straight away; **Cancel** puts back what you had, and **Save** keeps it.

## Hiding the grid

If you only want the favourite cards visible, click **Hide list**. The grid collapses and the window shrinks to make room; click **Show list** to bring it back. This state is remembered between sessions, along with the window's size and position.

## Keyboard shortcuts

| Keys | What it does |
|---|---|
| **Ctrl+Alt+Space** (from any app) | Bring QuickerPlaces to the front, ready to search |
| **Ctrl+F** | Jump to the search box |
| **Ctrl+N** | Add folder |
| **Ctrl+U** | Add link |
| **Ctrl+H** | Hide / show the list |
| **Ctrl+Z** | Undo the last Remove (repeat for earlier ones, this session) |
| **Ctrl+1** … **Ctrl+9** | Open favourite card 1–9 |
| **Enter** (in search box) | Open the top result |
| **Down** (in search box) | Move into the grid |
| **Esc** (with a date filter active) | Clear the date filter, keeping the other filters |
| **Esc** (in search box, with no date filter) | Clear the search, or move into the grid if it's already empty |
| **Arrow keys** (in the Library or workspace calendar) | Day mode: Up/Down moves a day, Left/Right a week; Week/Month mode: move a whole period |

These act on the selected grid row, when the grid has keyboard focus:

| Keys | What it does |
|---|---|
| **Enter** | Open |
| **F2** | Rename |
| **Ctrl+E** | Edit folder or link |
| **Ctrl+D** | Toggle favourite |
| **Ctrl+C** | Copy folder or link |
| **Delete** | Remove (moves it to Recently Deleted; the next row is then selected) |

## The unsaved-changes banner

If QuickerPlaces can't write a change to disk — the file's permissions changed, another program is holding it open, the disk is full, and so on — a banner appears at the top of the window instead of the app silently pretending everything is fine.

The banner means exactly one thing: **the change you just made is on your screen but not yet on disk.** Nothing is lost — whatever you added, renamed, edited, favourited, reordered, removed, restored, or permanently deleted stays exactly as you left it in the app. It just hasn't been written to `places.json` yet.

Three buttons on the banner:

- **Retry** — tries the save again. If whatever was blocking it has cleared up (permission restored, the other program closed the file, disk space freed), the banner disappears and your change is now safely on disk.
- **Show data folder** — opens File Explorer to your `places.json` file, in case you want to check permissions, free up disk space, or see what's going on yourself.
- **Show log** — opens the diagnostic log (see below) with whatever program you have associated with `.log` files, so you or someone helping you can see exactly what QuickerPlaces tried and what went wrong.

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

It's capped at 256 KB and rolls over to a second file (`quickerplaces.1.log`) once it fills up, so it can never grow without bound even if something keeps failing while you leave the app running. It never contains any of your place names or the folders or links you've saved — only counts and file paths — so it's safe to share with someone helping you troubleshoot without handing over your actual data.

## Only one QuickerPlaces at a time

QuickerPlaces only allows one running copy per Windows user on a machine. If you try to launch it again while it's already running — from a shortcut, from Explorer, however — the existing window is brought to the front instead of a second copy opening, with the search box ready, just like the global shortcut. (If the window happens to be minimized, it's restored first.) This is what keeps two copies from ever fighting over the same `places.json` file and one silently overwriting the other's changes.

## Exporting places

Choose **Export places** from **Options** to open a checklist of every place in your list, all checked by default. Places in Recently Deleted are never exported. Uncheck anything you don't want to include, then choose where to save the resulting `.json` file. Saving over an earlier export is safe: the new file is written completely before it replaces the old one, so an interrupted export never leaves a half-written file behind. This is the way to back up your list or hand a set of places to someone else running QuickerPlaces.

## Importing places

Choose **Import places** from **Options** and pick a `.json` file that was previously created with Export. QuickerPlaces compares every item in that file against what you already have and silently drops anything that would collide — same name (case-insensitive) or same folder or link (exact match) as something you already saved. You're never shown or asked about those; there's nothing to decide.

What's left — the items that don't collide with anything — is presented as a checklist, all checked by default, exactly like Export. Uncheck anything you don't want, click **Import**, and the selected items are added and written to disk immediately. A short summary tells you how many were brought in.

If everything in the file collides with what you already have, you'll see an empty (or very short) list — that's expected, not an error.

A few more rules:

- Only places in your list count as collisions. A place in the file whose name or folder or link matches only something in your Recently Deleted is still offered. (Restoring that old one later then goes through the Restore place dialog.)
- Removed places in a file are skipped. Exports never contain them, but a copied `places.json` can.
- Each imported place keeps its Last Opened, Opens and the date it was first added, as they were in the file, so exporting and importing again loses nothing but favourite status. Exports from versions before Last Opened existed import as never opened.
- Exports made by older versions of QuickerPlaces still import.
- A file exported by a newer version of QuickerPlaces is refused with "That file was exported by a newer version of QuickerPlaces. Update QuickerPlaces to import it." Nothing is offered from it.

## Where your data lives

QuickerPlaces keeps a few small plain-text files, all safe to open in a text editor if you're curious or want to back them up manually:

- **Your places:** `%AppData%\QuickerPlaces\QuickerPlaces\places.json` — written the instant anything changes. It holds Recently Deleted too.
- **A backup of the previous version:** `%AppData%\QuickerPlaces\QuickerPlaces\places.bak.json` — QuickerPlaces keeps the previous contents of `places.json` every time it saves, automatically, right next to it. You don't need to do anything to get this; it's just there as an extra safety net.
- **Window layout** (size, position, whether the grid is collapsed, how it's sorted), the **global hotkey**, and the tray/startup switches: `%LocalAppData%\QuickerPlaces\QuickerPlaces\settings.json` — saved when the window closes, and straight away when you change Settings.
- **Project sessions:** `%AppData%\QuickerPlaces\QuickerPlaces\sessions.json`, beside `places.json`, with its previous version as `sessions.bak.json` — written the instant you save, edit, reopen or delete a session. Copy it along with `places.json` to take your sessions to another machine.
- **Recent Files settings and recent opens:** `%LocalAppData%\QuickerPlaces\QuickerPlaces\recent-files.json` — the last 62 days of opens and a year of the files you opened and daily counts; older opens are in your activity history. It stays on this computer, is never exported, and exists only once you've turned Recent Files on.
- **Folder Activity roots and recorded time:** `%LocalAppData%\QuickerPlaces\QuickerPlaces\activity.json` — stays on this computer and is separate from a Places export.
- **Activity history:** `Documents\QuickerPlaces\History`, one file per month per PC — folder activity and Recent Files, kept for good (see *Activity history*). Copy the folder to keep your history when you change PCs; history written by several PCs in the same folder is kept apart by the PC's name in each file name. QuickerPlaces never deletes from these files: stopping tracking or turning Recent Files off leaves past months as they are.

Last Opened and Opens are stored with each place in `places.json`, so they go wherever that file goes. The sort is in `settings.json`, which stays on this computer.
- **The diagnostic log:** `%LocalAppData%\QuickerPlaces\QuickerPlaces\logs\quickerplaces.log` — see "Where the log lives" above.

You never need to touch any of these files by hand, but if you ever want to move your places to another machine, copying `places.json` across is all it takes (Recently Deleted goes with it). **Places file** in **Options** opens the folder that holds `places.json` in File Explorer, with the file selected. The backup and any set-aside damaged files (see "The startup recovery prompt") are in the same folder.

**Upgrading from a version without Recently Deleted.** This version stores `places.json` in a new format: dates are kept in UTC (the grid still shows your local date), and removed places are marked rather than deleted. The first time it starts, it converts your existing file in memory and writes nothing. The converted file is saved with your first change, and right after that save `places.bak.json` is your pre-upgrade file, until the next save replaces it. From then on, an older version of QuickerPlaces that has the startup recovery prompt says the file was written by a newer version, and leaves it untouched. A version from before the recovery prompt existed doesn't check: it would show the places in Recently Deleted as ordinary places, and forget they were removed the next time it saved. Don't go back to one of those with this file.

**Upgrading from a version without Last Opened.** The same happens again: the file is converted in memory when QuickerPlaces starts and saved in the new format with your first change, usually the first place you open. Every place starts as never opened. Right after that first save `places.bak.json` is your pre-upgrade file, and any earlier version of QuickerPlaces with the recovery prompt, including the one with Recently Deleted, says the file was written by a newer version and leaves it untouched.

**Upgrading to the version with activity history.** `recent-files.json` moves to a new format that keeps 62 days of opens instead of a year. The first time it starts, this version saves all of your existing opens into your activity history, works out the new file in memory, and writes it with the next file you open or setting you change. After that, an older QuickerPlaces says Recent Files was saved by a newer version and keeps Recent Files off; `activity.json` is unchanged and still works in an older version.

## If something goes wrong

- **First launch, or a missing places file:** QuickerPlaces just starts with an empty list — this is normal, not an error, and your first **Add folder**/**Add link** creates the file.
- **A places file that can't be loaded** (damaged, held open by another program, or from a newer version of the app): see "The startup recovery prompt" above — QuickerPlaces asks you what to do rather than guessing, and it never touches your file except when you explicitly choose to start fresh from a genuinely damaged one.
- **A save that doesn't go through:** see "The unsaved-changes banner" above — your change stays visible and nothing is lost; the banner tells you and lets you retry.
- **Removed something by mistake:** press Ctrl+Z (while QuickerPlaces is still open), or restore it from Recently Deleted within seven days.
- **A place that won't open:** you'll get an on-screen message explaining why (folder no longer exists, link is malformed, etc.) rather than the app freezing or closing.

If you hit anything not covered here, or something that looks like an actual crash, that's worth reporting rather than working around — see `ai/BUILD_SUMMARY.md` for the project's current known-issues status.

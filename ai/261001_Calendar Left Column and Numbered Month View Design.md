# Calendar in the left column, and a numbered month view — design

Date: 2026-10-01. Status: Draft for review. Builds on the [Desk layout design](260929_Desk%20Layout%20and%20Recents%20Panel%20Design.md) (branch `ccr-6156d37a-mo223d`).

## 1. Outcome

- ~~The built-in **Desk** keeps the Year activity calendar along the bottom of the main column, under Files.~~ **Superseded the same day at the user's request: Desk version 3 puts Activity at the bottom of the left column** (see BUILD_SUMMARY). The note about a saved arrangement still holds for anyone who personalised Desk from version 2.
- The calendar can be **moved to the left column** with Arrange (Settings → Customise layout… → Arrange: the Column choice, or drag). Nothing new is added for the move itself.
- In the left column, and anywhere else the panel is too narrow for the year, it shows the **month view**, with the **day number in each day's cell**.
- A calendar saved in the left column shows the month view **on startup**, not only after being moved in the same session.

Out of scope: a corner or floating calendar (dropped), a button on the calendar to move it, a different month layout (the weekday rows and week columns stay), numbers on the year strip.

## 2. Month view on startup

`YearActivityPanel.FitWidth` chooses year or month by comparing the panel's width with `LibraryViewModel.CalendarStripWidth`. That is 0 until the activity data has loaded (`BuildCalendar`), and `FitWidth` runs only on load and resize. A panel that is already narrow at startup compares against 0, picks the year strip, and never looks again.

- The panel re-runs `FitWidth` when `CalendarStripWidth` changes (it listens to the DataContext's `PropertyChanged`, and re-subscribes when the DataContext changes).
- With no strip yet (width 0) there is no year to show, so the panel shows the month view whatever its width, and switches to the year once the strip exists and the panel is wide enough.
- The comparison moves into a small pure function, `YearActivityPanel.ShowsMonthView(panelWidth, stripWidth)`, so it is tested without WPF.

## 3. Numbered days

- The month view's cells show the day of the month (1–31) instead of being blank squares. The year strip is unchanged.
- A new style, `Button.CalendarDayNumbered`, based on `Button.CalendarDay`: same size, heat colours, today outline, selection and out-of-range hiding, plus the number, centred, in about 8 px type (about 12 px after the month view's 1.5× scale).
- The number's colour follows the cell: the primary text colour on the light heats and untracked cells, a light colour on the dark heats (3 and 4), so it reads in Light and Dark. Colours come from existing theme resources; if none fits, one is added to both themes.
- The cell's tooltip and automation name stay the existing label (date and activity), so screen readers do not read bare numbers.
- Only `Button.CalendarDayNumbered` is used in the month view. The strip keeps `Button.CalendarDay`.

## 4. Testing

UI-free (`QuickerPlaces.Tests`):
- `ShowsMonthView`: strip width 0, narrow or wide (month); real width, narrow (month); real width, wide (year).

On Windows, with `--workspace --data-root <scratch>`, UI Automation and real clicks (see the real-click testing memory note):
- Save a layout with the calendar in the left column, restart: the month view shows, with numbers, from the start.
- Reset to Desk: the strip along the bottom, no numbers on it.
- Move the calendar left in Arrange and back: the view follows the width.
- Both themes: numbers readable on every heat level, today and the selected day.
- A 30- and a 31-day month, and a month starting on each end of the week, fit the cells.

## 5. Order of work

1. `ShowsMonthView` and the re-check on `CalendarStripWidth` (test first).
2. `Button.CalendarDayNumbered` and its use in the month view.
3. The real-click checks above; a line in `BUILD_SUMMARY.md`.

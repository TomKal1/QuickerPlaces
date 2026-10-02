The list of saved places: a table on `Bg-Panel` with a `Bg-Raised` header row, row dividers instead of stripes, and a `Border-Default` outline with `radius-8`.

- **Columns:** Name (2.4*, type icon + name + star), Folder or link (3*, `path` style in IBM Plex Mono), Last opened (1.45*, regional date format), Opens (0.6*, centred). There is no Type column: the folder or globe icon already says it. Widths are proportional so the table scales with the window.
- **Header:** 34px; labels in `label-column` capitals, `Text-Secondary`. The sorted column shows its label in `Text-Primary`, a `Highlight-Text` arrow, and a 2px `Highlight-Text` underline, so both follow the highlight choice.
- **Rows:** 36px, 12px side padding, `Border-Default` divider, long text ends in "…". Names are `body-strong` (15px), a step larger than the other columns; paths are `Text-Secondary`.
- **Hover:** `Bg-RowHover`. **Selected:** `Highlight` fill with `On-Highlight` text and icons (secondary columns at 85%).
- **Star:** a filled `Signal` star follows a favourite's name. On a hovered or selected row that isn't a favourite, an outline star in `Text-Tertiary` (on selection, `On-Highlight` at 80%) offers to add it; clicking toggles, as does Ctrl+D.
- **Type icon:** folder or globe, 16px, `Highlight-Text`, 9px before the name.
- **Keyboard:** Enter opens, F2 renames, Ctrl+E edits the folder or link, Ctrl+D toggles favourite, Delete removes.
- **The consumer provides** the places and the sort. Rows in the preview are examples.
- The same table style serves Recents, Recently Deleted, Import and Export.

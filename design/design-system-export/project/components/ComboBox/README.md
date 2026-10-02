A dropdown picker for a longer list of choices, such as the roll-up in Folder tracking settings. Restyled here to match the Saab direction; it was not drawn on the canvas.

- **Closed:** 34px, `Bg-Raised`, `Border-Strong` outline, `radius-6`, the value in `body` and a chevron in `Text-Secondary`. Hover turns the outline `Highlight-Text`.
- **Open:** a `Bg-Raised` menu 4px below, `Border-Strong` outline, `radius-8`, 4px padding, no shadow. Items are 30px with `radius-6`: the chosen one fills `Highlight` with `On-Highlight` text; the one under the pointer fills `Bg-RowHover`.
- **Prefer a `SegmentedControl`** when there are three or fewer choices.
- **The consumer provides** the items and the bound selection. The option names in the preview are placeholders.

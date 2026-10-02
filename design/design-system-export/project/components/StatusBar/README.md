A short confirmation at the bottom of a window after Remove, Undo, Copy or a restore.

- **Look:** 38px, `Bg-Raised`, `Border-Default` outline, `radius-8`, 10px above the table.
- **Contents:** a tick in `Highlight-Text`, the message in `body` (one line, ending in "…"), an optional compact Undo `Button` with a `Ctrl Z` keycap, and a compact icon-only dismiss button.
- **Timing:** hides 10 seconds after a message that offers Undo, 8 seconds after any other; stays while the pointer or keyboard focus is inside it. It never takes focus when it appears.
- **Wording:** say what happened and where it went ("It stays in Recently Deleted for 7 days.").
- **The consumer provides** the message, whether Undo is offered, and the commands.

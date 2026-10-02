The 36px round badge that opens a message dialog and tells its kind at a glance. Restyled to match the Saab direction; it was not drawn on the canvas.

- **Look:** a `Bg-Raised` circle with a 2px outline and one bold 16px character, both in the kind's colour: Info "i" in `Info`, Warning "!" in `Signal`, Error "×" in `Danger`, Question "?" in `Highlight-Text`.
- **Why outlined:** version 1 put white characters on bright fills, which failed contrast (1.7–2.8:1). Coloured characters on `Bg-Raised` clear 4.3:1 or better in both themes, well above the 3:1 needed for an icon-like mark.
- **Layout:** the message sits 14px to the right in 14px text, wrapping.
- **The consumer provides** the kind and the message. The message in the preview is a placeholder.

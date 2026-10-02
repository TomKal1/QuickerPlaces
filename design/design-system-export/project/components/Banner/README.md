A message pinned above a window's content when something needs attention. Restyled to match the Saab direction; it was not drawn on the canvas.

- **Look:** `Bg-Raised`, `radius-8`, 10px padding (14px on the left), a 1px outline in the status colour, a 16px icon in the same colour, and the message in `Text-Primary` (the colour sits on the outline and icon, not the text, so the text stays readable).
- **Warning** (unsaved changes): `Signal` outline and triangle icon; actions Retry (compact `PrimaryButton`) and Show data folder.
- **Error:** `Danger` outline and circle icon; action Retry save.
- **Behaviour:** takes no space when hidden and never takes focus when it appears.
- **The consumer provides** the message and actions. The wording in the preview is a placeholder.

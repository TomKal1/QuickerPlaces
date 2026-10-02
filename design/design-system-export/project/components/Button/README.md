The standard button for every action that isn't a main one (Settings in the header, Cancel, Hide list and the rest): `Bg-Raised` fill, `Border-Strong` outline, `radius-6`, `button` text.

- **Sizes:** standard 34px tall with 14px side padding; compact (`--sm`) 28px with 10px padding and 13px text, for rows inside panels, tables and the status bar; icon-only 34 × 34 or 28 × 28.
- **Icon:** optional 16px stroke icon 7px before the label ("Add folder" with a plus).
- **Shortcut:** a compact button may carry a keycap after its label ("Undo" + `Ctrl Z`).
- **States:** hover turns the outline `Highlight-Text`; keyboard focus draws a 2px `Signal` ring 2px outside; disabled shows `Text-Tertiary` on a `Border-Default` outline.
- **The consumer provides** the label in sentence case, the command, and an `aria-label` / `AutomationProperties.Name` for icon-only buttons.
- **Don't** use it for a main action: that is `PrimaryButton`.

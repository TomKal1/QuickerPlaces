The Recents year strip: one 11px square per day in week columns, shaded in leather tones by how often tracked folders were visited.

- **Cells:** 11 × 11, `radius-2`, 2px apart (13px pitch). `Heat-0` (with a `Border-Default` outline) for a tracked day with no visits, `Heat-1` to `Heat-4` for more visits, `Heat-Untracked` before tracking began, a dashed `Border-Default` outline for days after today.
- **Today** gets a 1.5px `Signal` ring. **The period shown** in the table below is outlined in `Highlight-Text` (a whole week column in Week mode).
- **Labels:** Mon / Wed / Fri and month names in the `calendar` style, `Text-Tertiary`.
- **Panel:** `Bg-Panel`, `Border-Default`, `radius-10`, 12 × 16 padding. Its top row holds a "Year activity" `label-caps` label, a compact `SegmentedControl` (Week / Month / Day), a "Fewer visits … More" legend, previous/next icon buttons, and the year.
- **Visits bars** in the table below use `Leather` on a `Border-Default` track, 70 × 8px, `radius-2`.
- **The consumer provides** each day's intensity and the selected period. The preview shows example data for 20 weeks; the real strip shows the whole year.

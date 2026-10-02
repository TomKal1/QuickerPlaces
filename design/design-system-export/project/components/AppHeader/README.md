The top of a QuickerPlaces window: the header with monogram, name, subheader and main actions, then the racing stripe.

- **Monogram:** 40 × 40, `Highlight`, `radius-8`, a faint inner edge and a 1px `Trim-Line` ring; "QP" in TASA Orbiter 800, 16px, `On-Highlight`. It follows the highlight choice; the title bar shows the app icon, which stays `Brand-Green`.
- **Name and subheader:** 14px right of the monogram. The name uses `title-window`; the subheader is `body` in `Text-Secondary`, one line, ending in "…" if it doesn't fit. It mentions the global shortcut only when one is set.
- **Actions,** right-aligned 8px apart: Recents (a `PrimaryButton` with a clock icon and a 7px `Signal` dot, ringed in `Highlight`, when there is new activity), Add folder and Add link (`PrimaryButton`s), then Settings (a `Button` with the sliders icon).
- **Racing stripe:** two full-width 4px bands, `Trim-Stripe` (silver in dark, near-black in light) over `Highlight`, 16px below the header and 14px above what follows. Every window has one under its header; it bleeds past the window padding to both edges.
- **Window padding:** 18px top, 20px sides, 16px bottom.
- **The consumer provides** the app name, subheader text and commands.

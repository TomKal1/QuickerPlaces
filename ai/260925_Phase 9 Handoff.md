---
title: QuickerPlaces — Hand-off, 2026-09-25 (start Phase 9)
status: current as of 2026-09-25 — supersede with a new dated hand-off rather than editing this one
created: 2026-09-25
parent: ai/260901_Professional Improvements Plan.md
supersedes: ai/260925_Phase 3 Handoff.md (for where to work and what next; its §5 constraints still apply)
---

# Hand-off — start Phase 9, step 1

## 1. Where to work

- **Branch `claude/phase-9-folder-activity`**, taken from `main` at `556497c` (PR #7, Phase 3 merged), plus three commits: the WWTools direction in roadmap §4.21, the roadmap reorder (§1.1), and the Phase 9 plan refresh. **Not pushed.** The user sometimes pushes and merges PRs themselves, so check `git log` and the remote before assuming anything.
- **Phase 9 is next** (roadmap §1.1; the release split was withdrawn). The plan is [`260914_Folder Activity Tracking Plan.md`](260914_Folder%20Activity%20Tracking%20Plan.md). Read **§0.1** first (the 2026-09-25 refresh and the user's answers), then **§4** (D1–D26), then **§5.1**, **§7** and **§9**.
- State: all 329 tests pass; the solution builds with 0 warnings on Windows.

## 2. Step 1 — what to build (plan §9, step 1)

Pure logic only: **no UI, no store, no COM, no P/Invoke.** Everything new is UI-free and linked into `QuickerPlaces.Tests` like `PlacesService`, so it compiles on Linux too.

- `Services/Activity/`: `IShellWindowProbe`, `ShellWindowSnapshot` (`record(string Path, nint Hwnd, bool IsForeground)`), `IUserPresence`, `IMonotonicClock`, `RootPathMatcher`, and `FolderActivityTracker`.
- The tracker takes the probe, presence, the monotonic clock, and a `TimeProvider` for the local day (D19). It emits attributed intervals: root, folder, local date, seconds, and a visit when one starts. The store in step 2 consumes them, so keep the output a plain record or event, not a store call.
- Tests (plan §7, first list): dwell threshold (D9), foreground-only and idle (D7), lock, suspend and clock-gap discard, and the 2× poll cap (D6), the three rollups including root and drive-root cases (D8), dropped non-filesystem, relative and outside-root paths (D13), ambiguous tabs (D14), case- and separator-insensitive matching with equivalent prefixes, stored under the root's own path (D15, D22), and the midnight split and daylight-saving days (D19).
- Fakes: scripted `FakeShellWindowProbe`, `FakeUserPresence` and `FakeMonotonicClock` in `QuickerPlaces.Tests/Fakes/`. Use the existing `ManualTimeProvider` and `TestZones` (`CentralEuropean` has the DST rule).
- Work test-first, as Phase 3 did: write the failing test, watch it fail, then implement.

## 3. Conventions and traps

- **One commit per coherent piece**, message in the repo's style (what, and why), ending with the session's attribution lines. Don't push unless asked.
- **No test reads the machine's clock or zone.** Wall-clock decisions go through the injected `TimeProvider`; durations through `IMonotonicClock`.
- **Paths in tests:** the suite runs on Linux too, where `\` is not a separator. Write Windows paths as literal strings and never pass them to `Path.*` helpers in code the tests exercise. `RootPathMatcher` must split on both `\` and `/` itself; see `Services/AliasSuggestion.cs` for the pattern.
- **Editing files on Windows:** in Git Bash, Python heredocs receive `\\` as `\`, so C# `\n` escapes turn into real line breaks. Use the Edit and Write tools for any change that contains a backslash.
- **Never run the app against the real data folder.** `places.json` and `settings.json` have fixed `%AppData%`/`%LocalAppData%` paths with no override. Visual checks can use the offscreen WPF render technique from Phase 3's session: load `Theme.xaml` into a `new Application().Resources` in a PowerShell `-STA` script and `RenderTargetBitmap` a window.
- Phase 3 hand-off §5 still applies. In particular, `PlaceLauncher` is the only launch path and `RecordOpen` the only usage writer, and Phase 9 must not feed QuickerPlaces' opens into its store (D23).

## 4. After step 1

Step 2 is the store (`activity.json`: configuration written at once and reported on failure, buffered activity, 62-day detail plus 365-day totals, quarantine). Step 3 is the late-bound COM probe and the **performance measurement gate**, which the user has to support by running the app through a normal working day. Tell them before step 3 starts.

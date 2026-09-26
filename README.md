# QuickerPlaces

A lightweight Windows desktop utility for storing and quickly opening remembered "places" — folder paths and URLs — under a memorable alias. Part of the **QuickerLinks** project: a better path launcher than Quick Links.

> **Status:** early build. The features (search, hotkey, undo, copy, settings and the rest) have had a hands-on pass on Windows. Phase 1 of the [roadmap](ai/260901_Professional%20Improvements%20Plan.md) (persistence reliability and recovery: failed saves are reported with a Retry banner, a damaged or unreadable store is handled safely, and only one instance runs at a time) is merged in, and so is Phase 2 (a seven-day Recently Deleted: Remove no longer asks, and removed places can be restored for a week). Both were checked by hand on Windows, apart from their failure paths, which were accepted as untested. Phase 3 (Last Opened and Opens columns, and a remembered sort) is implemented on its branch, not yet merged. It builds with no warnings and all 311 automated tests pass, but **it has not yet been run or checked by hand**. See `ai/BUILD_SUMMARY.md` for the checklists, and `ai/260925_Phase 3 Handoff.md` for where the roadmap stands.

## What it does

- Save a folder path or a URL under a unique **alias**, with validation that blocks duplicate aliases and duplicate paths/URLs before they're saved.
- Browse everything in a sortable grid — right-click a row for **Open**, **Copy Path/URL**, **Rename Alias**, **Edit Path/URL**, **Toggle Favourite**, or **Remove**; double-click to open. Removed something by mistake? **Undo** (or Ctrl+Z) puts it back, and **Recently Deleted** keeps it for seven days.
- **Search** the grid as you type, by alias or path/URL. Press Enter to open the top result, so the search box doubles as a quick launcher.
- Press **Ctrl+Alt+Space** from any app to bring QuickerPlaces to the front with the search box ready, type a few letters, and press Enter to open. Launching it again does the same thing rather than opening a second copy. Change the shortcut in **Settings** (the gear icon).
- Drive everything from the keyboard: Ctrl+F search, Ctrl+N / Ctrl+U add, Ctrl+1–9 open a favourite, plus Enter / F2 / Ctrl+E / Ctrl+D / Delete on the selected row. The full list is in [`USERGUIDE.md`](USERGUIDE.md#keyboard-shortcuts).
- Pin your most-used places as one-click **favourite bubbles** above the grid, drag-and-drop to reorder them, and collapse the grid entirely when you just want the bubbles.
- Everything is written to disk immediately as you work — no save button. If a save ever fails, the change stays on screen and a banner says so, with a Retry that rewrites it; QuickerPlaces never claims a change is stored when it isn't.
- **Export** any subset of your places to a JSON file to share or back up, and **import** from one — anything that would collide with what you already have is filtered out automatically, before you're ever asked to pick.

## Getting started

**Requirements:** Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/), and either Visual Studio 2022+ (with the .NET desktop development workload) or the `dotnet` CLI.

```
git clone <this-repo-url>
cd <repo>/src
```

Then either open `QuickerPlaces.sln` in Visual Studio and run, or from the command line:

```
dotnet build QuickerPlaces.sln
dotnet run --project QuickerPlaces
```

### Running the tests

```
dotnet test QuickerPlaces.sln
```

The tests (`src/QuickerPlaces.Tests`) cover the service layer and the UI-free view models behind the Recently Deleted dialog: validation, persistence, the schema migration, favourites, Recently Deleted and its expiry, export/import, and the search matching rule. They target plain `net10.0`, so they run on any OS. Building the WPF app itself on a non-Windows machine needs `-p:EnableWindowsTargeting=true`.

### Where your data lives

- Your saved places: `%AppData%\QuickerPlaces\QuickerPlaces\places.json` — written through on every change (add, edit, favourite, reorder, remove), not just on exit. The previous version is kept alongside it as `places.bak.json` on every save.
- Window layout (size/position, whether the grid is collapsed) and the global hotkey: `%LocalAppData%\QuickerPlaces\QuickerPlaces\settings.json`.
- Diagnostic log (save failures, load/recovery outcomes — never place aliases or paths): `%LocalAppData%\QuickerPlaces\QuickerPlaces\logs\quickerplaces.log`.

All plain text and safe to inspect, back up, or hand-edit if you know what you're doing. The folder icon in the app's header opens the places folder in File Explorer.

## Repository layout

```
.
├── src/                     # the actual application
│   ├── QuickerPlaces.sln
│   ├── QuickerPlaces/       # WPF project (App, Models, ViewModels, Views, Services, ...)
│   └── QuickerPlaces.Tests/ # xUnit tests for the services and models (no UI tests)
└── ai/                      # how this was built, and why
    ├── 260831_Raw brief for SI.txt   # the original one-paragraph request
    ├── 260831_Initial SI brief.md    # the spec/requirements handoff built from it
    └── BUILD_SUMMARY.md              # what got built, decisions made, bugs found & fixed
```

This project was scaffolded and largely written with Claude, working from the spec in `ai/260831_Initial SI brief.md` against an existing WPF starter template. `ai/BUILD_SUMMARY.md` has the full story — the decisions made resolving the spec against the template, and every bug found and fixed along the way — for anyone (human or AI) picking this back up later.

## Tech

WPF on .NET 10, hand-rolled MVVM (no external MVVM package), `System.Text.Json` for persistence, no third-party dependencies.

# QuickerPlaces

A lightweight Windows desktop utility for storing and quickly opening remembered "places" — folder paths and URLs — under a memorable alias. Part of the **QuickerLinks** project: a better path launcher than Quick Links.

> **Status:** early build. It compiles cleanly, and the service layer (validation, persistence, favourites, import/export) is covered by unit tests. The UI still hasn't had a full hands-on pass through every feature, so expect rough edges. See [`ai/BUILD_SUMMARY.md`](ai/BUILD_SUMMARY.md) for the bugs found and fixed so far.

## What it does

- Save a folder path or a URL under a unique **alias**, with validation that blocks duplicate aliases and duplicate paths/URLs before they're saved.
- Browse everything in a sortable grid — right-click a row for **Open**, **Rename Alias**, **Edit Path/URL**, **Toggle Favourite**, or **Remove**; double-click to open.
- **Search** the grid as you type, by alias or path/URL. Press Enter to open the top result, so the search box doubles as a quick launcher.
- Drive everything from the keyboard: Ctrl+F search, Ctrl+N / Ctrl+U add, Ctrl+1–9 open a favourite, plus Enter / F2 / Ctrl+E / Ctrl+D / Delete on the selected row. The full list is in [`USERGUIDE.md`](USERGUIDE.md#keyboard-shortcuts).
- Pin your most-used places as one-click **favourite bubbles** above the grid, drag-and-drop to reorder them, and collapse the grid entirely when you just want the bubbles.
- Everything is written to disk immediately as you work — no save button, no "unsaved changes."
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

The tests (`src/QuickerPlaces.Tests`) cover the service layer only: validation, persistence, favourites, export/import, and the search matching rule. They target plain `net10.0`, so they run on any OS. Building the WPF app itself on a non-Windows machine needs `-p:EnableWindowsTargeting=true`.

### Where your data lives

- Your saved places: `%AppData%\QuickerPlaces\QuickerPlaces\places.json` — written through on every change (add, edit, favourite, reorder, remove), not just on exit.
- Window layout (size/position, whether the grid is collapsed): `%LocalAppData%\QuickerPlaces\QuickerPlaces\settings.json`.

Both are plain JSON and safe to inspect, back up, or hand-edit if you know what you're doing. The folder icon in the app's header opens the places folder in File Explorer.

## Repository layout

```
.
├── src/                     # the actual application
│   ├── QuickerPlaces.sln
│   ├── QuickerPlaces/       # WPF project (App, Models, ViewModels, Views, Services, ...)
│   └── QuickerPlaces.Tests/ # xUnit tests for the service layer
└── ai/                      # how this was built, and why
    ├── QuickerPlaces-SI.md  # the original spec/requirements handoff
    └── BUILD_SUMMARY.md     # what got built, decisions made, bugs found & fixed
```

This project was scaffolded and largely written with Claude, working from the spec in `ai/QuickerPlaces-SI.md` against an existing WPF starter template. `ai/BUILD_SUMMARY.md` has the full story — the decisions made resolving the spec against the template, and every bug found and fixed along the way — for anyone (human or AI) picking this back up later.

## Tech

WPF on .NET 10, hand-rolled MVVM (no external MVVM package), `System.Text.Json` for persistence, no third-party dependencies.

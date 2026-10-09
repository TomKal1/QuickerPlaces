# qp: QuickerPlaces from the command line

`qp` reads and updates QuickerPlaces' stores from a terminal, a script or an
AI agent (Odysseus, Claude Code, Codex). It is built for agents first:

- **One JSON document per run** on stdout, always in the same envelope.
- **Stable exit codes and error codes**, so a caller branches without
  parsing messages.
- **Self-describing:** `qp describe` (or just `qp`) prints every command,
  option, error code and exit code as JSON. Point an agent at it.
- **Same rules as the app:** it compiles the app's own services, so
  migrations, validation (duplicate aliases or folders) and retention match
  exactly.

## Build and run

```powershell
dotnet build src/QuickerPlaces.Cli          # builds qp.dll (and qp.exe on Windows)
dotnet run --project src/QuickerPlaces.Cli -- places top --period week --pretty
```

It targets plain `net10.0`, so it builds and runs on any OS (the tests run it
on Linux). Opening a place uses the system's default handler.

## Commands

| Command | Writes | What it does |
|---|---|---|
| `describe` | | The full catalogue as JSON. Start here. |
| `status` | | Store paths, whether each loads, whether the app is running. |
| `places list` | | Saved places; `--tag`, `--favourites`, `--deleted`, `--sort alias\|opens\|last-opened\|added`. |
| `places find <text>` | | Every word must appear in the alias, folder/link, a tag or the note. |
| `places get <ref>` | | One place in full, with its timed opens. |
| `places top` | | Most-opened places in `--period day\|week\|month\|year\|all` or `--days N`. |
| `places open <ref>` | ✓ | Opens it and records the open (`--dry-run` to only resolve). |
| `places add` | ✓ | `--alias` and `--folder` or `--url`; optional `--tags`, `--note`. |
| `places tag <ref>` | ✓ | `--set`, `--add`, `--remove` (comma-separated). |
| `places note <ref>` | ✓ | `--text` or `--clear`. |
| `sessions list` / `sessions get <ref>` | | Project sessions and their files. |
| `sessions open <ref>` | ✓ | Opens the session's files (or `--file` ones) and records the reopen; lists missing files. |
| `sessions save` | ✓ | `--name`, `--tags`, and `--file` once per file. |
| `files open` | | PDF, Word and Excel files open now (Windows), with folders, containing places and a suggested name. |
| `files recent` | | PDF, Word and Excel files opened in a period (Recent Files, and the activity history for older days). |
| `folders recent` | | Time spent per folder in a period (Recents, and the activity history for older days), each linked to its saved place. |
| `activity days` | | Day by day: places opened, sessions reopened, files opened, folders worked in. Folders and files reach back through the activity history. |
| `revit info <file>` | | The Revit release a `.rvt`, `.rfa` or `.rte` file was saved in, and whether it is a central or a local. Read without Revit. Revit 2022 and later; older files give `problem: "tooOld"`. |
| `revit dialogs <pid>` | | Every top-level window of a running process (a `Revit.exe`): class, enabled, visible, title, static texts and buttons, and which count as dialogs Revit is waiting on. Read-only; nothing is pressed. `--release <year>` recognises a security prompt from recorded signatures. Windows only. |

`<ref>` is an alias (any case) or an id. Every command also takes
`--data-root <folder>` (or the `QUICKERPLACES_DATA_ROOT` variable) to use
test stores, as the app's own `--data-root` does, and `--pretty`.

## Output

```json
{"ok":true,"apiVersion":1,"command":"places top","data":{"period":{"name":"week","from":"2026-09-25","to":"2026-10-01"},"total":1,"places":[{"alias":"Tax 2026","opensInPeriod":4, "...": "..."}]}}
{"ok":false,"apiVersion":1,"command":"places get","error":{"code":"not_found","message":"No place is called \"tax\".","details":{"suggestions":["Tax 2026"]}}}
```

Times are ISO 8601 UTC. Period dates are local `yyyy-MM-dd`. Within an
`apiVersion`, fields are only ever added.

| Exit | Error code | Meaning |
|---|---|---|
| 0 | | Success |
| 2 | `usage` | Bad command, option or value |
| 3 | `not_found` | No such place or session; `details.suggestions` may help |
| 4 | `invalid`, `ambiguous` | The change breaks a rule, or the reference matches several items |
| 5 | `app_running` | QuickerPlaces is open but didn't take the change; nothing was written |
| 6 | `store_unavailable`, `save_failed` | A store is damaged, unreadable or from a newer version, or the disk refused the save |
| 7 | `open_failed` | The place or session couldn't be opened (`details.status`: `missing` or `failed`) |
| 8 | `unsupported` | Not possible on this system (`files open` needs Windows) |
| 1 | `internal` | A bug |

## Safety beside the app

The app keeps its stores in memory and writes all of a store on every
change, so nothing else may write those files while it runs. So:

- **Reads always work** and never change a file. Stores are opened read-only.
  An older store is migrated in memory only, and a damaged one is reported,
  never set aside.
- **While QuickerPlaces is open, changes go through it.** qp sends each one
  over a named pipe that only your Windows user can open. The app runs it on
  the data it holds, saves it and shows it at once. `status` reports
  `"changesGoTo": "app"`.
- **While it is closed, qp writes the files itself**, using the same
  operations (`Services/Remote/StoreOperations.cs`), so the rules are
  identical either way.
- If the app is open but doesn't answer (it is closing, or it is older than
  qp), the change is refused with `app_running` and nothing is written.
  `places open` and `sessions open` still open things then; they report
  `"recorded": false`.

## Example: save what's open as a session

```
qp files open                      # files, their folders and places, and a suggestedName
qp sessions save --name "Acme audit 2026-10-01" --tags acme,audit \
   --file "D:\Clients\Acme\Audit\Report.pdf" --file "D:\Clients\Acme\Audit\Figures.xlsx"
```

Finding open files is a best guess (window titles, files programs hold
open, Windows' Recent Items), so an agent should show the list, and the
name it chose, before saving.

## Notes for agent integrations

- Call `qp describe` once and keep the catalogue as the tool definition. An
  MCP server can wrap each command as one tool, with no logic of its own.
- `openCount` is the lifetime total. `opens` (from `places get`) and
  `opensInPeriod` count only timed opens, kept since schema v4 and capped at
  500. `opensTimed` says how many of `openCount` have a time.
- `exists` reports whether a folder or file is there now. It is `null` for
  links.

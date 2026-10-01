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
| `files recent` | | PDF, Word and Excel files opened in a period (Recent Files). |
| `folders recent` | | Time spent per folder in a period (Recents), each linked to its saved place. |

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
| 5 | `app_running` | QuickerPlaces is open, so writes are refused |
| 6 | `store_unavailable`, `save_failed` | A store is damaged, unreadable or from a newer version, or the disk refused the save |
| 7 | `open_failed` | The place couldn't be opened (`details.status`: `missing` or `failed`) |
| 1 | `internal` | A bug |

## Safety beside the app

The app keeps places.json in memory and writes all of it on every change, so
anything another process wrote would be overwritten. So:

- **Reads always work** and never change a file. Stores are opened read-only.
  An older store is migrated in memory only, and a damaged one is reported,
  never set aside.
- **Writes are refused (`app_running`) while QuickerPlaces is open** on the
  same stores. `places open` still opens the place then, but doesn't record
  the open (`"recorded": false, "notRecordedReason": "app_running"`).
- A small race remains: starting the app while a write is in progress. Writes
  take milliseconds, so this is accepted for now. Sending changes through the
  running app is the planned fix.

## Notes for agent integrations

- Call `qp describe` once and keep the catalogue as the tool definition. An
  MCP server can wrap each command as one tool, with no logic of its own.
- `openCount` is the lifetime total. `opens` (from `places get`) and
  `opensInPeriod` count only timed opens, kept since schema v4 and capped at
  500. `opensTimed` says how many of `openCount` have a time.
- `exists` reports whether a folder or file is there now. It is `null` for
  links.

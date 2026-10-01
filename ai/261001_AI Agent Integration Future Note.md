# Future option: AI agents driving QuickerPlaces through qp

**Status:** potential dev step, not scheduled. Written 2026-10-01 at the user's request, to record the direction before deciding whether to build it.

## What is already built

These are done on `claude/hopeful-allen-ljsp3h`. Builds and tests pass on Linux; nothing below has been checked on Windows yet (see the checklist at the end).

- **places.json schema v4.** Each place keeps its last 500 open times (`opens`), its own `tags` and an optional `note`. A v3 store migrates with its `lastOpenedAt` as the first entry in `opens`.
- **`qp`, the command line** (`src/QuickerPlaces.Cli`, [README](../src/QuickerPlaces.Cli/README.md)). It prints one JSON document per run, with stable error and exit codes, and `qp describe` prints the full command catalogue for an agent to read. It covers places (list, find, get, top, open, add, tag, note), sessions (list, get, open, save), `files open` (Windows), `files recent`, `folders recent` and `activity days`.
- **Changes go through the running app.** While QuickerPlaces is open, qp sends each change over a named pipe that only the current Windows user can open (`Services/Remote`). The app runs it on its own services, saves it and refreshes its window. When the app is closed, qp runs the same operations on the files itself.

## The potential next step: an MCP wrapper

An MCP server is the standard way to give an AI agent a set of tools. The wrapper would turn each qp command into one tool and run `qp` for each call. It would have no logic of its own: the tool definitions come from `qp describe`, and every rule (validation, migrations, saving through the app) stays in qp.

Proposed shape:

- In Odysseus as `mcp_servers/quickerplaces_server.py`, next to its existing MCP servers. It may be worth a stand-alone copy later, so other local agents can use it too.
- Read-only tools by default. Tools that change something (`places add/tag/note`, `sessions save`) and tools that open things on screen (`places open`, `sessions open`) ask the user to confirm first.
- It finds `qp` from a configured path, and passes `--data-root` through for testing.
- Roughly 100 lines of Python. qp's own tests already cover the behaviour underneath.

## How an agent would reach it: options considered

| Option | What runs where | Decision |
|---|---|---|
| **Odysseus with a local model** | Everything on this PC; nothing leaves it | Preferred where privacy matters most. Limited by how well the local model uses tools |
| **Odysseus or OpenAI's Codex CLI with a cloud model** (OpenAI API key or Enterprise sign-in) | Agent and qp local; only the conversation goes to the model provider | Acceptable. Nothing listens for outside connections |
| **Both** | Local model for everyday questions, cloud model for naming and summaries | Likely the practical end state |
| **ChatGPT (web) calling qp directly** through a custom connector or GPT Action | Needs qp behind a public HTTPS endpoint (tunnel or reverse proxy) with OAuth | **Rejected (2026-10-01).** It puts a door to the file system on the internet |

An API key lets a local program call a cloud model; it does not let the cloud model reach the PC. So any cloud option still needs the local wrapper above.

## Privacy

- With a cloud model, everything qp returns is sent as conversation content: file names, folder paths, place aliases, tags, notes and session names. Check that this is acceptable for client folder names before using one. Enterprise and API terms normally exclude this data from training, but confirm with IT.
- qp never sends anything anywhere itself. It only reads and writes the local stores and talks to the local app.

## Example flows this enables

- "Open my Acme folder": `places find acme`, then `places open`.
- "What did I work on yesterday / this week?": `activity days --days 1`, or `--period week`.
- "Reopen the Henderson session": `sessions open "Henderson audit"`.
- "Save what I have open as a session": `files open` (files, folders, containing places, a suggested name). The agent proposes a name from the folders, file names and date, shows the list and the name, and then runs `sessions save`.
- "Suggest tags for my untagged places": `places list`, then `places tag` for each place the user approves.

## Open questions before building

1. Should the wrapper live in Odysseus, or stand alone so any MCP client (Codex, Claude Code, others) can use it?
2. Which writes, if any, may run without asking each time?
3. Should `qp` be published as a single-file exe, so the wrapper needs no .NET SDK? This is related to Phase 8 (distribution).
4. Does a stronger signal than "likely open" matter enough for `sessions save` to need app-specific adapters? (See [260926_File Activity Future Note](260926_File%20Activity%20Future%20Note.md).)

## Windows checks still owed for what is built

- [ ] With the app open, `qp places add --alias Test --url https://example.com` shows the place in the window at once, with the status line.
- [ ] With the app open, `qp places open Test` opens the browser and the Opens column goes up.
- [ ] With the app closed, the same commands write places.json, and the app shows them on its next start.
- [ ] `qp files open` lists the same files the Sessions screen finds.
- [ ] `qp sessions save` with those files shows the session in the Sessions panel (a Sessions window already open shows it next time it opens).
- [ ] `qp sessions open` opens the files and updates the session's Last opened time.
- [ ] Two Windows users on one PC each reach only their own app.
- [ ] An existing v3 places.json upgrades on the next save, with no places lost.

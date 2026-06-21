# Command Reference

The full manual for the `eu4indexer` CLI. The flag descriptions here mirror the
`--help` output; `Eu4Indexer.Cli/Program.fs` is the authoritative source.

Run any command with `--help` to see its flags. From a source checkout, replace
`eu4indexer <cmd>` with `dotnet run --project Eu4Indexer.Cli -- <cmd>`.

```text
USAGE: eu4indexer [--help] <subcommand> [<args>]

SUBCOMMANDS:
    index       parse game + mods and write the index
    detect      show resolved game dir, mods, and predicted file overrides
    workshop    list installed Steam Workshop items (id and mod name)
    playset     list launcher playsets, or the mods of one playset
    serve       run the read-only MCP server over stdio
    setup       download the cwtools config rules for the game
    install     register the MCP server + skill with local agents
    use         set the active index the MCP server serves by default
    list        list registered indexes (* marks the active one)
    version     print the eu4indexer version and exit
```

## Common concepts

### Selecting the game

Every command takes `--game <id>` (default `eu4`; supported: `eu4`, `hoi4`). The
game determines which CWTools config is used, which extractors run, and which
per-game detail tables are written.

### Locating the game and mods

- **Game**: pass `--game-dir` / `-g`, or omit it to auto-detect. Auto-detection
  finds the Steam client (via the Windows registry, or the default install
  location on macOS / Linux), then reads its `libraryfolders.vdf` to locate every
  Steam library — so the game is found even when installed on a non-default
  drive. If detection fails, pass `--game-dir` explicitly.
- **Mods**: combine any of —
  - `--mod <path>` / `-m` — a `.mod` descriptor file or a content directory;
    repeatable, order = load order.
  - `--workshop-id <id>` / `-w` — a subscribed Steam Workshop item, pulled
    straight from the workshop content dir; repeatable (see `workshop`).
  - `--playset <name-or-id>` / `-p` — a launcher playset's *enabled* mods, read
    from `launcher-v2.sqlite` in the playset's load order (see `playset`).
  - `--auto-mods` — discover enabled mods from the launcher's `dlc_load.json` /
    `mod/*.mod` descriptors under the Paradox user-data directory.
- **Load order** when several sources are combined: `--mod`, then `--workshop-id`,
  then `--playset`, then `--auto-mods`. Later mods override earlier mods; any mod
  overrides the base game. Override relationships are recorded explicitly (see
  [database.md](database.md)).

### Config rules

The CWTools config repo is resolved from `--config-dir` / `-c`, then
`$EU4_CONFIG_DIR`, then the game-namespaced install dir
`~/.eu4indexer/config/<game>` (populated by `eu4indexer setup`).

---

## `index`

Parse the game + mods and write the index.

| Flag | Description |
|---|---|
| `--game-dir`, `-g` | base game directory (auto-detected if omitted) |
| `--mod`, `-m` | mod directory or `.mod` descriptor; repeatable, order = load order |
| `--workshop-id`, `-w` | Steam Workshop item id to include as a mod; repeatable |
| `--playset`, `-p` | launcher playset name or id; indexes its enabled mods in load order |
| `--auto-mods` | auto-discover enabled mods from the launcher / mod dir |
| `--config-dir`, `-c` | cwtools config repo dir (default: `$EU4_CONFIG_DIR`, then `~/.eu4indexer/config/<game>`) |
| `--db`, `-o` | output target: a SQLite file path, or a PostgreSQL connection string. Default: `~/.eu4indexer/db/<game>/<name>.db` |
| `--name`, `-n` | registry name for this index (default: `default`); the default db file is named after it |
| `--languages` | comma-separated localisation languages (default: all) |
| `--skip-generic` | index only events/missions/decisions/modifiers |
| `--no-fts` | skip building full-text search tables |
| `--verbose` | print per-stage progress |
| `--progress` | show live progress: a counter of processed files / entities / loc entries while parsing, then the current finalize sub-step (indexes / FTS / views / integrity / optimize). Refreshes in place on a terminal; falls back to periodic lines when stderr is redirected |
| `--game` | game id (default: `eu4`; supported: `eu4`, `hoi4`) |

SQLite indexes are recorded in a registry so `serve`/`use`/`list` and the MCP
server can find them. Without `--db`, the index is written to the game-namespaced
install dir and registered as the **active** index. Use `--name` to build several
(e.g. `vanilla`, a playset). Postgres targets are not registered (they are not
local files the MCP server reads).

```bash
# Auto-detect the game, index vanilla, register as active
eu4indexer index --config-dir /path/to/cwtools-eu4-config

# Index with explicit mods, in load order, into a named index
eu4indexer index \
    --mod /path/to/mod_a --mod /path/to/mod_b \
    --config-dir /path/to/cwtools-eu4-config \
    --name my-playset --verbose

# HOI4
eu4indexer index --game hoi4 --config-dir /path/to/cwtools-hoi4-config
```

### Export target (SQLite or PostgreSQL)

`--db` accepts either a **SQLite file path** (the default) or a **PostgreSQL
connection string**, auto-detected:

```bash
# SQLite (default)
eu4indexer index … --db eu4.db

# PostgreSQL — keyword form or a postgres:// URI
eu4indexer index … --db "Host=localhost;Database=eu4;Username=eu4;Password=secret"
eu4indexer index … --db "postgres://eu4:secret@localhost/eu4"
```

The Postgres export carries the same tables, override graph, and `refs` causal
graph. Full-text search is provided by `pg_trgm` GIN indexes (the substring/CJK
analogue of the SQLite trigram FTS). Existing eu4-indexer tables in the target
database are dropped and rebuilt; other objects are left untouched. The role
needs `CREATE EXTENSION pg_trgm` privilege (skip search with `--no-fts`). The
bundled MCP server reads SQLite only; the Postgres export is for your own SQL /
BI / `pgvector` use. See [database.md](database.md#postgresql-export) for details.

---

## `refresh`

Re-index a registered database in place — use it after a game or mod update so
the index reflects the new content.

| Flag | Description |
|---|---|
| `--name`, `-n` | registry name of the index to re-index (default: refresh **every** registered index) |
| `--verbose` | print per-stage progress |
| `--progress` | show a live counter of processed files / entities / loc entries (same as `index`) |

`refresh` needs no source flags: each index records its original `index`
invocation (game dir, mods, workshop ids, playset, auto-discovery, languages,
`--skip-generic` / `--no-fts`) in its `meta`, and replays it through the same
resolvers. So:

- **Workshop mods** (`--workshop-id`) are re-located by id — a moved Steam
  library or updated item is found again.
- **A playset** (`--playset`) is re-expanded — you get its current mod list,
  load order, and enabled/disabled state.
- **Auto-discovery** (`--auto-mods`) re-runs against the current launcher state.
- The base game dir is re-detected when it was auto-detected originally, so a
  relocated install still resolves.

When refreshing all indexes, a single failure (e.g. a deleted game dir) is
reported as a warning and skipped; the rest still refresh, and the command exits
non-zero if any failed. The active index selection is left unchanged. Only
SQLite indexes are registered, so Postgres exports are not refreshed here —
re-run `index` with the connection string for those.

```bash
# Re-index every registered database after a patch
eu4indexer refresh

# Re-index just one, with progress
eu4indexer refresh --name my-playset --progress
```

---

## `detect`

Show the resolved game dir, mods (in load order), and predicted file overrides —
a dry run for `index`. Takes the same source-selection flags: `--game-dir`,
`--mod`, `--workshop-id`, `--playset`, `--auto-mods`, `--game`.

```bash
eu4indexer detect --game-dir /path/to/eu4 --mod /path/to/some_mod
```

---

## `workshop`

List installed Steam Workshop items as `<id>  <name>` (reads each
`descriptor.mod` name only — no full parse). Pass ids positionally to show only
those. Flags: `--game`.

```bash
eu4indexer workshop
```
```text
Workshop items (3):
  1000000001   Example Mod A
  1000000002   Example Mod B
  1000000003   Example Mod C
```

```bash
# Show only specific ids
eu4indexer workshop 1000000001 1000000003
```

Feed the ids you want to `index --workshop-id`:

```bash
eu4indexer index \
    --workshop-id 1000000001 --workshop-id 1000000002 \
    --config-dir /path/to/cwtools-eu4-config --db eu4.db
```

---

## `playset`

List launcher playsets, or one playset's mods, read straight from the launcher's
`launcher-v2.sqlite`. Flags: `--game`. A playset name with spaces can be passed
unquoted (positional tokens are joined) or quoted.

```bash
eu4indexer playset
```
```text
Playsets (2) [* = active]:
  * My Playset
    Another Playset
```

```bash
eu4indexer playset "My Playset"
```
```text
Playset 'My Playset' (active) — 3 mods (2 enabled) [x = enabled]:
  [x] 1000000001   Example Mod A
  [x] 1000000002   Example Mod B
  [ ] 1000000003   Example Mod C
```

Index a whole playset's *enabled* mods, in the playset's load order:

```bash
eu4indexer index --playset "My Playset" \
    --config-dir /path/to/cwtools-eu4-config --db out.db --verbose
```
```text
Indexed 3 sources, NNNN files, NNNN entities (NNNN effective), NNNN loc entries.
Overrides: NNNN. Parse errors: 0. FK violations: 0.
Registered index 'default' (active) at …/out.db
```

> The ids, names, and counts above are illustrative placeholders.

---

## `setup`

Download the CWTools config rules for the game into `~/.eu4indexer/config/<game>`.
With no `--game`, downloads config for all known games.

| Flag | Description |
|---|---|
| `--game` | only download config for this game (default: all known games) |
| `--ref` | override the pinned cwtools config commit/branch to download |

```bash
eu4indexer setup                 # all games
eu4indexer setup --game hoi4     # one game
```

---

## `serve`

Run the read-only MCP server over stdio.

| Flag | Description |
|---|---|
| `--db`, `-o` | index database to serve (default: the active database, or `$EU4_DB`) |

Database precedence: explicit `--db`, then the registry's active index, then
`$EU4_DB`. The server refuses a schema-version mismatch on startup. See
[MCP tools](#mcp-tools) below.

---

## `install`

Register the MCP server + skill with local agents (Claude Code, Codex). Writes a
**user-scoped** MCP server pointing at the installed binary by absolute path and
copies the per-game skill into the agent's skills dir — no plugin and no
per-directory setup needed.

| Flag | Description |
|---|---|
| `--agents` | comma-separated agents to register with (default: `claude,codex`) |
| `--language` | skill language (default: `en`; supported: `en`, `zh`) |
| `--yes` | assume yes to prompts (non-interactive) |

```bash
eu4indexer install                       # claude + codex, English skill
eu4indexer install --agents claude --language zh
```

---

## `use` / `list`

Manage the index registry. `list` shows all registered SQLite indexes (`*` marks
the active one); `use <name>` sets which one `serve` and the MCP server serve by
default.

```bash
eu4indexer list
eu4indexer use my-playset
```

---

## `version`

Print the `eu4indexer` version and exit.

---

## `update`

Self-update the installed binary to the latest GitHub release, then refresh any
CWTools config rules whose pinned ref has moved on in the new build. Brings both
the binary and the config rules current in one step.

| Flag | Description |
|---|---|
| `--check` | only report whether a newer release is available; don't download |
| `--force` | reinstall the latest release even if already up to date |

```bash
eu4indexer update            # update if a newer release exists, then refresh config
eu4indexer update --check    # just compare current vs latest
eu4indexer update --force    # reinstall the latest binary
```

How it works:

- Resolves the latest release via GitHub's `releases/latest` redirect (no API
  token, no rate limit) and compares it to the running version.
- Downloads the version-less `eu4indexer-<rid>` archive for the host and replaces
  the installed `bin/` and `skills/` atomically. On Unix the live binary is
  swapped in place; on **Windows** a detached helper waits for the process to
  exit, swaps the directories, and relaunches the config refresh (a running
  `.exe` and its loaded DLLs can't be overwritten in place).
- After the swap, the **new** binary refreshes config for any game whose
  installed ref differs from the one it pins (recorded in a `.eu4indexer-ref`
  marker; pre-marker installs are refreshed once).

`update` only works for script installs (`install.sh` / `install.ps1`). Run from
a source checkout (`dotnet run`, `bin/Debug/...`) it refuses the swap — update via
git instead. Offline or pinned installs (`EU4INDEXER_DIST`) have no release source;
use `--check` to compare versions only.

---

## Manual MCP registration

To register with another MCP client, point it at the installed binary:

```json
{ "mcpServers": { "eu4": {
  "command": "/absolute/path/to/.eu4indexer/bin/eu4indexer",
  "args": ["serve"]
} } }
```

`serve` serves the active index from the registry; add `"--db", "/abs/path.db"`
to pin a specific one. Building from source instead? Use
`dotnet run --project Eu4Indexer.Cli -- serve --db /abs/path/eu4.db`.

## MCP tools

`eu4indexer serve` exposes the index to an AI agent as typed tools, so the
correct joins (effective-only, the causal graph, localisation) are built in
rather than left to ad-hoc SQL. When several indexes are registered, the agent
can switch between them mid-session.

| Tool | Purpose |
|---|---|
| `describe_schema` | the DDL and a data dictionary — call it first |
| `explain_entity` | an entity's conditions, options, and what it references / is referenced by |
| `what_triggers` | reverse traversal of the causal graph (what reaches X) |
| `what_does_it_do` | forward traversal of the causal graph (what X reaches) |
| `analyze_effects` | summarise the effects an entity applies |
| `find_by_condition` | what is gated by a flag/variable/trigger |
| `trace_to_goal` | bounded backward-chaining: what sequence of actions reaches an event/flag/variable |
| `find_dangling` | flags checked but never set, events fired but undefined — candidate bugs |
| `resolve_symbol` | explain a trigger/effect, or expand a scripted definition |
| `search_localisation` | markup-stripped, CJK-friendly text search |
| `search_everything` | cross-type search when the content type is unknown |
| `list_sources` | load order and source metadata |
| `get_overrides` | who-overrode-what across the three levels |
| `list_databases` / `select_database` | pick which registered index to query |
| `read_query` | a guarded read-only `SELECT` escape hatch |

The bundled per-game skill (`skills/eu4-indexer/<game>/<lang>/SKILL.md`) teaches
an agent the game's causal model and the workflows for combining these tools
(explain an entity, explain a phenomenon / find a bug, plan how to reach a goal).

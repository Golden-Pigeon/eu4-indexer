# Project Structure & Architecture

How the repository is laid out, how the subsystems fit together, and how the
indexing pipeline flows. For the database itself see [database.md](database.md);
for the CLI see [commands.md](commands.md).

## Repository layout

```text
eu4-indexer/
├── Eu4Indexer.Core/        # F#  — parsing, extraction, override + ref resolution, DB writer
│   ├── Extractors/         #       per-type entity extractors
│   └── Database/           #       SQLite + PostgreSQL schema and writer
├── Eu4Indexer.Cli/         # F#  — Argu command-line front-end (Program.fs)
├── Eu4Indexer.Mcp/         # C#  — read-only MCP server exposing query tools
├── Eu4Indexer.Tests/       # C#  — xunit unit + integration tests
├── skills/eu4-indexer/     #       per-game, per-language agent skills (<game>/<lang>/SKILL.md)
├── scripts/                #       build-binaries.{sh,ps1}, mcp-smoke.py
├── external/cwtools/       #       vendored CWTools fork (git submodule)
├── install.sh / install.ps1
└── docs/
```

## Projects

| Project | Language | Purpose |
|---|---|---|
| `Eu4Indexer.Core` | F# | Parsing, extraction, override resolution, reference graph, SQLite/PostgreSQL writer |
| `Eu4Indexer.Cli` | F# (Argu) | CLI: `index`, `detect`, `workshop`, `playset`, `serve`, `setup`, `install`, `use`, `list`, `version` |
| `Eu4Indexer.Mcp` | C# | Read-only MCP server exposing query tools to agents |
| `Eu4Indexer.Tests` | C# (xunit) | Unit + integration tests |

## `Eu4Indexer.Core`

The library compiles in **dependency order** — the `<Compile>` list in
`Eu4Indexer.Core.fsproj` is significant; when you add a file, place it after
everything it depends on.

Key modules:

- **Discovery** (`Discovery.fs`, `Launcher.fs`, `ModDescriptor.fs`) — locate the
  game (Steam `libraryfolders.vdf`), resolve mods from `--mod` / workshop /
  playset (`launcher-v2.sqlite`) / auto-discovery.
- **File & override resolution** (`FileResolution.fs`, `OverrideResolution.fs`) —
  compute the effective file set across the load order and record file/entity/loc
  overrides as pure, unit-testable functions.
- **Parsing & config** (`Parsing.fs`, `ConfigCatalog.fs`, `Localisation.fs`) — the
  only modules that call CWTools. Keep all CWTools usage isolated here.
- **Extraction** (`Extractors/`) — one module per content type: `Events.fs`,
  `Missions.fs`, `Decisions.fs`, `Modifiers.fs`, `FocusTrees.fs` (HOI4),
  `Ideas.fs`, `Defines.fs`, plus `Generic.fs` (config-`type`-driven) and
  `Support.fs`.
- **Script tree & references** (`ScriptTree.fs`, `ReferenceExtractor.fs`) — flatten
  each entity into the recursive `script_nodes` tree and derive the `refs` causal
  graph.
- **Pipeline** (`Pipeline.fs`) — orchestrates a run end to end.
- **Database** (`Database/Schema.fs`, `PostgresSchema.fs`, `Dialect.fs`,
  `Writer.fs`) — DDL and the bulk-load writer for both backends.
- **App plumbing** (`AppPaths.fs`, `AppInfo.fs`, `Registry.fs`, `Setup.fs`,
  `AgentInstall.fs`) — install dir layout, version, the index registry, config
  download, and agent registration.

### The `GameAdapter` abstraction

The core is **game-agnostic**. `GameAdapter.fs` (`Domain.fs` for the shared
types) defines a `GameAdapter` describing a game's id, directory layout,
extractor set, and detail tables. `GameAdapter.byId` maps `"eu4"`/`"hoi4"` to an
adapter; `allAdapters` drives multi-game `setup`. Adding a game means writing a
new adapter (and any game-specific extractors / detail tables) — the discovery,
override, parsing, and writer machinery is reused unchanged.

## Indexing pipeline

```text
discover sources (game + mods, load order)
  → resolve effective files + record file/entity/loc overrides
  → parse scripts and localisation (CWTools)
  → extract entities (per-type extractors)
  → flatten into the script_nodes tree, tag symbols from the .cwt config
  → derive the refs causal graph
  → write SQLite / PostgreSQL
  → build secondary indexes, FTS, and views; register the index
```

Errors are **recorded, not swallowed**: parse failures go into `parse_errors` and
the run continues.

## `Eu4Indexer.Mcp`

A read-only MCP server. It opens the database with `Mode=ReadOnly`, keeps all SQL
parameterized and result sets bounded, and puts the right joins (effective rows,
the `refs` graph, localisation) inside each tool so agents don't hand-roll them. A
tool is a `[McpServerTool]` method on a `[McpServerToolType]` class taking
`Eu4Database` as its first parameter; it is picked up automatically. The server
rejects an index whose `PRAGMA user_version` doesn't match `Schema.UserVersion`.
See the [MCP tool list](commands.md#mcp-tools).

## CWTools submodule

CWTools is referenced from source via a git submodule at `external/cwtools` — a
[fork](https://github.com/Golden-Pigeon/cwtools) pinned to a build that compiles
against the current .NET SDK / FSharp.Core — because the published NuGet
(0.3.0, 2019) predates the rule format used by the current Paradox config repos.
Changes to CWTools itself belong in the fork; update the submodule pointer here
in a separate, clearly described commit.

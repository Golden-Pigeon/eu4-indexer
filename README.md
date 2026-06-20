# eu4-indexer

Indexes Paradox grand-strategy game scripts — **Europa Universalis IV** and
**Hearts of Iron IV**, plus any loaded mods — into a queryable SQLite (or
PostgreSQL) database. It parses events, missions, decisions, focus trees, ideas,
modifiers and every other `common/` script type with
[CWTools](https://github.com/cwtools/cwtools), recording their full
condition/effect trees, localisation in all languages, and the exact override
relationships between the base game and each mod. The core is game-agnostic (a
`GameAdapter` abstraction); EU4 and HOI4 ship today, with room for more.

## Contents

- [Architecture](#architecture)
- [Highlights](#highlights)
- [Installation](#installation)
- [Usage](#usage)
- [Documentation](#documentation)
- [Contributing](#contributing)
- [License](#license)
- [Disclaimer](#disclaimer)
- [Acknowledgements](#acknowledgements)

## Architecture

Four projects plus a bundled agent skill. `Eu4Indexer.Core` does the work
(discover sources → resolve overrides → parse with CWTools → extract entities →
build the causal `refs` graph → write the database); `Eu4Indexer.Cli` is the
command-line front-end; `Eu4Indexer.Mcp` is a read-only MCP server that serves a
built index to an AI agent as typed tools; and the per-game skill teaches the
agent how to use them.

| Project | Language | Purpose |
|---|---|---|
| `Eu4Indexer.Core` | F# | Parsing, extraction, override + reference resolution, SQLite/PostgreSQL writer |
| `Eu4Indexer.Cli` | F# (Argu) | The `eu4indexer` command-line interface |
| `Eu4Indexer.Mcp` | C# | Read-only MCP server exposing query tools to agents |
| `Eu4Indexer.Tests` | C# (xunit) | Unit + integration tests |

CWTools is vendored as a git submodule at `external/cwtools`. See
[docs/architecture.md](docs/architecture.md) for the full layout and pipeline.

## Highlights

- **Multi-game** through one game-agnostic core (`GameAdapter`); EU4 + HOI4 today.
- **Full override resolution** at three levels (file / entity / localisation)
  across the load order — every winner/loser is recorded, not just the result.
- **Recursive condition/effect tree** with triggers/effects/modifiers tagged from
  the CWTools `.cwt` config.
- **Derived causal graph** (`refs`) answering "what triggers X" and "how do I
  reach goal Y".
- **CJK-aware localisation**: EU4 special-escape decoding, markup stripping, and a
  trigram FTS so colour-split Chinese text stays searchable.
- **Steam / launcher integration**: game auto-detection, Workshop items,
  launcher playsets, and auto-discovered enabled mods.
- **MCP server + guided skill** (English / 中文) for querying with an agent.
- **SQLite or PostgreSQL** export from the same run.
- **Self-contained binary** — no .NET install needed to run it.

## Installation

### Script (recommended)

```bash
# macOS / Linux
curl -fsSL https://raw.githubusercontent.com/Golden-Pigeon/eu4-indexer/main/install.sh | sh
```

```powershell
# Windows (PowerShell)
irm https://raw.githubusercontent.com/Golden-Pigeon/eu4-indexer/main/install.ps1 | iex
```

The script downloads the self-contained `eu4indexer` binary plus the bundled
skill into `~/.eu4indexer`, symlinks it onto your PATH via `~/.local/bin`, and
clears the macOS Gatekeeper quarantine / Windows Mark-of-the-Web on the unsigned
binary. The install location is configurable with `EU4INDEXER_HOME` or
`--location DIR`.

### Manual

Download a release archive for your platform from the
[Releases](https://github.com/Golden-Pigeon/eu4-indexer/releases) page, extract
it, and put `bin/eu4indexer` on your PATH. The binary bundles its own runtime, so
no .NET install is required.

### From source

Requires the **.NET 9 SDK**. Clone with submodules so CWTools is fetched:

```bash
git clone --recursive https://github.com/Golden-Pigeon/eu4-indexer.git
# already cloned without --recursive?
git submodule update --init --recursive

dotnet build Eu4Indexer.slnx
# run any command via:
dotnet run --project Eu4Indexer.Cli -- <command> [args]
```

## Usage

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

Each command takes `--game eu4|hoi4` (default `eu4`) and `--help`. The full
manual — every flag, mod/game selection, the SQLite/PostgreSQL export target, and
the Workshop/playset walkthroughs — is in
[docs/commands.md](docs/commands.md).

### A typical run

```bash
eu4indexer setup       # download the cwtools config rules (per game)
eu4indexer index       # build an index from your local install (auto-detects
                       # the game; add --mod / --playset / etc.)
eu4indexer install     # register the MCP server + skill with Claude Code / Codex
```

Indexes default to `~/.eu4indexer/db/<game>/<name>.db` and are tracked in a
registry: `eu4indexer list` shows them and `eu4indexer use <name>` sets the
active one.

Keep a script install current with `eu4indexer update` — it self-updates the
binary to the latest release and refreshes any stale config rules
(`eu4indexer update --check` just compares versions).

### Querying with an agent (skill)

`eu4indexer install` writes a user-scoped MCP server pointing at the installed
binary and copies the per-game skill (`skills/eu4-indexer/<game>/<lang>/SKILL.md`)
into your agent — no plugin and no per-directory setup needed. Choose the skill
language with `--language en|zh` and the agents with `--agents claude,codex`.
Afterwards, start your agent in **any directory** and ask game-content questions
(events, missions, focuses, modifiers, flags, mod overrides, localisation, "how
do I achieve X", "is this a bug"); the skill drives the `eu4` MCP tools. See the
[MCP tool list](docs/commands.md#mcp-tools).

## Documentation

- [Project Structure & Architecture](docs/architecture.md) — layout, modules,
  the `GameAdapter` abstraction, and the indexing pipeline.
- [Database Schema](docs/database.md) — tables, the override graph, the `refs`
  causal graph, views, full-text search, and the PostgreSQL export.
- [Command Reference](docs/commands.md) — every command and flag, mod/game
  selection, export targets, and the MCP tools.
- [Changelog](CHANGELOG.md) — what changed in each release.

## Contributing

Contributions are welcome — bug fixes, new extractors, and new game adapters. See
[CONTRIBUTING.md](CONTRIBUTING.md) for setup, conventions, and the test workflow
(`dotnet test`; integration tests no-op without game data).

## License

[MIT](LICENSE) covers the **source code of this tool only**. This project links
[CWTools](https://github.com/cwtools/cwtools), which is also MIT-licensed.

## Disclaimer

- **Not affiliated with Paradox Interactive.** This is an unofficial, fan-made
  tool, not developed, endorsed, sponsored by, or affiliated with Paradox
  Interactive AB. *Europa Universalis IV*, *Hearts of Iron IV*, and all related
  names, assets, and trademarks are the property of Paradox Interactive.
- **No game content is included or distributed.** This repository ships only
  source code. It reads game and mod files that already exist on your own
  machine; you must own a legitimate copy of the game to use it.
- **The generated database contains third-party content.** Any index you build
  embeds text and script owned by Paradox Interactive and/or the respective mod
  authors. That output is intended for personal, research, and interoperability
  use. You are responsible for not redistributing it in violation of those
  parties' rights, the game EULA, or mod licenses (e.g. Steam Workshop terms).
- **Mod content belongs to its authors.** Override and localisation data
  extracted from mods remains the property of the mod creators.
- **No warranty.** The software is provided "as is", without warranty of any
  kind, as stated in the [LICENSE](LICENSE). Use at your own risk; the authors
  are not liable for any data loss or other damages.

## Acknowledgements

- [CWTools](https://github.com/cwtools/cwtools) — the Paradox-script parser this
  tool is built on, vendored as a [fork](https://github.com/Golden-Pigeon/cwtools).
- The non-Latin EU4 special-escape decoding logic is based on
  [bruceCzK's original conversion script](https://gist.github.com/bruceCzK/96ad6e054111f929ed67291552d36334).

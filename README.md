# eu4-indexer

Indexes Europa Universalis IV (EU4) game scripts — and any loaded mods — into a
queryable SQLite database. It parses events, missions, decisions, modifiers and
every other `common/` script type with [CWTools](https://github.com/cwtools/cwtools),
records their full condition/effect trees, localisation in all languages, and
the exact override relationships between the base game and each mod.

The design is game-agnostic at its core (a `GameAdapter` abstraction); EU4 is the
only implementation today, with room to add CK3 / HOI4 / Stellaris / VIC3 later.

## Projects

| Project | Language | Purpose |
|---|---|---|
| `Eu4Indexer.Core` | F# | Parsing, extraction, override resolution, SQLite writer |
| `Eu4Indexer.Cli` | F# (Argu) | `index` and `detect` commands |
| `Eu4Indexer.Tests` | C# (xunit) | Unit + integration tests |

CWTools is referenced from source via a git submodule at `external/cwtools`
(a [fork](https://github.com/Golden-Pigeon/cwtools) pinned to a build that
compiles against the current .NET SDK / FSharp.Core) because the published
NuGet (0.3.0, 2019) predates the rule format used by the current EU4 config repo.

## Prerequisites

- .NET 9 SDK
- Clone with submodules so CWTools is fetched:
  ```bash
  git clone --recursive https://github.com/Golden-Pigeon/eu4-indexer.git
  # already cloned? then:
  git submodule update --init --recursive
  ```
- The EU4 config rules repo, e.g.
  [`cwtools-eu4-config`](https://github.com/cwtools/cwtools-eu4-config)
- A copy of the EU4 game files, and optionally mod directories

## Usage

```bash
# Show what would be indexed (game dir, mods in load order, replace_paths)
dotnet run --project Eu4Indexer.Cli -- detect \
    --game-dir /path/to/eu4 \
    --mod /path/to/some_mod

# Build the index
dotnet run -c Release --project Eu4Indexer.Cli -- index \
    --game-dir /path/to/eu4 \
    --mod /path/to/mod_a \
    --mod /path/to/mod_b \
    --config-dir /path/to/cwtools-eu4-config \
    --db eu4.db \
    --verbose
```

### Locating the game and mods

- **Game**: pass `--game-dir`, or omit it to auto-detect from the platform's
  default Steam library locations (Windows / macOS / Linux).
- **Mods**: pass `--mod <path>` (repeatable — order is load order; a `.mod`
  descriptor file or a content directory both work), or use `--auto-mods` to
  discover enabled mods from the launcher's `dlc_load.json` / `mod/*.mod`
  descriptors under the Paradox user-data directory.
- **Config**: `--config-dir`, or the `EU4_CONFIG_DIR` environment variable.

Later mods override earlier mods, and any mod overrides the base game. Override
relationships are recorded explicitly — see below.

## Schema highlights

- `sources` — base game + mods, with `load_order` and descriptor metadata.
- `files` — every file from every source (shadowed losers retained, with
  `is_effective = 0`), plus `content_hash` and `parse_status`.
- `entities` — generic table keyed by `entity_type` + `entity_key`; core types
  (`event`, `mission`, `decision`, `*_modifier`) also get typed detail tables
  (`event_details`, `mission_details`, …).
- `script_nodes` — the recursive condition/effect tree: one row per
  clause/leaf, with `context` (`trigger`/`effect`/`mtth`/`ai_chance`/`metadata`),
  `depth`/`parent_id`, and a `symbol_id` tagging known triggers/effects/modifiers.
- `symbols` — trigger/effect/modifier dictionary distilled from the `.cwt` config.
- `localisation` — every language, with `is_replace` and `is_effective`.
  Non-Latin mods (e.g. Chinese) that hide their text in the `l_english` slot
  using EU4's [special-escape](https://gist.github.com/bruceCzK/96ad6e054111f929ed67291552d36334)
  encoding are decoded back to real UTF-8 on the way in; ASCII/Latin text is
  untouched. A `value_plain` column holds the value with inline formatting
  markup (`§` colour codes, `£` icons) stripped; `loc_fts` indexes it with the
  trigram tokenizer so colour-split CJK text stays searchable.
- Override tables: `file_overrides`, `entity_overrides`, `loc_overrides`,
  unified by the `v_override_summary` view (level + kind + winner/loser source).
- `refs` — derived causal graph: one row per reference a script node makes
  (fires an event, sets/checks a flag or variable, applies/checks a modifier,
  calls a scripted trigger/effect, on_action firing). Flags are scope-qualified
  (`country_flag`/`global_flag`/`province_flag`/`ruler_flag`) and conditions
  carry a `negated` flag. Powers "what triggers X / what would reach goal Y".
- FTS5: `loc_fts`, `entity_fts` for full-text search over localisation and raw
  entity script.

`PRAGMA user_version` and the `meta` table carry schema version and run provenance.

## Example queries

```sql
-- Events that use the add_stability effect
SELECT DISTINCT e.entity_key
FROM entities e
JOIN script_nodes n ON n.entity_id = e.entity_id
JOIN symbols s ON s.symbol_id = n.symbol_id
WHERE e.entity_type = 'event' AND e.is_effective = 1
  AND s.kind = 'effect' AND s.name = 'add_stability';

-- What did a mod (source_id = 2) override, across all three levels?
SELECT level, kind, what FROM v_override_summary WHERE winner_source_id = 2;

-- Entities whose trigger references the country tag FRA
SELECT DISTINCT e.entity_type, e.entity_key
FROM script_nodes n
JOIN entities e USING (entity_id)
WHERE n.context = 'trigger' AND (n.value = 'FRA' OR (n.key = 'tag' AND n.value = 'FRA'));
```

## Querying with an agent (MCP)

`Eu4Indexer.Mcp` is a read-only [MCP](https://modelcontextprotocol.io) server
that exposes the index to an AI agent as typed tools, so the correct joins
(effective-only, the causal graph, localisation) are built in rather than left
to ad-hoc SQL. It takes the database path from `--db` or the `EU4_DB`
environment variable and refuses a schema-version mismatch on startup.

Tools so far: `explain_entity` (an entity's conditions, options, and what it
references / is referenced by); `what_triggers` and `what_does_it_do` (reverse
and forward traversal of the causal graph); `find_by_condition` (what is gated
by a flag/variable/trigger); `trace_to_goal` (bounded backward-chaining: what
sequence of actions reaches an event/flag/variable); `find_dangling` (flags
checked but never set, events fired but undefined — candidate bugs);
`search_localisation` (markup-stripped,
CJK-friendly text search); `search_everything` (cross-type search when the
content type is unknown); and `resolve_symbol` (explain a trigger/effect, or
expand a scripted definition).

Register it with an MCP client, e.g. Claude Code / Desktop:

```json
{ "mcpServers": { "eu4": {
  "command": "dotnet",
  "args": ["run", "--project", "/abs/path/Eu4Indexer.Mcp", "--", "--db", "/abs/path/eu4.db"]
} } }
```

## Testing

```bash
dotnet test
```

Unit tests run anywhere. Integration tests index the real game + example mod and
no-op automatically when those paths are absent. To enable them, copy
`.env.example` to `.env` and fill in the paths (or set `EU4_GAME_DIR`,
`EU4_CONFIG_DIR`, and `EU4_EXAMPLE_MOD_DIR` in the environment, which takes
precedence). `.env` is git-ignored.

```bash
cp .env.example .env   # then edit the paths
dotnet test
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full development workflow.

## License

[MIT](LICENSE) covers the **source code of this tool only**. This project links
[CWTools](https://github.com/cwtools/cwtools), which is also MIT-licensed.

## Disclaimer

- **Not affiliated with Paradox Interactive.** This is an unofficial, fan-made
  tool. It is not developed, endorsed, sponsored by, or affiliated with Paradox
  Interactive AB. *Europa Universalis IV*, EU4, and all related names, assets,
  and trademarks are the property of Paradox Interactive.
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

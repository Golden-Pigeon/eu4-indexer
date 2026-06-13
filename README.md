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

CWTools is referenced from local source (`../cwtools/CWTools/CWTools.fsproj`,
0.6.0-alpha) because the published NuGet (0.3.0, 2019) predates the rule format
used by the current EU4 config repo.

## Prerequisites

- .NET 9 SDK
- A checkout of [`cwtools`](https://github.com/cwtools/cwtools) at `../cwtools`
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
  untouched.
- Override tables: `file_overrides`, `entity_overrides`, `loc_overrides`,
  unified by the `v_override_summary` view (level + kind + winner/loser source).
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

## Testing

```bash
dotnet test
```

Unit tests run anywhere. Integration tests index the real game + example mod and
no-op automatically when those paths are absent; point them with the
`EU4_GAME_DIR`, `EU4_CONFIG_DIR`, and `EU4_EXAMPLE_MOD_DIR` environment variables.

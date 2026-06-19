# Database Schema

The index is a single SQLite database (or a mirrored PostgreSQL one). This
document describes the tables, the override graph, the derived `refs` causal
graph, the views, and full-text search. `Eu4Indexer.Core/Database/Schema.fs` is
the authoritative source for the SQLite DDL; `PostgresSchema.fs` mirrors it.

The schema is **mostly shared across games**: every game gets the same core
infrastructure tables, plus a small set of game-specific *detail* tables (EU4:
`mission_*`; HOI4: `focus_*`).

## Versioning and provenance

- `PRAGMA user_version` carries the schema version (`Schema.UserVersion`,
  currently `3`). The MCP server refuses to serve an index whose version doesn't
  match the binary.
- The `meta` table holds run provenance (key/value): app version, game id,
  indexing timestamp, etc.

## Core tables

| Table | What it holds |
|---|---|
| `sources` | base game + mods, with `kind`, `load_order` (unique), and descriptor metadata (`mod_version`, `supported_version`, `remote_file_id`, …). |
| `source_tags` / `source_dependencies` / `source_replace_paths` | per-source descriptor lists. |
| `files` | every file from every source. Shadowed losers are retained with `is_effective = 0`. Carries `content_hash`, `byte_size`, and `parse_status` (`ok`/`error`/`skipped`). |
| `parse_errors` | one row per parse failure (message + line/col). The run never aborts on a single bad file. |
| `symbols` | trigger/effect/modifier dictionary distilled from the `.cwt` config; unique on `(kind, name)`. |
| `config_types` | the CWTools `type` definitions (paths, name field, etc.) that drive generic extraction. |
| `entities` | generic table keyed by `entity_type` + `entity_key`, with source/file location (`start_line`/`end_line`/`stmt_index`), `subtypes`, full `raw_text`, and `is_effective`. |
| `script_nodes` | the recursive condition/effect tree: one row per clause/leaf/value, with `context` (`trigger`/`effect`/`mtth`/`ai_chance`/`metadata`), `depth`/`parent_id`/`sort_order`, `key`/`operator`/`value`/`value_kind`, and a `symbol_id` tagging known triggers/effects/modifiers. |
| `modifier_values` | flat `(entity_id, modifier_key) → value` pairs for modifiers defined inline in an entity body, with an optional `symbol_id`. |
| `entity_localisation` | `(entity_id, role) → loc_key` (e.g. an event's `title`/`desc`). |
| `defines` | game constants per source (`define_key`, `value`, `source_id`); later sources win (see `v_effective_defines`). |
| `localisation` | every language entry — see below. |

### Localisation

`localisation` holds every language entry, with `is_replace` and `is_effective`.
Two pieces of CJK/markup handling matter:

- **Special-escape decoding.** Non-Latin mods (e.g. Chinese) that hide their text
  in the `l_english` slot using EU4's
  [special-escape](https://gist.github.com/bruceCzK/96ad6e054111f929ed67291552d36334)
  encoding are decoded back to real UTF-8 on the way in; ASCII/Latin text is
  untouched.
- **Markup stripping.** A `value_plain` column holds the value with inline
  formatting markup (`§` colour codes, `£` icons) removed, so colour-split CJK
  text stays searchable. `loc_fts` indexes `value_plain` with the trigram
  tokenizer.

## Override tables

Override relationships are recorded explicitly at three levels:

| Table | Level |
|---|---|
| `file_overrides` | file shadow / `replace_path` |
| `entity_overrides` | entity redefinition / file shadow / `replace_path` |
| `loc_overrides` | localisation later-source / replace-dir / duplicate / shadow |

Each row names the winner and loser (`winner_source_id` / `loser_source_id`, plus
the specific file/entity/loc ids) and an `identical_content` flag. The
`v_override_summary` view unifies all three into `(level, kind, what,
winner_source_id, loser_source_id, identical_content)`.

## `refs` — the causal graph

`refs` is a derived cross-reference graph: one row per reference a script node
makes to another piece of content. It powers "what triggers X" and "what would
reach goal Y".

- `ref_kind` ∈ `fires_event` | `sets_flag` | `checks_flag` | `sets_variable` |
  `checks_variable` | `applies_modifier` | `checks_modifier` |
  `calls_scripted_trigger` | `calls_scripted_effect` | `on_action_fires`
- `target_type` ∈ `event` | `country_flag` | `global_flag` | `province_flag` |
  `ruler_flag` | `variable` | `modifier` | `scripted_trigger` | `scripted_effect`
  — flags are **scope-qualified** so a `has_country_flag` check is not confused
  with a `set_global_flag` set.
- `from_context` records where the reference sits (`trigger`/`effect`/`mtth`/
  `option_trigger`/`option_effect`); `option_node_id` points at the enclosing
  event-option clause when applicable; `negated` flags conditions inside a `NOT`.

## Per-game detail tables

Core types also get typed detail tables. The set depends on the game:

**Shared:** `event_details`, `event_options`, `decision_details`.

**EU4:** `mission_details`, `mission_requirements`.

**HOI4:** `focus_details` (tree id, icon, x/y, relative position),
`focus_requirements` (prerequisite focuses). HOI4 `event_details.event_kind`
covers `country`/`news`/`state`/`unit_leader`/`operative_leader` (EU4 uses
`country`/`province`).

## Views

| View | Purpose |
|---|---|
| `v_effective_entities` | effective entities joined to their source name and relative path. |
| `v_effective_loc` | localisation rows with `is_effective = 1`. |
| `v_effective_defines` | each define resolved to the winning (highest load-order) source. |
| `v_override_summary` | all three override levels unified. |
| `v_modifier_grants` | unifies the two ways an entity grants a modifier: `applied` as an effect (`add_*_modifier = { name = M }`, from `refs`) vs `defined` inline (from `modifier_values`). |

## Full-text search

| FTS table | Over | Tokenizer |
|---|---|---|
| `loc_fts` | `localisation.value_plain` | `trigram` (CJK substrings findable) |
| `entity_fts` | `entities.raw_text` | `unicode61` with `_` as a token char, so identifiers like `set_country_flag` stay whole |

## PostgreSQL export

The Postgres schema (`PostgresSchema.fs`) mirrors the SQLite one table-for-table
and column-for-column, so the same positional INSERTs work. Differences:

- Keys are `INTEGER` (int4) to match the Int32 ids the indexer assigns;
  auto-assigned override/option/ref ids use `GENERATED … AS IDENTITY`.
- Foreign keys are `DEFERRABLE` (checked at commit) to allow the same bulk-load
  insert order as SQLite.
- Full-text search is replaced by `pg_trgm` GIN indexes over `value_plain` and
  `raw_text` — the same CJK substring matching as the SQLite trigram tokenizer
  (`value_plain ILIKE '%幻梦之森%'` stays fast). Skip with `--no-fts` (needs
  `CREATE EXTENSION pg_trgm`).
- On export, only eu4-indexer's own tables are dropped (`CASCADE` removes the
  dependent views) and rebuilt; other objects in the database are untouched.
- The MCP server reads SQLite only; the Postgres export is for your own SQL / BI
  / `pgvector` use.

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

-- Everything that fires event 'flavor_eng.1', and what fires it
SELECT from_entity_id, from_context FROM refs
WHERE ref_kind = 'fires_event' AND target_type = 'event' AND target_key = 'flavor_eng.1';

-- Flags checked but never set (candidate dangling references)
SELECT DISTINCT target_type, target_key FROM refs
WHERE ref_kind = 'checks_flag'
  AND target_key NOT IN (SELECT target_key FROM refs WHERE ref_kind = 'sets_flag');
```

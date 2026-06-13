---
name: eu4-indexer
description: >
  Query and reason over a Europa Universalis IV (EU4) script index through the
  `eu4` MCP server: explain events/missions/decisions and their conditions and
  options, trace what triggers what, find likely bugs, and work out how to reach
  a goal. Use whenever the user asks about EU4 (or a loaded mod) game content —
  events, missions, decisions, modifiers, flags, mod overrides, localisation —
  or asks "how do I achieve X", "why can't I do Y", or "is this a bug".
---

# EU4 Indexer

This skill drives the `eu4` MCP server, which exposes a read-only SQLite index of
the EU4 base game plus any loaded mods (built by `Eu4Indexer.Cli`). The index has
already resolved mod overrides and decoded localisation, and it carries a derived
**reference graph** (`refs`) of how content connects.

If the `eu4` tools are not available, tell the user to build an index and register
the server (see "Setup" at the end) instead of guessing from training data.

**Call `describe_schema` once at the start** of a session to see which
`entity_type` and `ref_kind` values exist, the languages present, and the DDL.

## How EU4 content connects (mental model)

- **Events** are keyed `namespace.id`. They fire one of three ways: `triggered_only`
  (must be fired by another event/decision/on_action), `mean_time_to_happen` (fires
  on its own over time while conditions hold), or from an **on_action** engine hook.
  Each event has **options**; an option may carry a `trigger` block (its visibility
  condition) and the rest of the option body is its **effects**.
- **Decisions** are player-clicked, gated by `potential` + `allow` conditions, with an `effect`.
- **Missions** have a `trigger` (completion condition) and an `effect` (reward), plus
  `required_missions` forming a prerequisite chain.
- **Flags** are the backbone of game state and are **scope-qualified**:
  `country_flag` / `global_flag` / `province_flag` / `ruler_flag`
  (`set_*_flag` / `clr_*_flag` / `has_*_flag`). A country-flag check is unrelated to a
  global-flag set even if they share a name.
- **Variables**: `set_variable` / `check_variable`.
- **Modifiers** are either *applied* as an effect (`add_country_modifier = { name = M }`)
  or *defined inline* in an idea/reform body. `v_modifier_grants` unifies both.
- **Scripted triggers/effects** are reusable named conditions/effects; expand them with
  `resolve_symbol`.
- **Overrides**: later mods override earlier mods and the base game, at three levels
  (file / entity / localisation). Tools return **effective** (winning) rows by default.
- **Localisation** values contain `§` colour codes and `£` icons; search runs against a
  markup-stripped, CJK-friendly column. Non-Latin mods often hide their text in the
  `l_english` slot.

## Tools

| Tool | Use |
|---|---|
| `describe_schema` | Data dictionary + DDL. Start here. |
| `explain_entity` | One entity: conditions, options (condition vs effects), inbound + outbound refs. |
| `what_triggers` | Reverse: what fires/references this (and an event's firing model). |
| `what_does_it_do` | Forward: what this fires/sets/checks/applies/calls. |
| `find_by_condition` | What is gated by a flag/variable/scripted trigger. |
| `trace_to_goal` | Backward-chain candidate action sequences to reach an event/flag/variable. |
| `find_dangling` | Flags checked but never set, events fired but undefined — candidate bugs. |
| `search_everything` | Cross-type search when you don't know the content type. |
| `search_localisation` | Text search (markup-stripped, CJK-friendly). |
| `resolve_symbol` | Explain a trigger/effect; expand a scripted definition. |
| `list_sources` / `get_overrides` | Load order; who overrode what. |
| `read_query` | Escape hatch: a single read-only SELECT for shapes the tools don't cover. |

## Workflow 1 — Explain an event/mission/decision fully

1. `explain_entity(entityType, entityKey)`. Read the script tree by each node's
   `context`: `trigger` nodes are conditions, `effect` nodes are what happens, options
   split into `option_trigger` (when the option is shown) and `option_effect`.
2. For "what makes this happen", read `triggeredBy` (or call `what_triggers` for the
   firing model: triggered-only vs MTTH vs on_action).
3. Expand any scripted trigger/effect named in the conditions with `resolve_symbol`.
4. Localised title/description text is in the returned `localisation`.

## Workflow 2 — Explain a phenomenon / find a bug (type unknown)

1. `search_everything(text)` (or `search_localisation` if it's UI text) to find the
   entity behind the phenomenon.
2. `explain_entity` on the candidate; read its conditions.
3. If a condition is gated by a flag/variable, use `find_by_condition` to see what else
   depends on it, and check whether it is ever produced. A flag that `find_dangling`
   reports as **checked but never set** (or an event fired but undefined) is a strong
   "unreachable / likely bug" signal — but confirm it isn't engine-set or dynamically
   named first.

## Workflow 3 — Can I reach goal X, and how?

1. `trace_to_goal(targetKind, targetKey[, flagScope])` where `targetKind` is
   `event` | `flag` | `variable`. You get candidate chains ordered **base action →
   goal**, e.g. *click decision D → it fires event E → E sets flag F*.
2. **For each step's entity, call `explain_entity` to read its REAL conditions.** The
   chain only models settable state; it does **not** model non-symbolic prerequisites
   (country tag, date, province ownership, being at peace, ruler stats). Surface those
   to the user as "you also need …".
3. Present the sequence as concrete in-game steps, with the unmodelled prerequisites
   called out.

## Boundaries & gotchas

- `trace_to_goal` is **bounded symbolic backward-chaining, not a full planner**. It chains
  flags/variables/events/decisions/missions; everything else is a precondition to verify
  via `explain_entity`. It caps depth and path count (`truncated` flags this).
- `find_dangling` is **heuristic**: engine-set flags, dynamically-named targets
  (`$FLAG$`), and hardcoded engine events appear as false positives.
- Results are **effective-only** (override winners) by default; mod conflicts are already
  resolved. Use `get_overrides` to see what a mod changed.
- **Search vs identifiers**: use `search_localisation` / `search_everything` for prose;
  for exact script identifiers prefer the entity/graph tools or `read_query` on
  `script_nodes.key` / `script_nodes.value`. `value_plain` is the searchable text; the
  raw `value` keeps colour codes.
- **Numeric trigger quirk**: in EU4 a numeric condition written `x = 5` usually means
  `x >= 5`. Keep this in mind when explaining conditions.

## Setup (if the server isn't connected)

Build an index, then register the server:

```bash
dotnet run -c Release --project Eu4Indexer.Cli -- index \
  --game-dir /path/to/eu4 --mod /path/to/mod \
  --config-dir /path/to/cwtools-eu4-config --db eu4.db
```

```json
{ "mcpServers": { "eu4": {
  "command": "dotnet",
  "args": ["run", "--project", "/abs/path/Eu4Indexer.Mcp", "--", "--db", "/abs/path/eu4.db"]
} } }
```

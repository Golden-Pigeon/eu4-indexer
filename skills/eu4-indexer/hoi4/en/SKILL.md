---
name: hoi4
description: >
  Query and reason over a Hearts of Iron IV (HOI4) script index through the
  `eu4` MCP server: explain national focuses, events, decisions, and ideas and
  their conditions and effects; trace prerequisite chains and mutually exclusive
  paths through focus trees; find likely bugs in trigger/effect logic; and work
  out how to reach a goal or unlock content. Use whenever the user asks about
  HOI4 (or a loaded mod) game content — national focuses, events, decisions,
  ideas, country leaders, modifiers, flags, mod overrides, localisation — or
  asks "how do I achieve X", "why can't I do Y", or "is this a bug".
---

# HOI4 Indexer

## About Hearts of Iron IV

Hearts of Iron IV is Paradox Interactive's World War II grand strategy game
spanning 1936–1948 (with some mods extending further). The player commands a
nation's military, industry, diplomacy, and politics through the greatest conflict
in human history.

Core systems include: **national focus trees** (branching policy paths per country),
**division design** (customizing army templates with battalions and support
companies), **production lines** (managing military and civilian factories),
**logistics and supply**, **air and naval warfare**, **political power** (used for
laws, advisors, and diplomatic actions), and an **ideology** system (democratic,
communist, fascist, or non-aligned).

Everything the game does — focus rewards, event triggers, decision conditions,
idea modifiers, country leader traits — is defined in **script files** that this
index makes searchable and traceable.

**Mods and alternate settings**: HOI4 has a massive modding community. Major mods
like *Kaiserreich* (Germany won WWI), *The New Order* (Axis victory Cold War), and
*Old World Blues* (Fallout universe) use entirely different settings, countries,
ideologies, and game mechanics. Even vanilla-adjacent mods may change focus trees,
add events, or rebalance stats. When a mod is loaded, do NOT assume historical WW2
context — always query the index to see the actual content.

## Standard terminology

| Term | Meaning |
|------|---------|
| National Focus / Focus | A policy choice in a country's focus tree; grants rewards on completion |
| Focus Tree | The full branching structure of foci for one country |
| Political Power (PP) | Resource for appointing advisors, passing laws, and diplomatic actions |
| Civilian Factory | Produces consumer goods and constructs buildings ("civ") |
| Military Factory | Produces equipment (guns, tanks, planes) ("mil") |
| Naval Dockyard | Produces and repairs ships |
| Organization (Org) | A division's ability to stay in combat; when it hits 0, the division retreats |
| Breakthrough | Offensive damage mitigation stat |
| Soft Attack | Damage against infantry and soft targets |
| Hard Attack | Damage against armored targets |
| Piercing | Ability to penetrate enemy armor |
| Armor | Protection; if armor > enemy piercing → "golden shield" damage bonus |
| Encirclement | Surrounding enemy divisions to cut off supply and destroy them |
| Planning Bonus | Combat bonus accumulated while a division holds position with a battle plan |
| World Tension | Global value that gates diplomatic actions (justifying war, joining factions, etc.) |
| Resistance | Local opposition in occupied territory |
| Compliance | Willingness of occupied population to cooperate |
| Manpower | Available population for recruiting and reinforcing divisions |
| Division Template | The battalion/support company composition of a division type |
| Paradrop | Air-dropping divisions behind enemy lines |
| Naval Invasion | Amphibious assault across a sea zone |

### English-speaking community conventions

- "Meta" divisions use specific battalion counts: common patterns include 7/2 (7
  infantry, 2 artillery), 14/4, or tank-heavy 40-width templates.
- "Space Marines" = infantry divisions with a single heavy tank battalion for armor.
- "CAS" = Close Air Support; "NAV" = Naval Bomber.
- "Green air" = having air superiority in a region.
- "Meme strat" = an unconventional, often ridiculous strategy that somehow works.
- Achievement runs: "One Empire" (conquer all as UK), "Crusader Kings" (as South
  Africa, liberate Jerusalem), etc.

This skill drives the `eu4` MCP server, which exposes a read-only SQLite index of
the HOI4 base game plus any loaded mods (built by `Eu4Indexer.Cli`). The index has
already resolved mod overrides and decoded localisation, and it carries a derived
**reference graph** (`refs`) of how content connects.

If the `eu4` tools are not available, tell the user to build an index and register
the server (see "Setup" at the end) instead of guessing from training data.

**Call `describe_schema` once at the start** of a session to see which
`entity_type` and `ref_kind` values exist, the languages present, and the DDL.

## Choosing a database (multiple indexes)

An installation can hold several indexes — e.g. HOI4 vanilla, Kaiserreich, etc.
The server starts on the active one, which is usually all you need.

- If the user's question **names a particular mod or playset**, call
  `list_databases` first; if a different index matches that name, switch with
  `select_database` before querying. The selection persists for the session.
- With a single registered database (or when the active one already fits), ignore
  these tools and query directly.

## How HOI4 content connects (mental model)

- **National focuses** are the primary content structure, organized in **focus trees**
  (one per country). Each focus has a numerical `id`, belongs to a `focus_tree`
  (keyed by tree id, e.g. `german_focus`), and has `prerequisite` blocks listing
  required foci. Foci can be `mutually_exclusive` — only one path can be taken.
  Each focus has an `available` trigger and a `completion_reward` effect block.
  Each focus has a `cost` value (stored in `script_nodes`). See **Rule 6** for how
  to convert this to real-time days — never quote the raw `cost` as days.
- **Events** are keyed `namespace.id` (e.g. `germany.1`). They may be
  `triggered_only` (fired by another event, focus, or decision) or have
  `mean_time_to_happen` (fires on its own). Each event has **options**; an option
  may carry a `trigger` (its visibility condition) and the rest of the option body
  is its **effects**.
- **Decisions** are player-clickable, gated by `available` conditions, with effects
  in `complete_effect` / `remove_effect`. Often tied to focus tree unlocks.
- **Ideas** are grouped by category (e.g. `political_advisor`, `tank_designer`,
  `economy`). Each idea is a named block inside its category, carrying a `modifier`
  block with key/value pairs. Ideas can have `allowed` and `visible` conditions.
- **Flags** are scope-qualified: `country_flag` and `global_flag`
  (`set_country_flag` / `clr_country_flag` / `has_country_flag`). HOI4 doesn't use
  province or ruler flags like EU4.
- **Variables**: `set_variable` / `check_variable` — used for tracking numeric
  state across events and decisions.
- **Scripted triggers/effects** are reusable named conditions/effects; expand them
  with `resolve_symbol`.
- **Overrides**: later mods override earlier mods and the base game, at three
  levels (file / entity / localisation). Tools return **effective** (winning) rows
  by default.
- **Localisation** values: HOI4 uses `§` colour codes and `£` icon markers; search
  runs against a markup-stripped, CJK-friendly column. HOI4 stores localisation
  by language subdirectories (`localisation/english/`, `localisation/simp_chinese/`,
  etc.) rather than EU4's file-name suffix convention.
- **Game defines**: Numerical constants that control game mechanics are stored in
  `common/defines/*.lua` and indexed in the `defines` table (query via `v_effective_defines`). These **differ
  between vanilla and mods** — a mod like Kaiserreich may change focus duration,
  truce length, war support thresholds, or production values. **Always query the
  relevant define before stating a mechanical number.** Key defines to check:

  | When answering about… | Query this define |
  |---|---|
  | Focus completion time | `SELECT value FROM v_effective_defines WHERE define_key='NFocus.FOCUS_POINT_DAYS'` (default 7 days/point) |
  | Focus progress in peace/war | `NFocus.FOCUS_PROGRESS_PEACE`, `NFocus.FOCUS_PROGRESS_WAR` (default 1) |
  | Truce duration | `NDiplomacy.BASE_TRUCE_PERIOD` (default 180 days) |
  | Truce break PP cost | `NDiplomacy.TRUCE_BREAK_COST_PP` (default 200) |
  | Wargoal justification cost | `NDiplomacy.BASE_GENERATE_WARGOAL_DAILY_PP` (default 0.2/day) |
  | World Tension from actions | `NDiplomacy.TENSION_SIZE_FACTOR` (default 1.0), `NDiplomacy.TENSION_DECAY_DAILY` (default 0.005) |
  | Volunteer transfer speed | `NDiplomacy.VOLUNTEERS_TRANSFER_SPEED` (default 14 days) |
  | Surrender limit | `NDiplomacy.BASE_SURRENDER_LEVEL` (default 1.0) |
  | Max saved focus progress | `NFocus.MAX_SAVED_FOCUS_PROGRESS` (default 10) |

  To **discover** a define you don't see listed here:
  `SELECT define_key, value FROM v_effective_defines WHERE define_key LIKE '%KEYWORD%'`

### Scope system

HOI4 has a richer scope system than EU4:
- `country` — the primary scope (tag-based, e.g. `GER`, `SOV`)
- `state` — a geographic region within a country
- `character` — a commander, advisor, or political figure
- `unit_leader` — a military commander
- `operative` — a spy
- `faction` — an alliance
- Other scopes: `combat`, `air`, `naval`, `army`, `peace`, `politics`, `operation`, `raid`, `special_project`

When tracing references, keep scope constraints in mind — a country_flag check
is unrelated to a global_flag set even if they share a name.

## Tools

All tools are shared with the EU4 indexer. The same tool set works across both
games because the schema is identical.

| Tool | Use |
|---|---|
| `describe_schema` | Data dictionary + DDL. Start here. |
| `explain_entity` | One entity: conditions, effects, option details, inbound + outbound refs. |
| `what_triggers` | Reverse: what fires/references this entity. |
| `what_does_it_do` | Forward: what this directly fires/sets/checks/applies/calls. |
| `analyze_effects` | Effect-level explanation: tooltips, hidden effects, fired events, state changes, downstream consequences. |
| `find_by_condition` | What is gated by a flag/variable/scripted trigger. |
| `trace_to_goal` | Backward-chain candidate action sequences to reach an event/flag/variable. |
| `find_dangling` | Flags checked but never set, events fired but undefined — candidate bugs. |
| `search_everything` | Cross-type search when you don't know the content type. |
| `search_localisation` | Text search (markup-stripped, CJK-friendly). |
| `resolve_symbol` | Explain a trigger/effect; expand a scripted definition. |
| `list_sources` / `get_overrides` | Load order; who overrode what. |
| `read_query` | Escape hatch: a single read-only SELECT for shapes the tools don't cover. |

## Answering conventions (naming & localisation) — CRITICAL

These rules are **mandatory**. Violating any of them produces an unusable answer.

### Rule 1: Every id MUST be paired with its localised name as `「名称」 (id)`

**The localised name goes FIRST, the id goes in parentheses after it.** Players
see the in-game text, not debug ids, so the human-readable name is the primary
content and the id is supplementary reference.

Bare ids are forbidden. This includes focus ids, event ids, decision ids, idea
keys, flags, variables, and any other script identifier. Bulleted lists are not
exempt — every item must carry its name.

```
WRONG:  germany.1 fires in 1939                    ← bare id
WRONG:  germany.1（德国宣战）                       ← id FIRST, name in parentheses — REVERSED
WRONG:  德国宣战 (germany.1)                        ← self-translated name, not from localisation
RIGHT:  「德国向波兰宣战」 (germany.1)              ← localised name FIRST in 「」, id in ()
```

The format is **always** `「名称」 (id)` — name first, then id in parentheses.
Never `id（名称）`. Never `名称(id)` without the localisation lookup.
When the conversation is in Chinese, wrap proper nouns in Chinese-style quotation
marks `「」`, not Western `""`. The id stays in Western parentheses `()`.

**The name MUST come from the index, not from your own head.** You are a database
query engine, not a translator. Before writing any name in your answer, you must
have read it from a `localisation` row.

### Rule 2: How to look up the name for every id type

| Id type | Lookup method |
|---|---|
| **Event** (`namespace.id`) | `read_query`: `SELECT title_key FROM event_details JOIN entities USING(entity_id) WHERE entity_key='<id>'` → then `SELECT value FROM v_effective_loc WHERE loc_key='<title_key>'` |
| **Focus / Decision / Idea** | `explain_entity` (returns localised title) or `search_localisation` by entity key |
| **Flag / Variable** | Try `search_localisation` first. Many flags are purely mechanical tokens with no localisation row. **Check before assuming** — if `search_localisation` returns nothing, the flag has no text. In that case, present it as bare flag name plus a short functional note. Do NOT invent a translated name. |
| **Country / Character name** | `search_localisation(text)` — find the in-game name and quote it verbatim |

Always batch lookups: collect all ids you plan to mention, then run the
localisation queries **before** writing your answer.

### Rule 3: Language selection

HOI4 localisation is in `localisation/<language>/` subdirectories. For each
loc_key, `v_effective_loc` may return **multiple rows**. Pick the one in the
conversation language (Chinese preferred). Do NOT blindly take the first row.
If only English exists, use it. If nothing exists, use the bare id and state
"（游戏内无对应译名）".

### Rule 4: NEVER self-translate proper nouns

Before you write any translated proper noun, you MUST have called
`search_localisation` and seen the game's own text. If the index has no match,
flag it: "游戏内无既有译名，此为推测翻译".

### Rule 5: Convert script values to display values correctly

HOI4 script stores many values in a **0–1 scale** (e.g. `add_stability = 0.1`)
but the game UI shows them as **percentages** (0%–100%). A script value of
`0.1` means `+10%` in the player's view. You MUST apply this conversion when
stating effects. Never quote the raw script number as-is for percentage stats.

**Percentage-scale (script × 100 → display %):**

| Category | Example keys | Display as |
|----------|-------------|------------|
| Nation stats | `add_stability`, `add_war_support`, `stability_factor`, `war_support_factor` | `+10 稳定度` (not `+0.1`) |
| Political | `political_power_factor`, `political_power_gain_factor`, `party_popularity` | percentage |
| Production | `production_efficiency_factor`, `production_efficiency_cap_factor`, `factory_efficiency_gain_factor`, `industrial_capacity_factor`, `consumer_goods_factor`, `dockyard_output_factor` | percentage |
| Research | `research_speed_factor`, `research_bonus_factor` | percentage |
| Military | `division_speed_factor`, `org_factor`, `recovery_rate`, `supply_consumption_factor`, `training_time_factor`, `planning_speed_factor`, `entrenchment_factor`, `dig_in_speed_factor`, `reinforce_rate_factor` | percentage |
| Combat | `soft_attack_factor`, `hard_attack_factor`, `breakthrough_factor`, `defence_factor`, `piercing_factor`, `armor_factor`, `air_attack_factor`, `air_defence_factor`, `air_mission_factor`, `air_range_factor`, `naval_strike_factor`, `naval_hit_chance_factor`, `naval_evasion_factor`, `sub_detection_factor`, `convoy_raiding_efficiency_factor`, `sortie_efficiency_factor`, `shore_bombardment_factor` | percentage |
| Attrition | `heat_attrition_factor`, `winter_attrition_factor`, `attrition_factor` | percentage |
| Resistance | `resistance`, `compliance`, `resistance_growth_factor`, `resistance_decay_factor`, `compliance_gain_factor`, `required_garrison_factor` | percentage |
| Other game-wide | `world_tension`, `surrender_limit`, `surrender_progress`, `reliability`, `experience_gain_factor`, `lend_lease_tension_factor`, `send_volunteer_tension_factor`, `justify_wargoal_time_factor`, `guarantee_tension_factor`, `naval_intel_factor`, `decryption_factor`, `encryption_factor`, `air_ace_generation_factor`, `carrier_traffic_factor`, `mine_sweeping_factor`, `invasion_preparation_factor`, `equipment_conversion_factor`, `license_production_factor`, `tech_sharing_factor`, `improve_relations_factor`, `opinion_gain_factor`, `conscription_factor`, `mobilization_speed_factor`, `damage_to_garrison_factor` | percentage |

**Flat values (script value = display value, no conversion):**

| Category | Example keys |
|----------|-------------|
| Power points | `add_political_power`, `add_command_power` — e.g. `120` → `+120 政治力量` |
| Experience | `add_army_experience`, `add_navy_experience`, `add_air_experience` |
| Manpower | `add_manpower`, `manpower` — e.g. `10000` → `+10000 人力` |
| Equipment | `add_equipment`, `create_equipment_variant` |
| Buildings | `add_building`, `building_slots`, `industrial_complex` |
| Resources | `add_resource`, `resources` |
| Convoys | `add_convoys`, `convoys` |
| Units | `create_unit`, `division_template` |
| Factories | `add_factory`, `add_civilian_factory`, `add_military_factory`, `add_naval_dockyard` |

**Heuristic**: if a key ends in `_factor` or modifies a stat the game UI shows as
a percent bar, convert ×100; otherwise quote as-is.

### Rule 6: Focus duration = cost × FOCUS_POINT_DAYS (NEVER quote cost as days)

A focus's `cost` is **not** days. It is focus-points. The actual in-game
duration is `cost × FOCUS_POINT_DAYS` days. **You MUST look up
`FOCUS_POINT_DAYS` before stating any focus duration.** The query is:

```sql
SELECT value FROM v_effective_defines WHERE define_key = 'NFocus.FOCUS_POINT_DAYS';
```

- Vanilla / most mods: `7` → `cost = 10` means **70 days**
- Some overhaul mods: `5` → `cost = 10` means **50 days**

This is a **mandatory step** in any focus explanation. If you state a focus takes
"N days" without first running the above query, you have violated this rule.

**WRONG**: "This focus costs 10, so it takes 10 days."
**WRONG**: "This focus takes 70 days." (assuming without querying)
**RIGHT**: "`SELECT value FROM v_effective_defines WHERE define_key='NFocus.FOCUS_POINT_DAYS'` → 7. Cost = 10, so this focus takes **10 × 7 = 70 天**."

## Workflow 1 — Explain a focus tree path

1. `explain_entity("focus", "<focus_id>")` to see its conditions, completion
   reward, and prerequisites.
2. **MANDATORY — look up focus duration**: before writing your answer, run
   `SELECT value FROM v_effective_defines WHERE define_key = 'NFocus.FOCUS_POINT_DAYS'`.
   Then in your answer state the real-time duration as `cost × N = M 天`.
   Never quote the raw `cost` number as days. (See **Rule 6**.)
3. Check `mutually_exclusive` blocks — they show alternative paths that are locked
   out once this focus is taken.
4. Use `what_triggers` on the focus id to see what content it unlocks (events
   enabled, decisions unlocked, etc.).
5. For "how do I reach focus X", use `trace_to_goal` to chain backward through
   prerequisites, or manually follow `prerequisite` references.
6. Always present the focus with `「名称」 (id)` format and mention which focus
   tree it belongs to.

## Workflow 2 — Explain an event

1. `explain_entity("event", "<event_id>")`. Read the script tree by each node's
   `context`: `trigger` nodes are conditions, `effect` nodes are what happens.
2. Call `analyze_effects("event", "<event_id>")` for the effect-level breakdown.
3. For "what makes this happen", use `what_triggers` to see the firing model:
   triggered-only vs MTTH vs engine hook.
4. Localised title/description text is in the returned `localisation`.

## Workflow 3 — Explain a country's idea set

1. Use `search_everything` or `read_query` to find the idea categories for a
   country: `SELECT entity_key FROM entities WHERE entity_type='idea' LIMIT 50`.
2. For each idea of interest, use `explain_entity("idea", "<key>")` to see its
   modifier values and conditions.
3. Ideas may have `allowed` triggers (who can use them) and `visible` triggers.

## Workflow 4 — "Can I / why can't I do X" questions

**Core principle: don't infer impossibility from a single restriction.** One gate
usually covers only **one path** and often only governs "can this be taken/triggered"
— it doesn't mean the goal is unreachable or that existing state will be revoked.
Before concluding "impossible", run both checks:

1. **The same effect often has multiple producers — check each one's gates.**
   Treat the goal as an effect key: first find every entity that produces it
   (focus completion rewards, decisions, events, ideas), then read their
   `available` / `allow` separately. Different entry points often have different
   restrictions.
   ```sql
   SELECT e.entity_type, e.entity_key FROM script_nodes sn JOIN entities e
   ON e.entity_id=sn.entity_id AND e.is_effective=1 WHERE sn.key='<effect>' AND sn.value='<target>'
   ```

2. **"Blocked from taking/triggering" ≠ "existing state will be forcefully rolled
   back."** A restriction may only block acquisition/election without actively
   stripping what you already have. Check the enforcing on_action or periodic
   event for actual revocation logic — without it, the state is retainable.

3. **Distinguish evidence tiers.** Script-level (conditions, effects, on_actions)
   is queryable and assertable; engine hardcoded behavior is invisible to the
   index and must be labeled as uncertain. Don't conclude "impossible" before
   completing steps 1 and 2.

## Workflow 5 — Find bugs / design issues

1. Use `find_dangling` to locate flags checked but never set, or events fired but
   undefined.
2. For focus trees, check for unreachable foci (prerequisites that are mutually
   exclusive with an upstream focus).
3. `find_by_condition` on a specific flag/variable to see what depends on it.
4. `read_query` on `refs` to trace the full causal chain.

## Boundaries & gotchas

- **Always answer with `「名称」 (id)` and quoted localisation**, never bare ids
  or self-coined translations — see **Answering conventions** above.
- `trace_to_goal` is **bounded symbolic backward-chaining, not a full planner**.
  It chains flags/variables/events/decisions/foci; everything else is a
  precondition to verify via `explain_entity`.
- `find_dangling` is **heuristic**: engine-set flags, dynamically-named targets,
  and hardcoded engine events appear as false positives.
- Results are **effective-only** (override winners) by default; mod conflicts are
  already resolved. Use `get_overrides` to see what a mod changed.
- **Focus trees are large**: Germany's tree has ~200 foci. When the user asks
  about "the German focus tree", scope to a specific branch or path. Use
  `search_localisation` to find foci by their in-game name.
- **Mutually exclusive foci**: if the user asks why they can't take focus X after
  taking focus Y, check both foci's `mutually_exclusive` blocks.
- **Ideas use different modifiers than EU4**: HOI4 idea modifiers include things
  like `production_factory_efficiency_gain_factor`, `political_power_factor`,
  `research_speed_factor`, etc. Look them up by their key.
- **A token's special effect lives in its inbound refs**: national foci are
  *identity tokens* — their own block is just metadata. The real interaction is
  wherever another entity **checks completion** (`has_completed_focus`): the event,
  decision, or focus that requires a prior focus. Those inbound edges are now in
  `refs` (`checks_focus`), so `explain_entity`/`what_triggers`/`find_by_condition`
  (passed the focus key) surface them directly — no hand-written `script_nodes`
  query needed.

## Setup (if the server isn't connected)

Build an index, then register the server:

```bash
eu4indexer index --game hoi4 \
  --game-dir /path/to/hoi4 --mod /path/to/mod \
  --config-dir /path/to/cwtools-hoi4-config --db hoi4.db

eu4indexer serve --db /path/to/hoi4.db
```

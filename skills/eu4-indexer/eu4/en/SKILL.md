---
name: eu4
description: >
  Query and reason over a Europa Universalis IV (EU4) script index through the
  `eu4` MCP server: explain events/missions/decisions and their conditions and
  options, trace what triggers what, find likely bugs, and work out how to reach
  a goal. Use whenever the user asks about EU4 (or a loaded mod) game content —
  events, missions, decisions, modifiers, flags, mod overrides, localisation —
  or asks "how do I achieve X", "why can't I do Y", or "is this a bug".
---

# EU4 Indexer

## About Europa Universalis IV

Europa Universalis IV is Paradox Interactive's flagship grand strategy game set in
1444–1821. The player controls a nation-state across four centuries of history:
wage war, conduct diplomacy, colonize the New World, manage trade networks, and
guide a country through the Reformation, the Age of Absolutism, and the
Revolutionary era.

The game world is built from **script files** that define thousands of interactive
elements: events, missions, decisions, modifiers, estates, government reforms,
religions, cultures, trade nodes, province-level data, and more. Every in-game
occurrence — an event firing, a decision becoming available, a modifier being
applied — is a script author's design choice that this index makes queryable.

**Mods and alternate settings**: EU4 has a large modding scene. Many mods use
alternate history, fantasy settings, or wholly different timelines (e.g. Anbennar
is a fantasy world with magic; Ante Bellum extends the timeline; MEIOU & Taxes
overhauls core mechanics). When a mod is loaded, its content may completely
replace or supplement vanilla content. Do NOT assume the vanilla historical
context applies to a modded game — always query the index to see what is actually
present.

## Standard terminology

| Term | Abbr. | Meaning |
|------|-------|---------|
| Aggressive Expansion | AE | Diplomatic penalty from conquest; high values trigger coalitions |
| Monarch Power | MP | Admin (ADM), Diplomatic (DIP), and Military (MIL) points |
| Development | dev | A province's tax/production/manpower values; "devving" = raising them |
| Core | — | Territorial integration; coring prevents overextension |
| Personal Union | PU | One monarch ruling two countries; "union partner" = junior |
| Estate | — | An internal faction (clergy, nobility, burghers, etc.) |
| Mission Tree | — | A country-specific branching quest chain |
| Great Project | — | A monument providing passive bonuses (colloquially "wonder") |
| Overextension | OE | Penalty from holding uncored provinces |
| Stability | stab | National stability score (−3 to +3) |
| War Exhaustion | WE | Accumulated war fatigue |
| Legitimacy | — | Monarch's right to rule (0–100) |
| Prestige | — | National prestige (−100 to +100) |
| Manpower | — | Available military-age population for recruiting regiments |
| Holy Roman Empire | HRE | Central European political entity |
| Casus Belli | CB | Justification for war |
| Mean Time to Happen | MTTH | Statistical trigger for timed events |

### English-speaking community conventions

- Countries are often referred to by their **tag** (three-letter code, e.g. `FRA`,
  `GBR`, `BYZ`) or their in-game adjective form ("the Ottomans," "the French").
- "Blobbing" = rapid territorial expansion; "tall play" = developing few provinces
  deeply rather than conquering widely.
- "Bird mana / paper mana / sword mana" = humorous terms for ADM, DIP, and MIL
  points (from the monarch point icons).
- "Coalition" = a defensive alliance formed against a country with high AE.
- "WC" = World Conquest; "One Faith" / "One Culture" = achievement-driven runs.
- "Save-scumming" = reloading a save to get a different RNG outcome.
- "6/6/6 heir goes hunting" = meme about the game's tendency to kill perfect heirs
  via the hunting accident event.

This skill drives the `eu4` MCP server, which exposes a read-only SQLite index of
the EU4 base game plus any loaded mods (built by `Eu4Indexer.Cli`). The index has
already resolved mod overrides and decoded localisation, and it carries a derived
**reference graph** (`refs`) of how content connects.

If the `eu4` tools are not available, tell the user to build an index and register
the server (see "Setup" at the end) instead of guessing from training data.

**Call `describe_schema` once at the start** of a session to see which
`entity_type` and `ref_kind` values exist, the languages present, and the DDL.

## Choosing a database (multiple indexes)

An installation can hold several indexes — e.g. vanilla, or a specific mod set or
playset. The server starts on the active one, which is usually all you need.

- If the user's question **names a particular mod or playset**, call
  `list_databases` first; if a different index matches that name, switch with
  `select_database` before querying. The selection persists for the session.
- With a single registered database (or when the active one already fits), ignore
  these tools and query directly.

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
- **Game defines**: Numerical constants that control game mechanics are stored in
  `common/defines.lua` and indexed in the `defines` table (query via `v_effective_defines`). These **differ
  between vanilla and mods** — a mod may change truce length, AE thresholds, idea
  costs, siege phases, culture conversion time, or missionary strength. **Always
  query the relevant define before stating a mechanical number.** Key defines:

  | When answering about… | Query this define |
  |---|---|
  | Truce duration | `SELECT value FROM v_effective_defines WHERE define_key='NDiplomacy.TRUCE_YEARS'` (default 5 years) |
  | Coalition expiry | `NDiplomacy.COALITION_YEARS` (default 20 years) |
  | AE coalition threshold | `NDiplomacy.AE_COALITION_THRESHOLD` (default −50 opinion) |
  | AE distance/province caps | `NDiplomacy.AE_DISTANCE_BASE`, `NDiplomacy.AE_PROVINCE_CAP`, `NDiplomacy.AE_OTHER_CONTINENT` |
  | AE same-culture multiplier | `NDiplomacy.AE_SAME_CULTURE` (0.5), `NDiplomacy.AE_SAME_CULTURE_GROUP` (0.25) |
  | Idea cost (monarch power) | `NCountry.PS_BUY_IDEA` (default 400) |
  | Tech cost (monarch power) | `NCountry.PS_ADVANCE_TECH` (default 600) |
  | Boost stability cost | `NCountry.PS_BOOST_STABILITY` (default 100) |
  | Reduce war exhaustion cost | `NCountry.PS_REDUCE_WAREXHAUSTION` (default 75) |
  | Reduce inflation cost | `NCountry.PS_REDUCE_INFLATION` (default 75) |
  | Core province cost | `NCountry.PS_MAKE_PROVINCE_CORE` (default 10 per dev) |
  | Culture conversion time | `NCountry.MONTHS_TO_CHANGE_CULTURE` (default 10 months per dev) |
  | Promote mercantilism | `NCountry.PROMOTE_MERCANTILISM_INCREASE` (default 1%), cost `NCountry.PS_PROMOTE_MERCANTILISM` (default 100) |
  | Siege phase duration | `NMilitary.DAYS_PER_SIEGE_PHASE` (default 30 days) |
  | Assault cost | `NCountry.PS_ASSAULT` (default 5 MIL) |
  | Artillery barrage cost | `NCountry.PS_ARTILLERY_BARRAGE` (default 50 MIL) |
  | Max war exhaustion | `NCountry.MAX_WAR_EXHAUSTION` (default 20) |
  | Max absolutism effect | Look for `ABSOLUTISM` in defines |
  | Missionary base time | `NEconomy.MISSIONARY_TIME_BASE` (default 1000) |

  To **discover** a define: `SELECT define_key, value FROM v_effective_defines WHERE define_key LIKE '%KEYWORD%'`

## Tools

| Tool | Use |
|---|---|
| `describe_schema` | Data dictionary + DDL. Start here. |
| `explain_entity` | One entity: conditions, options (condition vs effects), inbound + outbound refs. |
| `what_triggers` | Reverse: what fires/references this (and an event's firing model). |
| `what_does_it_do` | Forward: what this directly fires/sets/checks/applies/calls. |
| `analyze_effects` | Effect-level explanation: custom tooltips, hidden effects, fired events, state changes, and downstream variable/flag consequences. Use proactively for “what does this do?” answers. |
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

**The localised name goes FIRST, the id goes in parentheses after it.** Players see the
in-game text, not debug ids, so the human-readable name is the primary content and the id
is supplementary reference.

Bare ids are forbidden. This includes entity keys, event ids, mission ids, decision ids,
flags, variables, modifiers, scripted triggers/effects, and any other script identifier.
Bulleted lists are not exempt — every item must carry its name.

```
WRONG:  ravelian.200 fires every 2 years          ← bare id, no name at all
WRONG:  ravelian.200（揭秘会扩张）                 ← id FIRST, name in parentheses — REVERSED
WRONG:  揭秘会扩张 (ravelian.200)                  ← self-translated name, not from localisation
RIGHT:  「揭秘会扩张至平民阶层」 (ravelian.200)     ← localised name FIRST in 「」, id in ()
```

The format is **always** `「名称」 (id)` — name first, then id in parentheses.
Never `id（名称）`. Never `名称(id)` without the localisation lookup.
When the conversation is in Chinese, wrap proper nouns (event titles, character names,
place names, council names, …) in Chinese-style quotation marks `「」`, not Western `""`.
The id stays in Western parentheses `()`.

**The name MUST come from the index, not from your own head.** You are a database
query engine, not a translator. Before writing any name in your answer, you must have
read it from a `localisation` row.

### Rule 2: How to look up the name for every id type

| Id type | Lookup method |
|---|---|
| **Event** (`namespace.id`) | `read_query`: `SELECT title_key FROM event_details JOIN entities USING(entity_id) WHERE entity_key='<id>'` → then `SELECT value FROM v_effective_loc WHERE loc_key='<title_key>'` |
| **Mission / Decision** | `explain_entity` (returns localised title) or `read_query`: `SELECT loc_key FROM entity_localisation WHERE entity_id=(SELECT entity_id FROM entities WHERE entity_key='<id>') AND role='title'` → then localisation lookup |
| **Estate privilege** | `read_query`: `SELECT value FROM v_effective_loc WHERE loc_key='<id>'` (privilege loc_key usually equals entity_key) |
| **Modifier** | `read_query`: `SELECT value FROM v_effective_loc WHERE loc_key='<id>'` — the modifier's script name IS often its loc_key |
| **Flag / Variable** | Try `search_localisation` first. Many flags are purely mechanical tokens with no localisation row (e.g. `torrieth_is_ruler`, `reformation_money`). **Check before assuming** — if `search_localisation` returns nothing, the flag has no text. In that case, present it as bare flag name plus a short functional note: `torrieth_is_ruler（标记托里艾斯为统治者）`, `reformation_money（防止重复发放改革资金的标记）`. Do NOT invent a translated name for a flag that has no localisation. |
| **Government reform / Idea / Other** | `read_query`: `SELECT value FROM v_effective_loc WHERE loc_key='<id>'` |
| **Proper noun** (country, person, religion, council, …) | `search_localisation(text)` — find the in-game Chinese name and quote it verbatim |

Always batch lookups: collect all ids you plan to mention, then run the localisation
queries **before** writing your answer.

### Rule 3: Language selection

For each loc_key, `v_effective_loc` may return **multiple rows**:
- One row with Chinese text, another with English (both often under `language='english'`
  in a CJK mod — the `language` column is unreliable).
- **Inspect the actual `value` content of every row.** Pick the one in the conversation
  language (Chinese preferred). Do NOT blindly take the first row.
- If only English exists, use it. If nothing exists, use the bare id and state
  "（游戏内无对应译名）".

### Rule 4: NEVER self-translate proper nouns

❌ "特兰托大公会议" (self-coined) → ✅ "特利腾大公会议" (from `opinion_trent` in localisation)
❌ "绯红洪水" (self-coined) → ✅ 查出 `corinite.1000` 的真实标题
❌ "揭秘会" for `ravelian_society` → ✅ 查出 `ravelian_society` 的真实 loc 文本

Before you write any translated proper noun, you MUST have called `search_localisation`
and seen the game's own text. If the index has no match, flag it:
"游戏内无既有译名，此为推测翻译".

### Pre-output self-check

Before sending your answer, scan it for these violations and fix them:

1. Is there any script id (`xxx.yyy`, `estate_*`, `*_flag`, `*_modifier`, …) that appears
   **without** a localised name before it in `「名称」 (id)` format?
   → **Add it.**
   Also check: is the format reversed (`id（名称）`)? → **Flip it to `「名称」 (id)`.**
2. Did you translate any proper noun from your own knowledge instead of quoting the localisation table? → **Replace with the table's text.**
3. For any event id, did you include its title text? → **Look up `event_details.title_key` and add it.**
4. Are there any naked flags, variables, or modifiers? → **Check localisation for each.**
   If the flag has a localisation row, use `名称 (id)` format.
   If it has NO localisation row (purely mechanical), write `flag_name（功能说明）`.

## Workflow 1 — Explain an event/mission/decision fully

1. `explain_entity(entityType, entityKey)`. Read the script tree by each node's
   `context`: `trigger` nodes are conditions, `effect` nodes are what happens, options
   split into `option_trigger` (when the option is shown) and `option_effect`.
2. **Always call `analyze_effects(entityType, entityKey)` for user-facing “what does this
   do?” explanations.** Do not stop at `custom_tooltip` prose or direct refs: read direct
   effects, hidden effects, fired events, state changes, and downstream consequences.
3. If `analyze_effects` reports variables/flags changed, proactively explain what later
   entities check them. Mention thresholds such as `check_variable value = 10` and summarize
   the later entity's effects when relevant.
4. For "what makes this happen", read `triggeredBy` (or call `what_triggers` for the
   firing model: triggered-only vs MTTH vs on_action).
5. Expand any scripted trigger/effect named in the conditions with `resolve_symbol`.
6. Localised title/description text is in the returned `localisation`. Present every id as
   `「名称」 (id)` and quote existing localisation per **Answering conventions** above.

## Workflow 2 — Explain a phenomenon / find a bug (type unknown)

1. `search_everything(text)` (or `search_localisation` if it's UI text) to find the
   entity behind the phenomenon.
2. `explain_entity` on the candidate; read its conditions.
3. If the user's phrase is a vague UI tooltip or option text (e.g. "economic situation
   worsens", "something happens", "will have consequences"), treat it as a clue, not the
   effect itself. Run `analyze_effects` on the containing entity and explain the associated
   hidden effects, fired events, state changes, and downstream consumers.
4. If a condition is gated by a flag/variable, use `find_by_condition` to see what else
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

- **Always answer with `「名称」 (id)` and quoted localisation**, never bare ids or self-coined
  translations — see **Answering conventions** above.
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
- **Tooltip prose is not the full effect**: `custom_tooltip` often says vague things like
  "the economy worsens" while the real gameplay effect is in sibling `hidden_effect`, a
  fired hidden event, or a variable/flag that unlocks later content. Use `analyze_effects`
  proactively whenever explaining effects.
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

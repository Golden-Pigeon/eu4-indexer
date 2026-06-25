namespace Eu4Indexer.Core.Database

/// SQLite DDL. Tables are created up front with only PK/UNIQUE constraints;
/// secondary indexes, FTS population and views are applied after bulk load
/// (see finalizeSql). Each game gets its own detail tables (EU4: mission_*,
/// HOI4: focus_*) but shares the core infrastructure tables.
module Schema =

    [<Literal>]
    let UserVersion = 4

    let private sharedTables =
        """
CREATE TABLE meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
) WITHOUT ROWID;

CREATE TABLE sources (
    source_id         INTEGER PRIMARY KEY,
    kind              TEXT NOT NULL CHECK (kind IN ('base_game','mod')),
    load_order        INTEGER NOT NULL UNIQUE,
    name              TEXT NOT NULL,
    root_path         TEXT NOT NULL,
    descriptor_path   TEXT,
    mod_version       TEXT,
    supported_version TEXT,
    remote_file_id    TEXT,
    picture           TEXT
);

CREATE TABLE source_tags (
    source_id INTEGER NOT NULL REFERENCES sources(source_id),
    tag       TEXT NOT NULL,
    PRIMARY KEY (source_id, tag)
) WITHOUT ROWID;

CREATE TABLE source_dependencies (
    source_id  INTEGER NOT NULL REFERENCES sources(source_id),
    dependency TEXT NOT NULL,
    PRIMARY KEY (source_id, dependency)
) WITHOUT ROWID;

CREATE TABLE source_replace_paths (
    source_id INTEGER NOT NULL REFERENCES sources(source_id),
    path      TEXT NOT NULL,
    PRIMARY KEY (source_id, path)
) WITHOUT ROWID;

CREATE TABLE files (
    file_id       INTEGER PRIMARY KEY,
    source_id     INTEGER NOT NULL REFERENCES sources(source_id),
    relative_path TEXT NOT NULL,
    folder        TEXT NOT NULL,
    file_name     TEXT NOT NULL,
    content_hash  TEXT NOT NULL,
    byte_size     INTEGER NOT NULL,
    is_effective  INTEGER NOT NULL DEFAULT 1,
    parse_status  TEXT NOT NULL DEFAULT 'ok'
                  CHECK (parse_status IN ('ok','error','skipped')),
    UNIQUE (source_id, relative_path)
);

CREATE TABLE parse_errors (
    file_id INTEGER NOT NULL REFERENCES files(file_id),
    message TEXT NOT NULL,
    line    INTEGER,
    col     INTEGER
);

CREATE TABLE file_overrides (
    override_id       INTEGER PRIMARY KEY,
    kind              TEXT NOT NULL CHECK (kind IN ('shadow','replace_path')),
    relative_path     TEXT NOT NULL,
    loser_file_id     INTEGER NOT NULL REFERENCES files(file_id),
    winner_file_id    INTEGER REFERENCES files(file_id),
    winner_source_id  INTEGER NOT NULL REFERENCES sources(source_id),
    loser_source_id   INTEGER NOT NULL REFERENCES sources(source_id),
    identical_content INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE symbols (
    symbol_id INTEGER PRIMARY KEY,
    name      TEXT NOT NULL,
    kind      TEXT NOT NULL CHECK (kind IN ('trigger','effect','modifier')),
    scope     TEXT,
    cwt_file  TEXT NOT NULL,
    UNIQUE (kind, name)
);

CREATE TABLE config_types (
    type_name        TEXT PRIMARY KEY,
    name_field       TEXT,
    paths            TEXT NOT NULL,
    type_per_file    INTEGER NOT NULL DEFAULT 0,
    skip_root_key    TEXT,
    localisation_map TEXT
) WITHOUT ROWID;

CREATE TABLE entities (
    entity_id    INTEGER PRIMARY KEY,
    entity_type  TEXT NOT NULL,
    entity_key   TEXT NOT NULL,
    file_id      INTEGER NOT NULL REFERENCES files(file_id),
    source_id    INTEGER NOT NULL REFERENCES sources(source_id),
    start_line   INTEGER NOT NULL,
    end_line     INTEGER NOT NULL,
    stmt_index   INTEGER NOT NULL,
    subtypes     TEXT,
    raw_text     TEXT NOT NULL,
    is_effective INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE entity_overrides (
    override_id       INTEGER PRIMARY KEY,
    kind              TEXT NOT NULL CHECK (kind IN ('redefinition','file_shadow','replace_path')),
    entity_type       TEXT NOT NULL,
    entity_key        TEXT NOT NULL,
    loser_entity_id   INTEGER NOT NULL REFERENCES entities(entity_id),
    winner_entity_id  INTEGER REFERENCES entities(entity_id),
    winner_source_id  INTEGER REFERENCES sources(source_id),
    loser_source_id   INTEGER NOT NULL REFERENCES sources(source_id),
    identical_content INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE script_nodes (
    node_id    INTEGER PRIMARY KEY,
    entity_id  INTEGER NOT NULL REFERENCES entities(entity_id),
    parent_id  INTEGER REFERENCES script_nodes(node_id),
    depth      INTEGER NOT NULL,
    sort_order INTEGER NOT NULL,
    node_kind  TEXT NOT NULL CHECK (node_kind IN ('clause','leaf','value')),
    context    TEXT NOT NULL CHECK (context IN ('trigger','effect','mtth','ai_chance','metadata')),
    key        TEXT,
    operator   TEXT,
    value      TEXT,
    value_kind TEXT CHECK (value_kind IN ('int','float','bool','date','string')),
    symbol_id  INTEGER REFERENCES symbols(symbol_id),
    line       INTEGER NOT NULL
);

CREATE TABLE modifier_values (
    entity_id    INTEGER NOT NULL REFERENCES entities(entity_id),
    modifier_key TEXT NOT NULL,
    value        TEXT NOT NULL,
    symbol_id    INTEGER REFERENCES symbols(symbol_id),
    PRIMARY KEY (entity_id, modifier_key)
) WITHOUT ROWID;

CREATE TABLE entity_localisation (
    entity_id INTEGER NOT NULL REFERENCES entities(entity_id),
    role      TEXT NOT NULL,
    loc_key   TEXT NOT NULL,
    PRIMARY KEY (entity_id, role)
) WITHOUT ROWID;

CREATE TABLE defines (
    define_key  TEXT NOT NULL,
    value       TEXT NOT NULL,
    source_id   INTEGER NOT NULL REFERENCES sources(source_id),
    PRIMARY KEY (define_key, source_id)
) WITHOUT ROWID;

CREATE TABLE localisation (
    loc_id       INTEGER PRIMARY KEY,
    loc_key      TEXT NOT NULL,
    language     TEXT NOT NULL,
    value        TEXT NOT NULL,
    -- value with inline formatting markup (color codes, icons) stripped, for
    -- search; see Localisation.stripMarkup
    value_plain  TEXT NOT NULL DEFAULT '',
    version_num  INTEGER,
    file_id      INTEGER NOT NULL REFERENCES files(file_id),
    source_id    INTEGER NOT NULL REFERENCES sources(source_id),
    is_replace   INTEGER NOT NULL DEFAULT 0,
    is_effective INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE loc_overrides (
    override_id       INTEGER PRIMARY KEY,
    loc_key           TEXT NOT NULL,
    language          TEXT NOT NULL,
    kind              TEXT NOT NULL CHECK (kind IN
        ('later_source','replace_dir','same_source_duplicate','file_shadow','replace_path')),
    loser_loc_id      INTEGER NOT NULL REFERENCES localisation(loc_id),
    winner_loc_id     INTEGER REFERENCES localisation(loc_id),
    winner_source_id  INTEGER REFERENCES sources(source_id),
    loser_source_id   INTEGER NOT NULL REFERENCES sources(source_id),
    identical_content INTEGER NOT NULL DEFAULT 0
);

-- Derived causal/cross-reference graph: one row per reference a script node
-- makes to another piece of content (fires an event, sets/checks a flag or
-- variable, applies/checks a modifier, calls a scripted trigger/effect, or an
-- on_action firing an event). target_type is scope-qualified for flags so a
-- has_country_flag check is not confused with a set_global_flag set.
CREATE TABLE refs (
    ref_id         INTEGER PRIMARY KEY,
    from_entity_id INTEGER NOT NULL REFERENCES entities(entity_id),
    from_context   TEXT NOT NULL,   -- trigger|effect|mtth|option_trigger|option_effect
    ref_kind       TEXT NOT NULL,   -- fires_event|sets_flag|checks_flag|sets_variable|
                                    -- checks_variable|applies_modifier|checks_modifier|
                                    -- calls_scripted_trigger|calls_scripted_effect|on_action_fires|
                                    -- identity checks (target_key = an entity_key):
                                    -- checks_idea|checks_idea_group|checks_reform|checks_building|
                                    -- checks_great_project|checks_policy|checks_disaster|
                                    -- checks_estate_privilege|checks_focus
    target_type    TEXT NOT NULL,   -- event|country_flag|global_flag|province_flag|ruler_flag|
                                    -- variable|modifier|scripted_trigger|scripted_effect|
                                    -- idea|idea_group|government_reform|building|great_project|
                                    -- policy|disaster|estate_privilege|focus
    target_key     TEXT NOT NULL,
    node_id        INTEGER NOT NULL REFERENCES script_nodes(node_id),
    -- the enclosing event option's clause node, when this reference is inside one
    option_node_id INTEGER REFERENCES script_nodes(node_id),
    negated        INTEGER NOT NULL DEFAULT 0
);
"""

    let private eu4DetailTables =
        """
CREATE TABLE event_details (
    entity_id         INTEGER PRIMARY KEY REFERENCES entities(entity_id),
    namespace         TEXT NOT NULL,
    event_kind        TEXT NOT NULL CHECK (event_kind IN ('country','province')),
    title_key         TEXT,
    desc_key          TEXT,
    picture           TEXT,
    is_triggered_only INTEGER NOT NULL DEFAULT 0,
    hidden            INTEGER NOT NULL DEFAULT 0,
    fire_only_once    INTEGER NOT NULL DEFAULT 0,
    major             INTEGER NOT NULL DEFAULT 0,
    has_mtth          INTEGER NOT NULL DEFAULT 0,
    option_count      INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE event_options (
    option_id  INTEGER PRIMARY KEY,
    entity_id  INTEGER NOT NULL REFERENCES entities(entity_id),
    option_idx INTEGER NOT NULL,
    name_key   TEXT,
    node_id    INTEGER NOT NULL REFERENCES script_nodes(node_id),
    UNIQUE (entity_id, option_idx)
);

CREATE TABLE mission_details (
    entity_id     INTEGER PRIMARY KEY REFERENCES entities(entity_id),
    series_key    TEXT NOT NULL,
    slot          INTEGER,
    is_generic    INTEGER NOT NULL DEFAULT 0,
    ai            INTEGER NOT NULL DEFAULT 1,
    icon          TEXT,
    position      INTEGER,
    has_highlight INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE mission_requirements (
    entity_id        INTEGER NOT NULL REFERENCES entities(entity_id),
    required_mission TEXT NOT NULL,
    PRIMARY KEY (entity_id, required_mission)
) WITHOUT ROWID;

CREATE TABLE decision_details (
    entity_id     INTEGER PRIMARY KEY REFERENCES entities(entity_id),
    major         INTEGER NOT NULL DEFAULT 0,
    ai_importance REAL
);
"""

    let private hoi4DetailTables =
        """
CREATE TABLE event_details (
    entity_id         INTEGER PRIMARY KEY REFERENCES entities(entity_id),
    namespace         TEXT NOT NULL,
    event_kind        TEXT NOT NULL CHECK (event_kind IN ('country','news','state','unit_leader','operative_leader')),
    title_key         TEXT,
    desc_key          TEXT,
    picture           TEXT,
    is_triggered_only INTEGER NOT NULL DEFAULT 0,
    hidden            INTEGER NOT NULL DEFAULT 0,
    fire_only_once    INTEGER NOT NULL DEFAULT 0,
    major             INTEGER NOT NULL DEFAULT 0,
    has_mtth          INTEGER NOT NULL DEFAULT 0,
    option_count      INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE event_options (
    option_id  INTEGER PRIMARY KEY,
    entity_id  INTEGER NOT NULL REFERENCES entities(entity_id),
    option_idx INTEGER NOT NULL,
    name_key   TEXT,
    node_id    INTEGER NOT NULL REFERENCES script_nodes(node_id),
    UNIQUE (entity_id, option_idx)
);

CREATE TABLE focus_details (
    entity_id            INTEGER PRIMARY KEY REFERENCES entities(entity_id),
    tree_id              TEXT NOT NULL,
    icon                 TEXT,
    x                    INTEGER,
    y                    INTEGER,
    relative_position_id TEXT
);

CREATE TABLE focus_requirements (
    entity_id      INTEGER NOT NULL REFERENCES entities(entity_id),
    required_focus TEXT NOT NULL,
    PRIMARY KEY (entity_id, required_focus)
) WITHOUT ROWID;

CREATE TABLE decision_details (
    entity_id     INTEGER PRIMARY KEY REFERENCES entities(entity_id),
    major         INTEGER NOT NULL DEFAULT 0,
    ai_importance REAL
);
"""

    let tablesSql (gameId: string) =
        let details =
            match gameId with
            | "eu4" -> eu4DetailTables
            | "hoi4" -> hoi4DetailTables
            | _ -> eu4DetailTables
        sharedTables + details

    let private sharedIndexes =
        """
CREATE INDEX idx_files_relpath ON files(relative_path);
CREATE INDEX idx_files_folder  ON files(folder) WHERE is_effective = 1;
CREATE INDEX idx_fovr_winner_src ON file_overrides(winner_source_id);
CREATE INDEX idx_fovr_loser_src  ON file_overrides(loser_source_id);
CREATE INDEX idx_fovr_relpath    ON file_overrides(relative_path);
CREATE INDEX idx_symbols_name ON symbols(name);
CREATE INDEX idx_entities_type_key ON entities(entity_type, entity_key);
CREATE INDEX idx_entities_file     ON entities(file_id);
CREATE INDEX idx_entities_source   ON entities(source_id);
CREATE INDEX idx_entities_eff      ON entities(entity_type) WHERE is_effective = 1;
CREATE INDEX idx_eovr_typekey ON entity_overrides(entity_type, entity_key);
CREATE INDEX idx_eovr_winner  ON entity_overrides(winner_source_id);
CREATE INDEX idx_eovr_loser   ON entity_overrides(loser_source_id);
CREATE INDEX idx_sn_entity    ON script_nodes(entity_id);
CREATE INDEX idx_sn_parent    ON script_nodes(parent_id);
CREATE INDEX idx_sn_key       ON script_nodes(key);
CREATE INDEX idx_sn_key_value ON script_nodes(key, value);
CREATE INDEX idx_sn_symbol    ON script_nodes(symbol_id) WHERE symbol_id IS NOT NULL;
CREATE INDEX idx_sn_value     ON script_nodes(value) WHERE value IS NOT NULL;
CREATE INDEX idx_evd_namespace ON event_details(namespace);
CREATE INDEX idx_mv_key ON modifier_values(modifier_key);
CREATE INDEX idx_eloc_key ON entity_localisation(loc_key);
CREATE INDEX idx_loc_key_lang ON localisation(loc_key, language);
CREATE INDEX idx_loc_source   ON localisation(source_id);
CREATE INDEX idx_lovr_key ON loc_overrides(loc_key, language);
CREATE INDEX idx_lovr_winner ON loc_overrides(winner_source_id);
CREATE INDEX idx_refs_target ON refs(target_type, target_key);
CREATE INDEX idx_refs_kind   ON refs(ref_kind);
CREATE INDEX idx_refs_from   ON refs(from_entity_id);
"""

    let private eu4DetailIndexes =
        """
CREATE INDEX idx_msd_series ON mission_details(series_key);
CREATE INDEX idx_msr_required ON mission_requirements(required_mission);
"""

    let private hoi4DetailIndexes =
        """
CREATE INDEX idx_fcd_tree ON focus_details(tree_id);
CREATE INDEX idx_fcr_required ON focus_requirements(required_focus);
"""

    let indexesSql (gameId: string) =
        let details =
            match gameId with
            | "eu4" -> eu4DetailIndexes
            | "hoi4" -> hoi4DetailIndexes
            | _ -> eu4DetailIndexes
        sharedIndexes + details

    let ftsSql =
        """
-- Localisation search uses the markup-stripped text and the trigram tokenizer
-- so CJK substrings (e.g. a colour-coded Chinese title) are findable.
CREATE VIRTUAL TABLE loc_fts USING fts5(
    value_plain, content='localisation', content_rowid='loc_id', tokenize='trigram'
);
-- Entity script search keeps unicode61 but treats '_' as part of a token so
-- Paradox identifiers like set_country_flag stay whole.
CREATE VIRTUAL TABLE entity_fts USING fts5(
    raw_text, content='entities', content_rowid='entity_id',
    tokenize="unicode61 tokenchars '_'"
);
INSERT INTO loc_fts(rowid, value_plain) SELECT loc_id, value_plain FROM localisation;
INSERT INTO entity_fts(rowid, raw_text) SELECT entity_id, raw_text FROM entities;
"""

    let viewsSql =
        """
CREATE VIEW v_effective_defines AS
    SELECT d.define_key, d.value, d.source_id, s.name AS source_name
    FROM defines d
    JOIN sources s USING (source_id)
    WHERE d.source_id = (
        SELECT MAX(d2.source_id) FROM defines d2 WHERE d2.define_key = d.define_key
    );

CREATE VIEW v_effective_entities AS
    SELECT e.*, s.name AS source_name, f.relative_path
    FROM entities e
    JOIN sources s USING (source_id)
    JOIN files f USING (file_id)
    WHERE e.is_effective = 1;

CREATE VIEW v_effective_loc AS
    SELECT * FROM localisation WHERE is_effective = 1;

CREATE VIEW v_override_summary AS
    SELECT 'file' AS level, kind, relative_path AS what,
           winner_source_id, loser_source_id, identical_content
    FROM file_overrides
    UNION ALL
    SELECT 'entity', kind, entity_type || ':' || entity_key,
           winner_source_id, loser_source_id, identical_content
    FROM entity_overrides
    UNION ALL
    SELECT 'localisation', kind, loc_key || ' (' || language || ')',
           winner_source_id, loser_source_id, identical_content
    FROM loc_overrides;

-- Unifies the two ways an entity grants a modifier: applied as an effect
-- (add_country_modifier = { name = M }) vs defined inline in the body of an
-- idea/reform/etc. (the modifier key appears directly in modifier_values).
CREATE VIEW v_modifier_grants AS
    SELECT from_entity_id AS entity_id, target_key AS modifier_key, 'applied' AS how
    FROM refs WHERE ref_kind = 'applies_modifier'
    UNION ALL
    SELECT entity_id, modifier_key, 'defined'
    FROM modifier_values;
"""

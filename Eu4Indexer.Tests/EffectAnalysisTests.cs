using Eu4Indexer.Mcp;
using Microsoft.Data.Sqlite;

namespace Eu4Indexer.Tests;

public sealed class EffectAnalysisTests
{
    [Fact]
    public void AnalyzeEffects_ConnectsTooltipStateChangeAndDownstreamConsequences()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"eu4_effects_{Guid.NewGuid():N}.db");

        try
        {
            CreateFixture(dbPath);

            var db = new Eu4Database(dbPath);
            var analysis = ConsequenceTools.AnalyzeEffects(db, "decision", "soyo_anld_company_export_anon_soyo");

            Assert.NotNull(analysis);
            var fired = Assert.Single(analysis!.Blocks.SelectMany(b => b.FiredEvents));
            Assert.Equal("flavor_sy0.119", fired.Event.EntityKey);
            Assert.Contains(fired.Tooltips, t =>
                t.LocKey == "soyo_more_economic_worsen_tt" &&
                t.Text == "英格兰的财政状况进一步恶化了。");
            Assert.Contains(fired.StateChanges, s =>
                s.Kind == "variable" &&
                s.Key == "soyo_anon_soyo_export_time_var" &&
                s.Operation == "change_variable" &&
                s.Value == "1");

            var consequence = Assert.Single(fired.DownstreamConsequences);
            Assert.Equal("flavor_sy0.114", consequence.Consumer.EntityKey);
            Assert.Contains("value 10", consequence.ConditionSummary);
            Assert.Contains(consequence.EffectSummary, e => e.Key == "release_all_subjects");
            Assert.Contains(consequence.EffectSummary, e => e.Key == "cede_province" && e.Value == "SY0");
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }

    [Fact]
    public void AnalyzeEffects_CoversReferenceExtractorStateVocabularyAndUnknownEffects()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"eu4_effects_{Guid.NewGuid():N}.db");

        try
        {
            CreateFixture(dbPath);

            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                Exec(conn,
                    """
                    INSERT INTO script_nodes (node_id, entity_id, parent_id, depth, sort_order, node_kind, context, key, operator, value, line) VALUES
                        (103, 10, 100, 1, 1, 'leaf', 'effect', 'obscure_custom_effect', '=', 'yes', 3),
                        (104, 10, 100, 1, 2, 'clause', 'effect', 'add_to_variable', '=', NULL, 4),
                        (105, 10, 104, 2, 0, 'leaf', 'effect', 'which', '=', 'accumulated_badness', 4),
                        (106, 10, 104, 2, 1, 'leaf', 'effect', 'value', '=', '2', 4),
                        (107, 10, 100, 1, 3, 'clause', 'effect', 'set_ruler_flag', '=', NULL, 5),
                        (108, 10, 107, 2, 0, 'leaf', 'effect', 'flag', '=', 'soyo_ruler_warning', 5),
                        (109, 10, 100, 1, 4, 'clause', 'effect', 'clr_country_flag', '=', NULL, 6),
                        (110, 10, 109, 2, 0, 'leaf', 'effect', 'flag', '=', 'soyo_old_warning', 6),
                        (320, 30, 300, 1, 1, 'clause', 'trigger', 'has_ruler_flag', '=', NULL, 33),
                        (321, 30, 320, 2, 0, 'leaf', 'trigger', 'flag', '=', 'soyo_ruler_warning', 33),
                        (330, 30, 300, 1, 2, 'clause', 'trigger', 'has_country_flag', '=', NULL, 34),
                        (331, 30, 330, 2, 0, 'leaf', 'trigger', 'flag', '=', 'soyo_old_warning', 34),
                        (340, 30, 300, 1, 3, 'clause', 'trigger', 'check_variable', '=', NULL, 35),
                        (341, 30, 340, 2, 0, 'leaf', 'trigger', 'which', '=', 'accumulated_badness', 35),
                        (342, 30, 340, 2, 1, 'leaf', 'trigger', 'value', '=', '2', 35);
                    INSERT INTO refs (ref_id, from_entity_id, from_context, ref_kind, target_type, target_key, node_id, option_node_id, negated) VALUES
                        (4, 10, 'effect', 'sets_variable', 'variable', 'accumulated_badness', 104, NULL, 0),
                        (5, 10, 'effect', 'sets_flag', 'ruler_flag', 'soyo_ruler_warning', 107, NULL, 0),
                        (6, 10, 'effect', 'sets_flag', 'country_flag', 'soyo_old_warning', 109, NULL, 0),
                        (7, 30, 'trigger', 'checks_variable', 'variable', 'accumulated_badness', 340, NULL, 0),
                        (8, 30, 'trigger', 'checks_flag', 'ruler_flag', 'soyo_ruler_warning', 320, NULL, 0),
                        (9, 30, 'trigger', 'checks_flag', 'country_flag', 'soyo_old_warning', 330, NULL, 0);
                    """);
            }

            var db = new Eu4Database(dbPath);
            var analysis = ConsequenceTools.AnalyzeEffects(db, "decision", "soyo_anld_company_export_anon_soyo");

            Assert.NotNull(analysis);
            var block = Assert.Single(analysis!.Blocks);
            Assert.Contains(block.DirectEffects, e => e.Key == "obscure_custom_effect");
            Assert.Contains(block.StateChanges, s =>
                s.Kind == "variable" && s.Key == "accumulated_badness" && s.Operation == "add_to_variable" && s.Value == "2");
            Assert.Contains(block.StateChanges, s =>
                s.Kind == "ruler_flag" && s.Key == "soyo_ruler_warning" && s.Operation == "set");
            Assert.Contains(block.StateChanges, s =>
                s.Kind == "country_flag" && s.Key == "soyo_old_warning" && s.Operation == "clear");
            Assert.Contains(block.DownstreamConsequences, c => c.StateKind == "ruler_flag" && c.StateKey == "soyo_ruler_warning");
            Assert.Contains(block.DownstreamConsequences, c => c.StateKind == "country_flag" && c.StateKey == "soyo_old_warning");
            Assert.Contains(block.DownstreamConsequences, c => c.StateKind == "variable" && c.StateKey == "accumulated_badness");
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }

    [Fact]
    public void AnalyzeEffects_PreservesFiredEventOptionBoundaries()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"eu4_effects_{Guid.NewGuid():N}.db");

        try
        {
            CreateFixture(dbPath);

            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                Exec(conn,
                    """
                    INSERT INTO script_nodes (node_id, entity_id, parent_id, depth, sort_order, node_kind, context, key, operator, value, line) VALUES
                        (220, 20, NULL, 0, 1, 'clause', 'effect', 'option', '=', NULL, 20),
                        (221, 20, 220, 1, 0, 'clause', 'effect', 'hidden_effect', '=', NULL, 21),
                        (222, 20, 221, 2, 0, 'leaf', 'effect', 'clr_country_flag', '=', 'unrelated_branch', 22);
                    INSERT INTO event_options (option_id, entity_id, option_idx, name_key, node_id) VALUES
                        (3, 20, 1, NULL, 220);
                    """);
            }

            var db = new Eu4Database(dbPath);
            var analysis = ConsequenceTools.AnalyzeEffects(db, "decision", "soyo_anld_company_export_anon_soyo");

            var fired = Assert.Single(analysis!.Blocks.SelectMany(b => b.FiredEvents));
            Assert.Equal(2, fired.Blocks.Count);
            Assert.Contains(fired.Blocks, b => b.OptionIndex == 0 && b.StateChanges.Any(s => s.Key == "soyo_anon_soyo_export_time_var"));
            Assert.Contains(fired.Blocks, b => b.OptionIndex == 1 && b.StateChanges.Any(s => s.Key == "unrelated_branch"));
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }

    [Fact]
    public void AnalyzeEffects_ScopesDownstreamSummaryToMatchingOption()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"eu4_effects_{Guid.NewGuid():N}.db");

        try
        {
            CreateFixture(dbPath);

            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                Exec(conn,
                    """
                    INSERT INTO script_nodes (node_id, entity_id, parent_id, depth, sort_order, node_kind, context, key, operator, value, line) VALUES
                        (350, 30, NULL, 0, 2, 'clause', 'effect', 'option', '=', NULL, 50),
                        (351, 30, 350, 1, 0, 'clause', 'trigger', 'trigger', '=', NULL, 51),
                        (352, 30, 351, 2, 0, 'clause', 'trigger', 'check_variable', '=', NULL, 52),
                        (353, 30, 352, 3, 0, 'leaf', 'trigger', 'which', '=', 'soyo_anon_soyo_export_time_var', 52),
                        (354, 30, 352, 3, 1, 'leaf', 'trigger', 'value', '=', '10', 52),
                        (355, 30, 350, 1, 1, 'leaf', 'effect', 'add_treasury', '=', '5', 53);
                    INSERT INTO event_options (option_id, entity_id, option_idx, name_key, node_id) VALUES
                        (4, 30, 1, NULL, 350);
                    UPDATE refs SET option_node_id = 350, node_id = 352
                    WHERE ref_id = 3;
                    """);
            }

            var db = new Eu4Database(dbPath);
            var analysis = ConsequenceTools.AnalyzeEffects(db, "decision", "soyo_anld_company_export_anon_soyo");

            var fired = Assert.Single(analysis!.Blocks.SelectMany(b => b.FiredEvents));
            var consequence = Assert.Single(fired.DownstreamConsequences);
            Assert.Contains(consequence.EffectSummary, e => e.Key == "add_treasury" && e.Value == "5");
            Assert.DoesNotContain(consequence.EffectSummary, e => e.Key == "release_all_subjects");
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }

    [Fact]
    public void AnalyzeEffects_ExpandsScriptedEffectsAndNegatedConditions()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"eu4_effects_{Guid.NewGuid():N}.db");

        try
        {
            CreateFixture(dbPath);

            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                Exec(conn,
                    """
                    INSERT INTO entities (entity_id, entity_type, entity_key, file_id, source_id, start_line, end_line, stmt_index, raw_text) VALUES
                        (40, 'scripted_effect', 'soyo_scripted_damage', 2, 1, 81, 90, 2, 'scripted effect');
                    INSERT INTO script_nodes (node_id, entity_id, parent_id, depth, sort_order, node_kind, context, key, operator, value, line) VALUES
                        (111, 10, 100, 1, 5, 'leaf', 'effect', 'soyo_scripted_damage', '=', 'yes', 7),
                        (400, 40, NULL, 0, 0, 'clause', 'effect', 'effect', '=', NULL, 81),
                        (401, 40, 400, 1, 0, 'clause', 'effect', 'set_country_flag', '=', NULL, 82),
                        (402, 40, 401, 2, 0, 'leaf', 'effect', 'flag', '=', 'scripted_flag', 82),
                        (360, 30, 300, 1, 4, 'clause', 'trigger', 'has_country_flag', '=', NULL, 36),
                        (361, 30, 360, 2, 0, 'leaf', 'trigger', 'flag', '=', 'scripted_flag', 36);
                    INSERT INTO refs (ref_id, from_entity_id, from_context, ref_kind, target_type, target_key, node_id, option_node_id, negated) VALUES
                        (10, 10, 'effect', 'calls_scripted_effect', 'scripted_effect', 'soyo_scripted_damage', 111, NULL, 0),
                        (11, 40, 'effect', 'sets_flag', 'country_flag', 'scripted_flag', 401, NULL, 0),
                        (12, 30, 'trigger', 'checks_flag', 'country_flag', 'scripted_flag', 360, NULL, 1);
                    """);
            }

            var db = new Eu4Database(dbPath);
            var analysis = ConsequenceTools.AnalyzeEffects(db, "decision", "soyo_anld_company_export_anon_soyo");
            var block = Assert.Single(analysis!.Blocks);

            Assert.Contains(block.StateChanges, s => s.Kind == "country_flag" && s.Key == "scripted_flag");
            Assert.Contains(block.DownstreamConsequences, c =>
                c.StateKey == "scripted_flag" && c.ConditionSummary.Contains("NOT", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }

    [Fact]
    public void AnalyzeEffects_HonorsFiredEventDepthLimit()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"eu4_effects_{Guid.NewGuid():N}.db");

        try
        {
            CreateFixture(dbPath);

            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                Exec(conn,
                    """
                    INSERT INTO entities (entity_id, entity_type, entity_key, file_id, source_id, start_line, end_line, stmt_index, raw_text) VALUES
                        (50, 'event', 'flavor_sy0.200', 2, 1, 91, 100, 3, 'event 200');
                    INSERT INTO event_details (entity_id, namespace, event_kind, is_triggered_only, option_count) VALUES
                        (50, 'flavor_sy0', 'country', 1, 1);
                    INSERT INTO script_nodes (node_id, entity_id, parent_id, depth, sort_order, node_kind, context, key, operator, value, line) VALUES
                        (207, 20, 203, 2, 1, 'clause', 'effect', 'country_event', '=', NULL, 16),
                        (208, 20, 207, 3, 0, 'leaf', 'effect', 'id', '=', 'flavor_sy0.200', 16),
                        (500, 50, NULL, 0, 0, 'clause', 'effect', 'option', '=', NULL, 91),
                        (501, 50, 500, 1, 0, 'clause', 'effect', 'hidden_effect', '=', NULL, 92),
                        (502, 50, 501, 2, 0, 'clause', 'effect', 'set_variable', '=', NULL, 93),
                        (503, 50, 502, 3, 0, 'leaf', 'effect', 'which', '=', 'too_deep', 93),
                        (504, 50, 502, 3, 1, 'leaf', 'effect', 'value', '=', '1', 93);
                    INSERT INTO event_options (option_id, entity_id, option_idx, name_key, node_id) VALUES
                        (5, 50, 0, NULL, 500);
                    INSERT INTO refs (ref_id, from_entity_id, from_context, ref_kind, target_type, target_key, node_id, option_node_id, negated) VALUES
                        (13, 20, 'option_effect', 'fires_event', 'event', 'flavor_sy0.200', 207, 200, 0),
                        (14, 50, 'option_effect', 'sets_variable', 'variable', 'too_deep', 502, 500, 0);
                    """);
            }

            var db = new Eu4Database(dbPath);
            var analysis = ConsequenceTools.AnalyzeEffects(db, "decision", "soyo_anld_company_export_anon_soyo", maxDepth: 1);
            var firstHop = Assert.Single(analysis!.Blocks.SelectMany(b => b.FiredEvents));

            Assert.DoesNotContain(firstHop.Blocks.SelectMany(b => b.FiredEvents), e => e.Event.EntityKey == "flavor_sy0.200");
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }

    [Fact]
    public void AnalyzeEffects_ExpandsScriptedEffectsInsideDeepestIncludedFiredEvent()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"eu4_effects_{Guid.NewGuid():N}.db");

        try
        {
            CreateFixture(dbPath);

            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                Exec(conn,
                    """
                    INSERT INTO entities (entity_id, entity_type, entity_key, file_id, source_id, start_line, end_line, stmt_index, raw_text) VALUES
                        (40, 'scripted_effect', 'soyo_scripted_damage', 2, 1, 81, 90, 2, 'scripted effect');
                    INSERT INTO script_nodes (node_id, entity_id, parent_id, depth, sort_order, node_kind, context, key, operator, value, line) VALUES
                        (111, 20, 203, 2, 1, 'leaf', 'effect', 'soyo_scripted_damage', '=', 'yes', 17),
                        (400, 40, NULL, 0, 0, 'clause', 'effect', 'effect', '=', NULL, 81),
                        (401, 40, 400, 1, 0, 'clause', 'effect', 'set_country_flag', '=', NULL, 82),
                        (402, 40, 401, 2, 0, 'leaf', 'effect', 'flag', '=', 'scripted_flag', 82);
                    INSERT INTO refs (ref_id, from_entity_id, from_context, ref_kind, target_type, target_key, node_id, option_node_id, negated) VALUES
                        (10, 20, 'option_effect', 'calls_scripted_effect', 'scripted_effect', 'soyo_scripted_damage', 111, 200, 0),
                        (11, 40, 'effect', 'sets_flag', 'country_flag', 'scripted_flag', 401, NULL, 0);
                    """);
            }

            var db = new Eu4Database(dbPath);
            var analysis = ConsequenceTools.AnalyzeEffects(db, "decision", "soyo_anld_company_export_anon_soyo", maxDepth: 1);
            var fired = Assert.Single(analysis!.Blocks.SelectMany(b => b.FiredEvents));

            Assert.Contains(fired.StateChanges, s => s.Kind == "country_flag" && s.Key == "scripted_flag");
            Assert.Contains(fired.Blocks.SelectMany(b => b.StateChanges), s => s.Kind == "country_flag" && s.Key == "scripted_flag");
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }

    private static void CreateFixture(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        Exec(conn,
            """
            CREATE TABLE sources (
                source_id INTEGER PRIMARY KEY,
                kind TEXT NOT NULL,
                load_order INTEGER NOT NULL,
                name TEXT NOT NULL,
                root_path TEXT NOT NULL,
                descriptor_path TEXT,
                mod_version TEXT,
                supported_version TEXT,
                remote_file_id TEXT,
                picture TEXT
            );
            CREATE TABLE files (
                file_id INTEGER PRIMARY KEY,
                source_id INTEGER NOT NULL,
                relative_path TEXT NOT NULL,
                folder TEXT NOT NULL,
                file_name TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                byte_size INTEGER NOT NULL,
                is_effective INTEGER NOT NULL DEFAULT 1,
                parse_status TEXT NOT NULL DEFAULT 'ok'
            );
            CREATE TABLE symbols (
                symbol_id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                kind TEXT NOT NULL,
                scope TEXT,
                cwt_file TEXT NOT NULL
            );
            CREATE TABLE entities (
                entity_id INTEGER PRIMARY KEY,
                entity_type TEXT NOT NULL,
                entity_key TEXT NOT NULL,
                file_id INTEGER NOT NULL,
                source_id INTEGER NOT NULL,
                start_line INTEGER NOT NULL,
                end_line INTEGER NOT NULL,
                stmt_index INTEGER NOT NULL,
                subtypes TEXT,
                raw_text TEXT NOT NULL,
                is_effective INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE event_details (
                entity_id INTEGER PRIMARY KEY,
                namespace TEXT NOT NULL,
                event_kind TEXT NOT NULL,
                title_key TEXT,
                desc_key TEXT,
                picture TEXT,
                is_triggered_only INTEGER NOT NULL DEFAULT 0,
                hidden INTEGER NOT NULL DEFAULT 0,
                fire_only_once INTEGER NOT NULL DEFAULT 0,
                major INTEGER NOT NULL DEFAULT 0,
                has_mtth INTEGER NOT NULL DEFAULT 0,
                option_count INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE event_options (
                option_id INTEGER PRIMARY KEY,
                entity_id INTEGER NOT NULL,
                option_idx INTEGER NOT NULL,
                name_key TEXT,
                node_id INTEGER NOT NULL
            );
            CREATE TABLE script_nodes (
                node_id INTEGER PRIMARY KEY,
                entity_id INTEGER NOT NULL,
                parent_id INTEGER,
                depth INTEGER NOT NULL,
                sort_order INTEGER NOT NULL,
                node_kind TEXT NOT NULL,
                context TEXT NOT NULL,
                key TEXT,
                operator TEXT,
                value TEXT,
                value_kind TEXT,
                symbol_id INTEGER,
                line INTEGER NOT NULL
            );
            CREATE TABLE refs (
                ref_id INTEGER PRIMARY KEY,
                from_entity_id INTEGER NOT NULL,
                from_context TEXT NOT NULL,
                ref_kind TEXT NOT NULL,
                target_type TEXT NOT NULL,
                target_key TEXT NOT NULL,
                node_id INTEGER NOT NULL,
                option_node_id INTEGER,
                negated INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE localisation (
                loc_id INTEGER PRIMARY KEY,
                loc_key TEXT NOT NULL,
                language TEXT NOT NULL,
                value TEXT NOT NULL,
                value_plain TEXT NOT NULL DEFAULT '',
                version_num INTEGER,
                file_id INTEGER NOT NULL,
                source_id INTEGER NOT NULL,
                is_replace INTEGER NOT NULL DEFAULT 0,
                is_effective INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE entity_localisation (
                entity_id INTEGER NOT NULL,
                role TEXT NOT NULL,
                loc_key TEXT NOT NULL
            );
            """);

        Exec(conn,
            """
            INSERT INTO sources (source_id, kind, load_order, name, root_path) VALUES (1, 'mod', 1, 'fixture', '/fixture');
            INSERT INTO files (file_id, source_id, relative_path, folder, file_name, content_hash, byte_size) VALUES
                (1, 1, 'decisions/soyo.txt', 'decisions', 'soyo.txt', 'a', 1),
                (2, 1, 'events/soyo.txt', 'events', 'soyo.txt', 'b', 1);
            INSERT INTO entities (entity_id, entity_type, entity_key, file_id, source_id, start_line, end_line, stmt_index, raw_text) VALUES
                (10, 'decision', 'soyo_anld_company_export_anon_soyo', 1, 1, 1, 10, 0, 'decision'),
                (20, 'event', 'flavor_sy0.119', 2, 1, 11, 30, 0, 'event 119'),
                (30, 'event', 'flavor_sy0.114', 2, 1, 31, 80, 1, 'event 114');
            INSERT INTO event_details (entity_id, namespace, event_kind, is_triggered_only, option_count) VALUES
                (20, 'flavor_sy0', 'country', 1, 1),
                (30, 'flavor_sy0', 'country', 0, 1);
            INSERT INTO localisation (loc_id, loc_key, language, value, value_plain, file_id, source_id) VALUES
                (1, 'soyo_more_economic_worsen_tt', 'english', '英格兰的财政状况进一步恶化了。', '英格兰的财政状况进一步恶化了。', 2, 1),
                (2, 'flavor_sy0.119.a', 'english', '继续出口', '继续出口', 2, 1);
            INSERT INTO entity_localisation (entity_id, role, loc_key) VALUES
                (20, 'option_0_name', 'flavor_sy0.119.a');
            """);

        Exec(conn,
            """
            INSERT INTO script_nodes (node_id, entity_id, parent_id, depth, sort_order, node_kind, context, key, operator, value, line) VALUES
                (100, 10, NULL, 0, 0, 'clause', 'effect', 'effect', '=', NULL, 1),
                (101, 10, 100, 1, 0, 'clause', 'effect', 'country_event', '=', NULL, 2),
                (102, 10, 101, 2, 0, 'leaf', 'effect', 'id', '=', 'flavor_sy0.119', 2),
                (200, 20, NULL, 0, 0, 'clause', 'effect', 'option', '=', NULL, 11),
                (201, 20, 200, 1, 0, 'leaf', 'metadata', 'name', '=', 'flavor_sy0.119.a', 12),
                (202, 20, 200, 1, 1, 'leaf', 'effect', 'custom_tooltip', '=', 'soyo_more_economic_worsen_tt', 13),
                (203, 20, 200, 1, 2, 'clause', 'effect', 'hidden_effect', '=', NULL, 14),
                (204, 20, 203, 2, 0, 'clause', 'effect', 'change_variable', '=', NULL, 15),
                (205, 20, 204, 3, 0, 'leaf', 'effect', 'which', '=', 'soyo_anon_soyo_export_time_var', 15),
                (206, 20, 204, 3, 1, 'leaf', 'effect', 'value', '=', '1', 15),
                (300, 30, NULL, 0, 0, 'clause', 'trigger', 'trigger', '=', NULL, 31),
                (301, 30, 300, 1, 0, 'clause', 'trigger', 'check_variable', '=', NULL, 32),
                (302, 30, 301, 2, 0, 'leaf', 'trigger', 'which', '=', 'soyo_anon_soyo_export_time_var', 32),
                (303, 30, 301, 2, 1, 'leaf', 'trigger', 'value', '=', '10', 32),
                (310, 30, NULL, 0, 1, 'clause', 'effect', 'option', '=', NULL, 40),
                (311, 30, 310, 1, 0, 'clause', 'effect', 'hidden_effect', '=', NULL, 41),
                (312, 30, 311, 2, 0, 'leaf', 'effect', 'release_all_subjects', '=', 'yes', 42),
                (313, 30, 311, 2, 1, 'leaf', 'effect', 'release_all_possible_countries', '=', 'yes', 43),
                (314, 30, 311, 2, 2, 'leaf', 'effect', 'cede_province', '=', 'SY0', 44);
            INSERT INTO event_options (option_id, entity_id, option_idx, name_key, node_id) VALUES
                (1, 20, 0, 'flavor_sy0.119.a', 200),
                (2, 30, 0, NULL, 310);
            INSERT INTO refs (ref_id, from_entity_id, from_context, ref_kind, target_type, target_key, node_id, option_node_id, negated) VALUES
                (1, 10, 'effect', 'fires_event', 'event', 'flavor_sy0.119', 101, NULL, 0),
                (2, 20, 'option_effect', 'sets_variable', 'variable', 'soyo_anon_soyo_export_time_var', 204, 200, 0),
                (3, 30, 'trigger', 'checks_variable', 'variable', 'soyo_anon_soyo_export_time_var', 301, NULL, 0);
            """);
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

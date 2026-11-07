using Eu4Indexer.Core;
using Eu4Indexer.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Eu4Indexer.Tests;

public class Hoi4RulesUpdateTests
{
    [Fact]
    public void LatestRules_FixtureFeatures_AreIndexedAndReachableThroughMcp()
    {
        if (TestPaths.Hoi4ConfigDir is not { } config || TestPaths.FixtureHoi4GameDir is not { } game) return;
        var path = Path.Combine(Path.GetTempPath(), $"hoi4_rules_{Guid.NewGuid():N}.db");
        try
        {
            var result = Pipeline.runWithPaths(GameAdapterModule.hoi4, game,
                ListModule.OfSeq(Array.Empty<string>()), config, path,
                skipGeneric: false, withFts: true, languages: ListModule.OfSeq(new[] { "english", "simp_chinese" }),
                log: FuncConvert.FromAction<string>(_ => { }));
            Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");
            Assert.Equal(0, result.ResultValue.ParseErrorCount);
            Assert.Equal(0, result.ResultValue.ForeignKeyViolations);
            var db = new Eu4Database(path);

            long Count(string sql) => db.Query(sql, r => r.GetInt64(0)).Single();
            Assert.Equal(1, Count("SELECT count(*) FROM entities WHERE entity_type='doctrine_folder' AND entity_key='fixture_special_forces'"));
            var folder = GraphTools.WhatTriggers(db, "fixture_special_forces", "doctrine_folder")!;
            Assert.Contains(folder.TriggeredBy, e => e.RefKind == "doctrine_in_folder");
            Assert.Contains(folder.TriggeredBy, e => e.RefKind == "adds_mastery");
            Assert.Contains(folder.TriggeredBy, e => e.RefKind == "assigns_doctrine_folder");
            Assert.Contains(GraphTools.FindByCondition(db, "fixture_special_forces"), e => e.EntityKey == "fixture.3");
            Assert.Contains(GraphTools.FindByCondition(db, "fixture_alternative_doctrine", negated: true), e => e.EntityKey == "fixture.3");
            Assert.Contains(GraphTools.WhatTriggers(db, "fixture_alternative_doctrine", "subdoctrine")!.TriggeredBy,
                e => e.RefKind == "doctrine_excludes");
            Assert.Equal(3, Count("SELECT count(*) FROM refs WHERE ref_kind='doctrine_uses_track' AND target_key='fixture_infantry_track'"));
            Assert.Equal(1, Count("SELECT count(*) FROM refs r JOIN entities e ON e.entity_id=r.from_entity_id WHERE e.entity_key='fixture.3' AND r.ref_kind='assigns_doctrine' AND r.target_key='fixture_multi_track_doctrine'"));
            Assert.Equal(0, Count("SELECT count(*) FROM refs WHERE target_type='doctrine_track' AND target_key='1'"));
            Assert.Contains(GraphTools.WhatDoesItDo(db, "fixture_multi_track_doctrine", "subdoctrine")!.References,
                e => e.RefKind == "sets_variable" && e.TargetKey == "fixture_mastery");
            Assert.Equal(2, Count("SELECT count(*) FROM refs WHERE ref_kind='sets_variable' AND target_key='fixture_mastery'"));
            Assert.Equal(1, Count("SELECT count(*) FROM refs WHERE ref_kind='sets_variable' AND target_key='fixture_explicit'"));
            Assert.Contains(GraphTools.FindByCondition(db, "fixture_started"), e => e.EntityKey == "fixture_commander");
            Assert.Contains(GraphTools.WhatDoesItDo(db, "fixture_deployed_ability", "ability")!.References,
                e => e.TargetKey == "fixture_ability_used" && e.FromContext == "effect");
            Assert.Contains(ConsequenceTools.AnalyzeEffects(db, "ability", "fixture_deployed_ability")!.Blocks,
                block => block.StateChanges.Any(change => change.Key == "fixture_ability_used"));
            var doctrineEffects = ConsequenceTools.AnalyzeEffects(db, "subdoctrine", "fixture_multi_track_doctrine")!;
            Assert.Contains(doctrineEffects.Blocks.SelectMany(block => block.StateChanges),
                change => change.Key == "fixture_mastery" && change.Value == "expression (see script tree)");
            Assert.DoesNotContain(doctrineEffects.Blocks.SelectMany(block => block.DirectEffects), effect => effect.Key == "pow");
            Assert.Contains(GraphTools.WhatTriggers(db, "fixture.3", "event")!.TriggeredBy, e => e.FromType == "on_action");
            Assert.Contains(GraphTools.WhatTriggers(db, "fixture.1", "event")!.TriggeredBy,
                e => e.FromType == "on_action" && e.RefKind == "on_action_fires");
            Assert.Contains(GraphTools.WhatTriggers(db, "fixture.2", "event")!.TriggeredBy,
                e => e.FromType == "on_action" && e.RefKind == "on_action_fires");

            Assert.Equal(1, Count("SELECT count(*) FROM script_nodes n JOIN symbols s ON s.symbol_id=n.symbol_id WHERE n.key='is_deployed' AND n.context='trigger' AND s.kind='trigger'"));
            Assert.Equal(1, Count("SELECT count(*) FROM script_nodes n JOIN symbols s ON s.symbol_id=n.symbol_id WHERE n.key='is_fighting_in_strategic_region' AND s.kind='trigger'"));
            Assert.Equal(2, Count("SELECT count(*) FROM script_nodes n JOIN entities e ON e.entity_id=n.entity_id WHERE e.entity_key='fixture_multi_track_doctrine' AND n.key='equipment_bonus'"));
            Assert.True(Count("SELECT count(*) FROM script_nodes WHERE key IN ('pow','root','round','mod','lerp','atan2','sin','not_equals')") >= 8);
            Assert.Equal(1, Count("SELECT count(*) FROM script_nodes WHERE key='regimental_support'"));
            Assert.Equal(1, Count("SELECT count(*) FROM modifier_values WHERE modifier_key='production_cost_arms_factory_factor' AND symbol_id IS NOT NULL"));
            Assert.Contains(EntityTools.ResolveSymbol(db, "production_cost_arms_factory_factor").Matches, s => s.Kind == "modifier");
            var scopes = EntityTools.ResolveSymbol(db, "army_non_core_attack_factor").Matches.Single(s => s.Kind == "modifier").Scope!;
            Assert.Contains("army", scopes.Split('|'));
            Assert.Contains("aggressive", scopes.Split('|'));
            Assert.Equal(1, Count("SELECT count(*) FROM entity_localisation l JOIN entities e ON e.entity_id=l.entity_id WHERE e.entity_key='fixture_grand_doctrine' AND l.loc_key='fixture_grand_doctrine_name'"));
            Assert.Contains(SearchTools.SearchLocalisation(db, "Custom Doctrine", "english", 10), hit => hit.LocKey == "fixture_doctrine_folder_name");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        }
    }

    [Theory]
    [InlineData("production_cost_<building>_factor", "production_cost_arms_factory_factor", true)]
    [InlineData("enum[project_tag]_speed_factor", "nuclear_speed_factor", true)]
    [InlineData("<unit>_mission_factor", "infantry_mission_factor", true)]
    [InlineData("production_cost_<building>_factor", "production_cost__factor", false)]
    [InlineData("production_cost_<building>_factor", "production_cost_bad/path_factor", false)]
    [InlineData("army_attack_factor", "army_attack_factor", false)]
    public void ModifierTemplates_PreserveLiteralBoundaries(string pattern, string name, bool expected) =>
        Assert.Equal(expected, ScriptTree.modifierTemplateMatches(pattern, name));
}

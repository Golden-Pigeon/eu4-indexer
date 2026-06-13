using Eu4Indexer.Core;
using Eu4Indexer.Mcp;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Eu4Indexer.Tests;

/// <summary>
/// Exercises the MCP query tools against a freshly built index. No-ops when the
/// game/config data is absent (same guard as IntegrationTests).
/// </summary>
public class McpToolsTests
{
    private static FSharpList<string> ToFs(params string[] items) => ListModule.OfSeq(items);

    [Fact]
    public void Tools_AnswerCoreQueries()
    {
        var gameDir = TestPaths.GameDir;
        var configDir = TestPaths.ConfigDir;
        if (gameDir is null || configDir is null) return;

        var dbPath = Path.Combine(Path.GetTempPath(), $"eu4_mcp_{Guid.NewGuid():N}.db");

        try
        {
            var result = Pipeline.runWithPaths(
                GameAdapterModule.eu4, gameDir, ToFs(), configDir, dbPath,
                skipGeneric: false, withFts: true, languages: ToFs("english"),
                log: FuncConvert.FromAction<string>(_ => { }));

            Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");

            var db = new Eu4Database(dbPath);
            db.EnsureSchemaVersion();

            // resolve_symbol: add_stability is a known effect in the dictionary
            var symbol = EntityTools.ResolveSymbol(db, "add_stability");
            Assert.Contains(symbol.Matches, m => m.Kind == "effect");

            // explain_entity on an event that has outbound references
            var eventKey = db.Query(
                """
                SELECT e.entity_key FROM entities e
                JOIN refs r ON r.from_entity_id = e.entity_id
                WHERE e.entity_type = 'event' AND e.is_effective = 1
                GROUP BY e.entity_id LIMIT 1
                """,
                r => r.GetString(0), null, 1).FirstOrDefault();

            Assert.NotNull(eventKey);

            var explanation = EntityTools.ExplainEntity(db, "event", eventKey!);
            Assert.NotNull(explanation);
            Assert.Equal("event", explanation!.Entity.EntityType);
            Assert.True(explanation.Script.Count > 0);
            Assert.True(explanation.References.Count > 0);

            // unknown entity returns null
            Assert.Null(EntityTools.ExplainEntity(db, "event", "does.not.exist.999"));

            // search_everything finds entity script mentioning add_stability
            var everything = SearchTools.SearchEverything(db, "add_stability", 5);
            Assert.Contains(everything, h => h.Kind == "entity");

            // search_localisation returns english hits for a common word
            var loc = SearchTools.SearchLocalisation(db, "Stability", "english", 5);
            Assert.NotEmpty(loc);
            Assert.All(loc, h => Assert.Equal("english", h.Language));

            // what_does_it_do: the same event lists its outbound references
            var forward = GraphTools.WhatDoesItDo(db, eventKey!, "event");
            Assert.NotNull(forward);
            Assert.True(forward!.References.Count > 0);

            // what_triggers: an event that is the target of a fires_event edge has inbound
            var firedEvent = db.Query(
                """
                SELECT target_key FROM refs
                WHERE ref_kind = 'fires_event'
                  AND target_key IN (SELECT entity_key FROM entities WHERE entity_type = 'event' AND is_effective = 1)
                LIMIT 1
                """,
                r => r.GetString(0), null, 1).FirstOrDefault();

            if (firedEvent is not null)
            {
                var inbound = GraphTools.WhatTriggers(db, firedEvent, "event");
                Assert.NotNull(inbound);
                Assert.True(inbound!.TriggeredBy.Count > 0);
            }

            // find_by_condition: a checked flag is used by at least one entity
            var checkedFlag = db.Query(
                "SELECT target_key FROM refs WHERE ref_kind = 'checks_flag' AND target_type = 'country_flag' LIMIT 1",
                r => r.GetString(0), null, 1).FirstOrDefault();

            if (checkedFlag is not null)
            {
                var users = GraphTools.FindByCondition(db, checkedFlag, "country_flag", null, 50);
                Assert.NotEmpty(users);
            }

            // trace_to_goal: a flag that is set somewhere is reachable with a chain
            var setFlag = db.Query(
                """
                SELECT target_key FROM refs
                WHERE ref_kind = 'sets_flag' AND target_type = 'country_flag'
                GROUP BY target_key ORDER BY count(*) DESC LIMIT 1
                """,
                r => r.GetString(0), null, 1).FirstOrDefault();

            if (setFlag is not null)
            {
                var trace = PlanningTools.TraceToGoal(db, "flag", setFlag, "country_flag", 4);
                Assert.True(trace.Reachable);
                Assert.NotEmpty(trace.Paths);
                Assert.All(trace.Paths, p => Assert.NotEmpty(p.Steps));
            }

            // find_dangling runs and returns a bounded, well-formed list
            var dangling = PlanningTools.FindDangling(db, "all", 10);
            Assert.NotNull(dangling);
            Assert.All(dangling, d => Assert.False(string.IsNullOrEmpty(d.TargetKey)));

            // list_sources includes the base game
            var sources = CatalogTools.ListSources(db);
            Assert.Contains(sources, s => s.Kind == "base_game");

            // describe_schema reports a usable data dictionary
            var schema = CatalogTools.DescribeSchema(db);
            Assert.NotEmpty(schema.EntityTypes);
            Assert.Contains("english", schema.Languages);
            Assert.Contains(schema.ReferenceKinds, k => k.Name == "fires_event");

            // get_overrides runs and returns a list
            Assert.NotNull(CatalogTools.GetOverrides(db, null, null, 10));

            // read_query: a SELECT returns rows; a non-SELECT is rejected
            var query = QueryTools.ReadQuery(db, "SELECT count(*) AS n FROM entities", 5);
            Assert.Single(query.Rows);
            Assert.Throws<ArgumentException>(() => QueryTools.ReadQuery(db, "DELETE FROM entities", 5));
            // a write hidden behind a CTE passes the prefix check but the EXPLAIN guard rejects it
            Assert.Throws<ArgumentException>(() =>
                QueryTools.ReadQuery(db, "WITH x AS (SELECT 1) DELETE FROM entities", 5));

            // a ';' inside a string literal is fine; a real statement separator is rejected
            var semi = QueryTools.ReadQuery(db, "SELECT ';' AS s", 5);
            Assert.Equal(";", semi.Rows[0]["s"]);
            Assert.Throws<ArgumentException>(() => QueryTools.ReadQuery(db, "SELECT 1; SELECT 2", 5));

            // this index has FTS, so the search tools are usable
            Assert.True(db.FtsAvailable);
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }
}

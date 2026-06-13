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
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }
}

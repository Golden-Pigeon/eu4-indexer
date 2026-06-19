using Eu4Indexer.Core;
using Eu4Indexer.Core.Database;
using Microsoft.Data.Sqlite;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Eu4Indexer.Tests;

/// <summary>
/// End-to-end indexing against a real Hearts of Iron IV install (and an optional
/// example mod) — the HOI4 analogue of <see cref="IntegrationTests"/>. No-ops when
/// the game data is absent, so the suite stays green without it. Enable by setting
/// HOI4_GAME_DIR (and optionally HOI4_CONFIG_DIR / HOI4_EXAMPLE_MOD_DIR) in .env or
/// the environment; the config otherwise comes from `eu4indexer setup`.
/// </summary>
public class Hoi4IntegrationTests
{
    private static FSharpList<string> ToFs(params string[] items) => ListModule.OfSeq(items);

    [Fact]
    public void Index_RealHoi4Game_ProducesQueryableDatabase()
    {
        var gameDir = TestPaths.Hoi4GameDir;
        var configDir = TestPaths.Hoi4ConfigDir;
        if (gameDir is null || configDir is null) return;

        var dbPath = Path.Combine(Path.GetTempPath(), $"hoi4_test_{Guid.NewGuid():N}.db");

        try
        {
            var result = Pipeline.runWithPaths(
                GameAdapterModule.hoi4, gameDir, ToFs(), configDir, dbPath,
                skipGeneric: false, withFts: true, languages: ToFs(),
                log: FuncConvert.FromAction<string>(_ => { }));

            Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");
            var report = result.ResultValue;

            Assert.True(report.EntityCount > 1000, $"expected >1000 entities, got {report.EntityCount}");
            Assert.Equal(0, report.ForeignKeyViolations);

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            // HOI4-specific content: national focuses and their detail/requirement tables.
            Assert.True(Scalar(conn, "SELECT count(*) FROM entities WHERE entity_type='focus' AND is_effective=1") > 50);
            Assert.True(Scalar(conn, "SELECT count(*) FROM focus_details") > 50);
            Assert.True(Scalar(conn, "SELECT count(*) FROM focus_requirements") > 0);
            Assert.True(Scalar(conn, "SELECT count(*) FROM entities WHERE entity_type='event' AND is_effective=1") > 0);
            Assert.True(Scalar(conn, "SELECT count(*) FROM symbols WHERE kind='effect'") > 100);

            // every symbol-tagged script node joins to a real symbol (FK integrity)
            Assert.Equal(0, Scalar(conn,
                "SELECT count(*) FROM script_nodes n WHERE n.symbol_id IS NOT NULL " +
                "AND NOT EXISTS (SELECT 1 FROM symbols s WHERE s.symbol_id=n.symbol_id)"));

            // the reference graph is populated and FK-consistent
            Assert.True(Scalar(conn, "SELECT count(*) FROM refs") > 100);
            Assert.True(Scalar(conn, "SELECT count(*) FROM refs WHERE ref_kind='fires_event'") > 0);
            Assert.Equal(0, Scalar(conn,
                "SELECT count(*) FROM refs r WHERE NOT EXISTS " +
                "(SELECT 1 FROM script_nodes n WHERE n.node_id=r.node_id)"));

            Assert.Equal(Schema.UserVersion, Scalar(conn, "PRAGMA user_version"));
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }

    [Fact]
    public void Index_WithHoi4Mod_RecordsOverrides()
    {
        var gameDir = TestPaths.Hoi4GameDir;
        var configDir = TestPaths.Hoi4ConfigDir;
        var modDir = TestPaths.Hoi4ExampleModDir;
        if (gameDir is null || configDir is null || modDir is null) return;

        var dbPath = Path.Combine(Path.GetTempPath(), $"hoi4_ovr_{Guid.NewGuid():N}.db");

        try
        {
            var result = Pipeline.runWithPaths(
                GameAdapterModule.hoi4, gameDir, ToFs(modDir), configDir, dbPath,
                skipGeneric: true, withFts: false, languages: ToFs("english"),
                log: FuncConvert.FromAction<string>(_ => { }));

            Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            // the mod (source 2) shadows base-game content, recorded in the override graph
            Assert.True(Scalar(conn, "SELECT count(*) FROM v_override_summary WHERE winner_source_id=2") > 0);
            // shadowed losers are retained but marked ineffective
            Assert.True(Scalar(conn, "SELECT count(*) FROM files WHERE is_effective=0") > 0);
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }

    private static long Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
}

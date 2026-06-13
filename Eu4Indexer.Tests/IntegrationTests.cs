using Eu4Indexer.Core;
using Eu4Indexer.Core.Database;
using Microsoft.Data.Sqlite;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Eu4Indexer.Tests;

/// <summary>
/// End-to-end indexing against the real game + example mod. No-ops when the
/// game/config data is not present on the machine.
/// </summary>
public class IntegrationTests
{
    private static FSharpList<string> ToFs(params string[] items) =>
        ListModule.OfSeq(items);

    [Fact]
    public void Index_RealGameAndMod_ProducesQueryableDatabase()
    {
        var gameDir = TestPaths.GameDir;
        var configDir = TestPaths.ConfigDir;
        var modDir = TestPaths.ExampleModDir;
        if (gameDir is null || configDir is null) return;

        var dbPath = Path.Combine(Path.GetTempPath(), $"eu4_test_{Guid.NewGuid():N}.db");
        var mods = modDir is null ? ToFs() : ToFs(modDir);

        try
        {
            var result = Pipeline.runWithPaths(
                GameAdapterModule.eu4, gameDir, mods, configDir, dbPath,
                skipGeneric: false, withFts: true, languages: ToFs(),
                log: FuncConvert.FromAction<string>(_ => { }));

            Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");
            var report = result.ResultValue;

            Assert.True(report.EntityCount > 10000, $"expected >10000 entities, got {report.EntityCount}");
            Assert.Equal(0, report.ForeignKeyViolations);

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            Assert.True(Scalar(conn, "SELECT count(*) FROM entities WHERE entity_type='event' AND is_effective=1") > 5000);
            Assert.True(Scalar(conn, "SELECT count(*) FROM symbols WHERE kind='effect'") > 500);

            // every effect-tagged script node joins to an effect symbol
            Assert.Equal(0, Scalar(conn,
                "SELECT count(*) FROM script_nodes n WHERE n.symbol_id IS NOT NULL " +
                "AND NOT EXISTS (SELECT 1 FROM symbols s WHERE s.symbol_id=n.symbol_id)"));

            // the add_stability proof query returns events
            Assert.True(Scalar(conn,
                "SELECT count(DISTINCT e.entity_id) FROM entities e " +
                "JOIN script_nodes n ON n.entity_id=e.entity_id " +
                "JOIN symbols s ON s.symbol_id=n.symbol_id " +
                "WHERE e.entity_type='event' AND s.kind='effect' AND s.name='add_stability'") > 0);

            // user_version is set
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
    public void Index_WithMod_RecordsOverrides()
    {
        var gameDir = TestPaths.GameDir;
        var configDir = TestPaths.ConfigDir;
        var modDir = TestPaths.ExampleModDir;
        if (gameDir is null || configDir is null || modDir is null) return;

        var dbPath = Path.Combine(Path.GetTempPath(), $"eu4_ovr_{Guid.NewGuid():N}.db");

        try
        {
            var result = Pipeline.runWithPaths(
                GameAdapterModule.eu4, gameDir, ToFs(modDir), configDir, dbPath,
                skipGeneric: true, withFts: false, languages: ToFs("english"),
                log: FuncConvert.FromAction<string>(_ => { }));

            Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            // the mod (source 2) shadows base-game files
            Assert.True(Scalar(conn,
                "SELECT count(*) FROM file_overrides WHERE kind='shadow' AND winner_source_id=2") > 0);

            // override summary view spans all three levels and is queryable
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

using Eu4Indexer.Core;
using Eu4Indexer.Core.Database;
using Microsoft.Data.Sqlite;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Eu4Indexer.Tests;

/// <summary>
/// End-to-end indexing against the repo's synthetic, copyright-safe example
/// game (and example mod). Unlike <see cref="IntegrationTests"/>, this needs no
/// real game files — only a cwtools config dir (CI provides one via
/// <c>eu4indexer setup</c> + EU4_CONFIG_DIR). No-ops when no config is present.
/// </summary>
public class FixtureIndexTests
{
    private static FSharpList<string> ToFs(params string[] items) => ListModule.OfSeq(items);

    private static long Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public void Index_ExampleGame_ProducesQueryableDatabase()
    {
        var gameDir = TestPaths.FixtureGameDir;
        var configDir = TestPaths.ConfigDir;
        if (gameDir is null || configDir is null) return;

        var dbPath = Path.Combine(Path.GetTempPath(), $"eu4_fixture_{Guid.NewGuid():N}.db");

        try
        {
            var result = Pipeline.runWithPaths(
                GameAdapterModule.eu4, gameDir, ToFs(), configDir, dbPath,
                skipGeneric: false, withFts: true, languages: ToFs("english"),
                log: FuncConvert.FromAction<string>(_ => { }));

            Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");
            var report = result.ResultValue;

            Assert.Equal(0, report.ParseErrorCount);
            Assert.Equal(0, report.ForeignKeyViolations);
            Assert.True(report.EntityCount >= 5, $"expected the fixture's entities, got {report.EntityCount}");

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            // The two synthetic events are present and effective.
            Assert.True(Scalar(conn, "SELECT count(*) FROM entities WHERE entity_type='event' AND is_effective=1") >= 2);

            // fixture.2's option uses add_stability — the proof query returns it.
            Assert.True(Scalar(conn,
                "SELECT count(DISTINCT e.entity_id) FROM entities e " +
                "JOIN script_nodes n ON n.entity_id=e.entity_id " +
                "JOIN symbols s ON s.symbol_id=n.symbol_id " +
                "WHERE e.entity_type='event' AND s.kind='effect' AND s.name='add_stability'") > 0);

            // Localisation decoded for the fixture keys.
            Assert.True(Scalar(conn, "SELECT count(*) FROM localisation WHERE loc_key='fixture.1.t'") > 0);

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
    public void Index_Hoi4Fixture_ProducesQueryableDatabase()
    {
        var gameDir = TestPaths.FixtureHoi4GameDir;
        var configDir = TestPaths.Hoi4ConfigDir;
        if (gameDir is null || configDir is null) return;

        var dbPath = Path.Combine(Path.GetTempPath(), $"hoi4_fixture_{Guid.NewGuid():N}.db");

        try
        {
            var result = Pipeline.runWithPaths(
                GameAdapterModule.hoi4, gameDir, ToFs(), configDir, dbPath,
                skipGeneric: false, withFts: true, languages: ToFs("english", "simp_chinese"),
                log: FuncConvert.FromAction<string>(_ => { }));

            Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");
            var report = result.ResultValue;

            Assert.Equal(0, report.ParseErrorCount);
            Assert.Equal(0, report.ForeignKeyViolations);
            Assert.True(report.EntityCount >= 6, $"expected ≥6 entities, got {report.EntityCount}");

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            Assert.True(Scalar(conn, "SELECT count(*) FROM entities WHERE entity_type='focus' AND is_effective=1") >= 3);
            Assert.True(Scalar(conn, "SELECT count(*) FROM entities WHERE entity_type='focus_tree' AND is_effective=1") >= 1);
            Assert.True(Scalar(conn, "SELECT count(*) FROM entities WHERE entity_type='idea' AND is_effective=1") >= 1);
            Assert.True(Scalar(conn, "SELECT count(*) FROM entities WHERE entity_type='event' AND is_effective=1") >= 2);

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
    public void Index_ExampleGameWithMod_RecordsOverrides()
    {
        var gameDir = TestPaths.FixtureGameDir;
        var modDir = TestPaths.FixtureModDir;
        var configDir = TestPaths.ConfigDir;
        if (gameDir is null || configDir is null || modDir is null) return;

        var dbPath = Path.Combine(Path.GetTempPath(), $"eu4_fixture_mod_{Guid.NewGuid():N}.db");

        try
        {
            var result = Pipeline.runWithPaths(
                GameAdapterModule.eu4, gameDir, ToFs(modDir), configDir, dbPath,
                skipGeneric: false, withFts: false, languages: ToFs("english"),
                log: FuncConvert.FromAction<string>(_ => { }));

            Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");
            var report = result.ResultValue;

            Assert.Equal(0, report.ForeignKeyViolations);
            // The mod shadows events/fixture_events.txt, so overrides are recorded.
            Assert.True(report.OverrideCount > 0, "expected the example mod to record overrides");

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            // Two sources: base game + the example mod.
            Assert.Equal(2, Scalar(conn, "SELECT count(*) FROM sources"));
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }
}

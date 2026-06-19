using Eu4Indexer.Core;
using Eu4Indexer.Core.Database;
using Microsoft.Data.Sqlite;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Eu4Indexer.Tests;

/// End-to-end indexing of the synthetic HOI4 fixture game. Skips when the
/// cwtools-hoi4-config is not installed on the machine.
public class Hoi4FixtureIndexTests
{
    private static FSharpList<string> ToFs(params string[] items) => ListModule.OfSeq(items);

    private static long Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
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

            // Entity types present.
            Assert.True(Scalar(conn, "SELECT count(*) FROM entities WHERE entity_type='event' AND is_effective=1") >= 2);
            Assert.True(Scalar(conn, "SELECT count(*) FROM entities WHERE entity_type='focus_tree' AND is_effective=1") >= 1);
            Assert.True(Scalar(conn, "SELECT count(*) FROM entities WHERE entity_type='focus' AND is_effective=1") >= 3);
            Assert.True(Scalar(conn, "SELECT count(*) FROM entities WHERE entity_type='idea' AND is_effective=1") >= 1);

            // Localisation in both languages.
            var englishLoc = Scalar(conn, "SELECT count(*) FROM localisation WHERE language='english' AND is_effective=1");
            var chineseLoc = Scalar(conn, "SELECT count(*) FROM localisation WHERE language='simp_chinese' AND is_effective=1");
            Assert.True(englishLoc > 0, $"expected english loc, got {englishLoc}");
            Assert.True(chineseLoc > 0, $"expected simp_chinese loc, got {chineseLoc}");

            // Focus prerequisites captured in focus_requirements.
            var focus2Id = Scalar(conn,
                "SELECT entity_id FROM entities WHERE entity_key='fixture_focus_2' AND is_effective=1");
            Assert.True(focus2Id > 0);
            var prereq = Scalar(conn,
                $"SELECT count(*) FROM focus_requirements WHERE entity_id={focus2Id}");
            Assert.True(prereq > 0, $"expected prerequisite for fixture_focus_2");

            // Idea modifier values.
            var ideaId = Scalar(conn,
                "SELECT entity_id FROM entities WHERE entity_key='fixture_advisor' AND is_effective=1");
            Assert.True(ideaId > 0);
            Assert.True(Scalar(conn,
                $"SELECT count(*) FROM modifier_values WHERE entity_id={ideaId} AND modifier_key='political_power_factor'") > 0);

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
    public void Index_Hoi4Fixture_EventsHaveReferenceEdges()
    {
        var gameDir = TestPaths.FixtureHoi4GameDir;
        var configDir = TestPaths.Hoi4ConfigDir;
        if (gameDir is null || configDir is null) return;

        var dbPath = Path.Combine(Path.GetTempPath(), $"hoi4_fixture_refs_{Guid.NewGuid():N}.db");

        try
        {
            var result = Pipeline.runWithPaths(
                GameAdapterModule.hoi4, gameDir, ToFs(), configDir, dbPath,
                skipGeneric: false, withFts: false, languages: ToFs("english"),
                log: FuncConvert.FromAction<string>(_ => { }));

            Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");
            var report = result.ResultValue;
            Assert.True(report.ParseErrorCount == 0, $"parse errors: {report.ParseErrorCount}");

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            // Reference edges exist.
            var refCount = Scalar(conn, "SELECT count(*) FROM refs");
            Assert.True(refCount > 0, $"expected reference edges, got {refCount}");

            // fixture.1 fires fixture.2.
            var firesEvent = Scalar(conn,
                "SELECT count(*) FROM refs WHERE ref_kind='fires_event' AND target_key='fixture.2'");
            Assert.True(firesEvent > 0, "expected fixture.1 to fire fixture.2");

            // At least one sets_flag reference.
            var setsFlag = Scalar(conn,
                "SELECT count(*) FROM refs WHERE ref_kind='sets_flag'");
            Assert.True(setsFlag > 0, $"expected sets_flag refs, got {setsFlag}");
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }
}

using System.Data.Common;
using Eu4Indexer.Core;
using Microsoft.Data.Sqlite;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Npgsql;

namespace Eu4Indexer.Tests;

/// <summary>
/// Verifies the PostgreSQL export backend. Builds the same index into SQLite and
/// into Postgres and asserts the row counts match, plus that the pg_trgm GIN
/// search index exists and substring search works. No-ops unless the game,
/// config, and an <c>EU4_PG_CONN</c> connection string are all available, so the
/// suite stays green without a database.
/// </summary>
public class PostgresExportTests
{
    private static FSharpList<string> ToFs(params string[] items) => ListModule.OfSeq(items);

    [Fact]
    public void Export_ToPostgres_MatchesSqliteRowCounts()
    {
        var gameDir = TestPaths.GameDir;
        var configDir = TestPaths.ConfigDir;
        var pgConn = TestPaths.PostgresConn;

        if (gameDir is null || configDir is null || pgConn is null)
            return;

        var mods = TestPaths.ExampleModDir is { } modDir ? ToFs(modDir) : ToFs();
        var dbPath = Path.Combine(Path.GetTempPath(), $"eu4_pgcmp_{Guid.NewGuid():N}.db");

        try
        {
            // Build the identical index into both backends.
            var sqliteResult = Pipeline.runWithPaths(
                GameAdapterModule.eu4, gameDir, mods, configDir, dbPath,
                skipGeneric: false, withFts: true, languages: ToFs("english"),
                log: FuncConvert.FromAction<string>(_ => { }));
            Assert.True(sqliteResult.IsOk, sqliteResult.IsError ? sqliteResult.ErrorValue : "");

            var pgResult = Pipeline.runWithPaths(
                GameAdapterModule.eu4, gameDir, mods, configDir, pgConn,
                skipGeneric: false, withFts: true, languages: ToFs("english"),
                log: FuncConvert.FromAction<string>(_ => { }));
            Assert.True(pgResult.IsOk, pgResult.IsError ? pgResult.ErrorValue : "");

            using var sqlite = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            sqlite.Open();
            using var pg = new NpgsqlConnection(pgConn);
            pg.Open();

            // Every core table must be present in Postgres with the same row count.
            var tables = new[]
            {
                "sources", "files", "entities", "script_nodes",
                "localisation", "refs", "symbols", "event_options",
            };

            foreach (var table in tables)
            {
                var sqliteCount = ScalarLong(sqlite, $"SELECT count(*) FROM {table}");
                var pgCount = ScalarLong(pg, $"SELECT count(*) FROM {table}");
                Assert.True(sqliteCount > 0, $"{table} should have rows after indexing");
                Assert.Equal(sqliteCount, pgCount);
            }

            // The pg_trgm GIN index exists and substring search returns a hit,
            // which is what gives CJK / colour-split localisation searchability.
            Assert.Equal(
                1,
                ScalarLong(pg, "SELECT count(*) FROM pg_indexes WHERE indexname = 'idx_loc_value_trgm'"));

            var sample = ScalarString(
                pg, "SELECT value_plain FROM localisation WHERE length(value_plain) >= 6 LIMIT 1");
            Assert.NotNull(sample);

            using var search = pg.CreateCommand();
            search.CommandText = "SELECT count(*) FROM localisation WHERE value_plain ILIKE $1";
            var needle = search.CreateParameter();
            needle.Value = "%" + sample!.Substring(1, 3) + "%";
            search.Parameters.Add(needle);
            Assert.True(Convert.ToInt64(search.ExecuteScalar()) >= 1);
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }

    private static long ScalarLong(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string? ScalarString(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? null : (string)result;
    }
}

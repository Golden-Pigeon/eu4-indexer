using Eu4Indexer.Core;
using Microsoft.Data.Sqlite;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Eu4Indexer.Tests;

/// <summary>
/// Covers the inputs that <c>refresh</c> reads back from a built index's
/// <c>meta</c> table. The encode/decode round-trip is a pure unit test (a
/// minimal in-memory-style meta table, no game data), while the flag-persistence
/// test indexes the synthetic fixture and so no-ops without a config dir.
/// </summary>
public class RefreshInputsTests
{
    private static FSharpList<string> ToFs(params string[] items) => ListModule.OfSeq(items);

    private static FSharpOption<string> Some(string s) => FSharpOption<string>.Some(s);

    /// <summary>Build a meta table with the given rows and read it back.</summary>
    private static IndexInputs.Inputs ReadFromMeta(IEnumerable<(string Key, string Value)> rows)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"eu4_meta_{Guid.NewGuid():N}.db");

        try
        {
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using (var create = conn.CreateCommand())
                {
                    create.CommandText = "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)";
                    create.ExecuteNonQuery();
                }

                foreach (var (key, value) in rows)
                {
                    using var insert = conn.CreateCommand();
                    insert.CommandText = "INSERT INTO meta (key, value) VALUES ($k, $v)";
                    insert.Parameters.AddWithValue("$k", key);
                    insert.Parameters.AddWithValue("$v", value);
                    insert.ExecuteNonQuery();
                }
            }

            SqliteConnection.ClearAllPools();
            return IndexInputs.read(dbPath);
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void Read_RoundTripsSourceSelectionWrittenByToExtraMeta()
    {
        // Arrange: the exact rows the CLI persists for a workshop + playset index.
        var rows = new List<(string, string)>
        {
            ("game_id", "hoi4"),
            ("config_repo_path", "/cfg/hoi4"),
            ("languages", "english,french"),
            ("skip_generic", "1"),
            ("with_fts", "0"),
        };

        foreach (var kv in IndexInputs.toExtraMeta(
                     Some("/games/hoi4"), ToFs("/mods/alpha", "/mods/beta"),
                     ToFs("111", "222"), Some("MyPlayset"), autoMods: true))
            rows.Add((kv.Item1, kv.Item2));

        // Act
        var inputs = ReadFromMeta(rows);

        // Assert
        Assert.Equal("hoi4", inputs.GameId);
        Assert.Equal("/cfg/hoi4", inputs.ConfigDir);
        Assert.Equal(new[] { "english", "french" }, inputs.Languages);
        Assert.True(inputs.SkipGeneric);
        Assert.False(inputs.WithFts);
        Assert.Equal("/games/hoi4", inputs.ExplicitGameDir.Value);
        Assert.Equal(new[] { "/mods/alpha", "/mods/beta" }, inputs.ExplicitMods);
        Assert.Equal(new[] { "111", "222" }, inputs.WorkshopIds);
        Assert.Equal("MyPlayset", inputs.Playset.Value);
        Assert.True(inputs.AutoMods);
    }

    [Fact]
    public void Read_EmptySelection_YieldsNonesAndEmptyLists()
    {
        var rows = new List<(string, string)> { ("game_id", "eu4") };
        foreach (var kv in IndexInputs.toExtraMeta(
                     FSharpOption<string>.None, ToFs(), ToFs(),
                     FSharpOption<string>.None, autoMods: false))
            rows.Add((kv.Item1, kv.Item2));

        var inputs = ReadFromMeta(rows);

        // Empty meta values decode to None / empty, not "" entries.
        Assert.True(FSharpOption<string>.get_IsNone(inputs.ExplicitGameDir));
        Assert.Empty(inputs.ExplicitMods);
        Assert.Empty(inputs.WorkshopIds);
        Assert.True(FSharpOption<string>.get_IsNone(inputs.Playset));
        Assert.False(inputs.AutoMods);
    }

    [Fact]
    public void Read_MissingKeys_FallBackToSafeDefaults()
    {
        // An index built before these keys existed: only game_id present.
        var inputs = ReadFromMeta(new[] { ("game_id", "eu4") });

        Assert.Equal("eu4", inputs.GameId);
        Assert.Equal("", inputs.ConfigDir);
        Assert.Empty(inputs.Languages);
        Assert.False(inputs.SkipGeneric); // defaults to full generic extraction
        Assert.True(inputs.WithFts); // defaults to FTS on
        Assert.Empty(inputs.ExplicitMods);
        Assert.False(inputs.AutoMods);
    }

    [Fact]
    public void Index_PersistsFlagsAndLanguagesToMeta()
    {
        var gameDir = TestPaths.FixtureGameDir;
        var configDir = TestPaths.ConfigDir;
        if (gameDir is null || configDir is null) return;

        var dbPath = Path.Combine(Path.GetTempPath(), $"eu4_refresh_{Guid.NewGuid():N}.db");

        try
        {
            var result = Pipeline.runWithPaths(
                GameAdapterModule.eu4, gameDir, ToFs(), configDir, dbPath,
                skipGeneric: true, withFts: false, languages: ToFs("english"),
                log: FuncConvert.FromAction<string>(_ => { }));

            Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");

            var inputs = IndexInputs.read(dbPath);
            Assert.Equal("eu4", inputs.GameId);
            Assert.Equal(configDir, inputs.ConfigDir);
            Assert.Equal(new[] { "english" }, inputs.Languages);
            Assert.True(inputs.SkipGeneric);
            Assert.False(inputs.WithFts);
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }
}

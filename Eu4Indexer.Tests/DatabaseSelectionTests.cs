using Eu4Indexer.Core;
using Eu4Indexer.Core.Database;
using Eu4Indexer.Mcp;
using Microsoft.Data.Sqlite;

namespace Eu4Indexer.Tests;

public sealed class DatabaseSelectionTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "selection_" + Guid.NewGuid().ToString("N"));
    private readonly string? _previousHome = Environment.GetEnvironmentVariable("EU4INDEXER_HOME");
    private readonly string _a;

    public DatabaseSelectionTests()
    {
        Directory.CreateDirectory(_home);
        Environment.SetEnvironmentVariable("EU4INDEXER_HOME", _home);
        _a = CreateDatabase("a");
        var b = CreateDatabase("b");
        Registry.save(new Registry.Config("a", new[] {
            new Registry.DbEntry("a", "eu4", _a, Schema.UserVersion, 1, "2026-11-07"),
            new Registry.DbEntry("b", "hoi4", b, Schema.UserVersion, 2, "2026-11-07"),
            new Registry.DbEntry("broken", "eu4", Path.Combine(_home, "missing.db"), Schema.UserVersion, 0, "2026-11-07")
        }));
    }

    private string CreateDatabase(string name)
    {
        var path = Path.Combine(_home, name + ".db");
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA user_version={Schema.UserVersion}; CREATE TABLE meta(key TEXT PRIMARY KEY,value TEXT); INSERT INTO meta VALUES('game_id','eu4');";
        cmd.ExecuteNonQuery();
        return path;
    }

    [Fact]
    public void NamedSessionsAndLegacyDefaultAreIndependent()
    {
        var selection = new DatabaseSelection(_a);
        using (selection.EnterSession("one")) { selection.Select("b"); Assert.Equal("b", selection.CurrentName); }
        Assert.Equal("a", selection.CurrentName);
        selection.Select("b");
        using (selection.EnterSession("two")) Assert.Equal("a", selection.CurrentName);
        using (selection.EnterSession("one")) Assert.Equal("b", selection.CurrentName);
        Assert.Equal("b", selection.CurrentName);
    }

    [Fact]
    public async Task AsyncRequestsRetainTheirOwnSelection()
    {
        var selection = new DatabaseSelection(_a);
        await Task.WhenAll(Enumerable.Range(0, 30).Select(async i => {
            using var scope = selection.EnterSession("session-" + i);
            var name = i % 2 == 0 ? "a" : "b";
            selection.Select(name);
            await Task.Delay(i % 5);
            Assert.Equal(name, selection.CurrentName);
            Assert.Single(selection.List(), db => db.IsActive);
        }));
        Assert.Equal("a", selection.CurrentName);
    }

    [Fact]
    public void NestedScopeRestoresAndFailedSelectionKeepsPreviousDatabase()
    {
        var selection = new DatabaseSelection(_a);
        using (selection.EnterSession("outer")) {
            selection.Select("b");
            using (selection.EnterSession("inner")) Assert.Equal("a", selection.CurrentName);
            Assert.Equal("b", selection.CurrentName);
            Assert.StartsWith("error:", selection.Select("broken"));
            Assert.Equal("b", selection.CurrentName);
        }
        Assert.Null(selection.SessionId);
    }

    [Fact]
    public void SessionsAreBoundedAndExplicitlyReleasable()
    {
        var selection = new DatabaseSelection(_a, maxSessions: 1);
        using (selection.EnterSession("one")) selection.Select("b");
        Assert.Throws<InvalidOperationException>(() => selection.EnterSession("two"));
        Assert.True(selection.CloseSession("one"));
        using (selection.EnterSession("one")) Assert.Equal("a", selection.CurrentName);
        Assert.False(selection.CloseSession("unknown"));
        Assert.Throws<ArgumentException>(() => selection.EnterSession("bad\nname"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("EU4INDEXER_HOME", _previousHome);
        SqliteConnection.ClearAllPools();
        Directory.Delete(_home, recursive: true);
    }
}

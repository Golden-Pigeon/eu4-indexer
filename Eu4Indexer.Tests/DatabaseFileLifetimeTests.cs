using Eu4Indexer.Core;
using Eu4Indexer.Mcp;
using Microsoft.Data.Sqlite;

namespace Eu4Indexer.Tests;

public sealed class DatabaseFileLifetimeTests
{
    private static void CreateIndex(string path, string game)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);" +
            "INSERT INTO meta VALUES ('game_id', $game);";
        command.Parameters.AddWithValue("$game", game);
        command.ExecuteNonQuery();
    }

    [Fact]
    public void McpQuery_ReleasesFileAndReadsItsReplacement()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eu4_query_lifetime_{Guid.NewGuid():N}.db");
        try
        {
            CreateIndex(path, "eu4");
            var database = new Eu4Database(path);
            Assert.Equal("eu4", database.QueryScalar<string>("SELECT value FROM meta WHERE key='game_id'"));

            File.Delete(path);
            CreateIndex(path, "hoi4");

            Assert.Equal("hoi4", database.QueryScalar<string>("SELECT value FROM meta WHERE key='game_id'"));
        }
        finally
        {
            SqliteTestDatabase.Delete(path);
        }
    }

    [Fact]
    public void IndexInputsRead_ReleasesFileAndReadsItsReplacement()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eu4_input_lifetime_{Guid.NewGuid():N}.db");
        try
        {
            CreateIndex(path, "eu4");
            Assert.Equal("eu4", IndexInputs.read(path).GameId);

            File.Delete(path);
            CreateIndex(path, "hoi4");

            Assert.Equal("hoi4", IndexInputs.read(path).GameId);
        }
        finally
        {
            SqliteTestDatabase.Delete(path);
        }
    }
}

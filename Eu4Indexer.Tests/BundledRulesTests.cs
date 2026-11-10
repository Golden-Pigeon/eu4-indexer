using Eu4Indexer.Core;
using Microsoft.Data.Sqlite;

namespace Eu4Indexer.Tests;

public class BundledRulesTests
{
    [Theory]
    [InlineData("eu4")]
    [InlineData("hoi4")]
    public void BuildOutputContainsCompleteRulesAndLicense(string game)
    {
        var directory = AppPaths.bundledConfigDir(game);
        Assert.True(File.Exists(Path.Combine(directory, "LICENSE")));
        Assert.True(File.Exists(Path.Combine(directory, game == "hoi4" ? "Config/scopes.cwt" : "scopes.cwt")));
        Assert.NotEmpty(Directory.EnumerateFiles(directory, "*.cwt", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("bundled", "")]
    [InlineData("external", "/custom/rules")]
    public void RefreshInputsTrackRuleOriginInsteadOfAnObsoleteEnginePath(string source, string expected)
    {
        var file = Path.Combine(Path.GetTempPath(), "bundled-rules-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString()))
            {
                c.Open();
                using var command = c.CreateCommand();
                command.CommandText = "CREATE TABLE meta(key TEXT PRIMARY KEY,value TEXT); INSERT INTO meta VALUES('game_id','eu4'),('config_repo_path','/custom/rules'),('config_source',$source)";
                command.Parameters.AddWithValue("$source", source);
                command.ExecuteNonQuery();
            }
            Assert.Equal(expected, IndexInputs.read(file).ConfigDir);
        }
        finally { File.Delete(file); }
    }
}

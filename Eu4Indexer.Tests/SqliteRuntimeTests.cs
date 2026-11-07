using Microsoft.Data.Sqlite;
using Xunit;

namespace Eu4Indexer.Tests;

public class SqliteRuntimeTests
{
    [Fact]
    public void BundledSqliteMeetsSecurityBaselineAndRetainsFullTextSearch()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version()";
        var version = Version.Parse((string)command.ExecuteScalar()!);
        Assert.True(version >= new Version(3, 50, 2), $"Vulnerable SQLite runtime: {version}");
        command.CommandText = "CREATE VIRTUAL TABLE documents USING fts5(content); INSERT INTO documents VALUES ('indexed scripts'); SELECT count(*) FROM documents WHERE documents MATCH 'scripts'";
        Assert.Equal(1L, command.ExecuteScalar());
    }
}

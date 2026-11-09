using Microsoft.Data.Sqlite;

namespace Eu4Indexer.Tests;

public sealed class SqliteTestDatabaseTests
{
    [Fact]
    public void Delete_ClosesPooledReadAndWriteHandlesBeforeRemovingFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eu4_pool_cleanup_{Guid.NewGuid():N}.db");

        try
        {
            using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = true,
            }.ToString());
            writer.Open();
            using (var command = writer.CreateCommand())
            {
                command.CommandText = "CREATE TABLE fixture (id INTEGER PRIMARY KEY)";
                command.ExecuteNonQuery();
            }
            var writeHandle = writer.Handle!;
            writer.Dispose();

            using var reader = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = true,
            }.ToString());
            reader.Open();
            var readHandle = reader.Handle!;
            reader.Dispose();

            Assert.False(writeHandle.IsClosed);
            Assert.False(readHandle.IsClosed);

            SqliteTestDatabase.Delete(path);

            Assert.True(writeHandle.IsClosed);
            Assert.True(readHandle.IsClosed);
            Assert.False(File.Exists(path));
            Assert.False(File.Exists(path + "-wal"));
            Assert.False(File.Exists(path + "-shm"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }
    }
}

using Microsoft.Data.Sqlite;

namespace Eu4Indexer.Tests;

internal static class SqliteTestDatabase
{
    public static void Delete(string path)
    {
        // Disposed pooled connections still hold native handles on Windows.
        // The test suite runs serially, so all temporary-database pools can close.
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
    }
}

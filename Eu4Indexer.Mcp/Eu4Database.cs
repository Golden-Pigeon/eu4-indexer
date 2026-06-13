using Eu4Indexer.Core.Database;
using Microsoft.Data.Sqlite;

namespace Eu4Indexer.Mcp;

/// <summary>
/// Read-only access to a built eu4-indexer database. Opens a fresh read-only
/// connection per query (Microsoft.Data.Sqlite pools them by connection
/// string), enforces parameterized SQL, a statement timeout, and bounded
/// result sets. The database is treated as an immutable artifact.
/// </summary>
public sealed class Eu4Database
{
    private const int CommandTimeoutSeconds = 30;

    private readonly string _connectionString;

    public Eu4Database(string dbPath)
    {
        if (!File.Exists(dbPath))
            throw new FileNotFoundException($"Index database not found: {dbPath}");

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
    }

    /// Fail fast if the database was built by a different indexer schema version.
    public void EnsureSchemaVersion()
    {
        var version = QueryScalar<long>("PRAGMA user_version");

        if (version != Schema.UserVersion)
            throw new InvalidOperationException(
                $"Index schema version {version} does not match the expected {Schema.UserVersion}. " +
                "Rebuild the index with the current eu4-indexer.");
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public List<T> Query<T>(
        string sql,
        Func<SqliteDataReader, T> map,
        IReadOnlyDictionary<string, object?>? parameters = null,
        int? limit = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = CommandTimeoutSeconds;

        if (parameters is not null)
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

        using var reader = cmd.ExecuteReader();
        var rows = new List<T>();

        while (reader.Read())
        {
            rows.Add(map(reader));

            if (limit is int max && rows.Count >= max)
                break;
        }

        return rows;
    }

    public T QueryScalar<T>(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = CommandTimeoutSeconds;

        if (parameters is not null)
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? default! : (T)Convert.ChangeType(result, typeof(T));
    }

    /// <summary>
    /// Wrap a user string as an FTS5 literal phrase so special characters can't
    /// be interpreted as FTS query operators.
    /// </summary>
    public static string FtsPhrase(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// Load an entity's script tree as nested nodes (roots first), capped at
    /// <paramref name="maxNodes"/>. Returns the roots and whether it was truncated.
    /// </summary>
    public (List<ScriptNodeDto> Roots, bool Truncated) LoadScriptTree(long entityId, int maxNodes = 400)
    {
        var flat = Query(
            """
            SELECT n.node_id, n.parent_id, n.context, n.key, n.operator, n.value,
                   s.kind AS symbol_kind, s.name AS symbol_name
            FROM script_nodes n
            LEFT JOIN symbols s ON s.symbol_id = n.symbol_id
            WHERE n.entity_id = $id
            ORDER BY n.depth, n.sort_order
            """,
            r => new
            {
                NodeId = r.GetInt64(0),
                ParentId = r.IsDBNull(1) ? (long?)null : r.GetInt64(1),
                Context = r.GetString(2),
                Key = r.IsDBNull(3) ? null : r.GetString(3),
                Operator = r.IsDBNull(4) ? null : r.GetString(4),
                Value = r.IsDBNull(5) ? null : r.GetString(5),
                SymbolKind = r.IsDBNull(6) ? null : r.GetString(6),
                SymbolName = r.IsDBNull(7) ? null : r.GetString(7),
            },
            new Dictionary<string, object?> { ["$id"] = entityId });

        var truncated = flat.Count > maxNodes;
        var kept = truncated ? flat.Take(maxNodes).ToList() : flat;

        var nodes = kept.ToDictionary(
            x => x.NodeId,
            x => new ScriptNodeDto(x.Key, x.Operator, x.Value, x.Context, x.SymbolKind, x.SymbolName, new List<ScriptNodeDto>()));

        var roots = new List<ScriptNodeDto>();

        foreach (var row in kept)
        {
            var dto = nodes[row.NodeId];

            if (row.ParentId is long pid && nodes.TryGetValue(pid, out var parent))
                parent.Children.Add(dto);
            else
                roots.Add(dto);
        }

        return (roots, truncated);
    }
}

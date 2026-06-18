using Eu4Indexer.Core;

namespace Eu4Indexer.Mcp;

/// <summary>
/// Holds the database the tools currently query and can switch between the
/// indexes registered for this installation (~/.eu4indexer/config.json). A
/// single MCP session can therefore answer questions about different mod sets:
/// the agent calls list_databases / select_database to pick the right one.
///
/// Registered as a singleton; the tools receive <see cref="Eu4Database"/> via a
/// transient factory that returns <see cref="Current"/>, so they need no changes
/// and always see the active selection.
/// </summary>
public sealed class DatabaseSelection
{
    private readonly Dictionary<string, Eu4Database> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Eu4Database Current { get; private set; }

    /// <summary>The registry name of the current database, or "" if ad-hoc.</summary>
    public string CurrentName { get; private set; }

    public DatabaseSelection(string initialPath)
    {
        Current = Open(initialPath);
        CurrentName = NameForPath(initialPath) ?? "";
    }

    private Eu4Database Open(string path)
    {
        var key = path.Replace('\\', '/');

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var db = new Eu4Database(path);
        db.EnsureSchemaVersion();
        _cache[key] = db;
        return db;
    }

    private static string? NameForPath(string path)
    {
        var normalized = path.Replace('\\', '/');

        foreach (var entry in Registry.load().Databases)
            if (string.Equals(entry.Path.Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase))
                return entry.Name;

        return null;
    }

    /// <summary>The indexes known to this installation, marking the active one.</summary>
    public List<DatabaseInfo> List() =>
        Registry.load().Databases
            .Select(e => new DatabaseInfo(e.Name, e.Game, e.Sources, e.IndexedAt, e.Name == CurrentName))
            .ToList();

    /// <summary>
    /// Make a registered index active by name. Returns a human-readable result
    /// (the tool surfaces it to the agent rather than throwing).
    /// </summary>
    public string Select(string name)
    {
        var entry = Registry.load().Databases.FirstOrDefault(e => e.Name == name);

        if (entry is null)
            return $"error: no registered index named '{name}'. Call list_databases to see the options.";

        try
        {
            Current = Open(entry.Path);
            CurrentName = entry.Name;
            return $"Selected '{name}' ({entry.Game}, {entry.Sources} sources).";
        }
        catch (Exception ex)
        {
            return $"error: could not open index '{name}': {ex.Message}";
        }
    }
}

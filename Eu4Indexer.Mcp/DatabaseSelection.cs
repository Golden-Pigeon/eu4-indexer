using System.Collections.Concurrent;
using Eu4Indexer.Core;

namespace Eu4Indexer.Mcp;

/// <summary>
/// Connection-local default selection plus isolated logical query sessions.
/// The MCP request filter binds a logical session with eu4indexer/sessionId.
/// Legacy callers continue to use the connection's default selection.
/// </summary>
public sealed class DatabaseSelection
{
    private sealed record Selection(Eu4Database Database, string Name);
    private readonly ConcurrentDictionary<string, Lazy<Eu4Database>> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Selection> _sessions = new(StringComparer.Ordinal);
    private readonly AsyncLocal<string?> _session = new();
    private readonly Selection _initial;
    private Selection _default;
    private readonly object _sessionGate = new();
    private readonly int _maxSessions;

    public Eu4Database Current => Snapshot.Database;
    public string CurrentName => Snapshot.Name;
    public string? SessionId => _session.Value;
    private Selection Snapshot => _session.Value is { } id
        ? (_sessions.TryGetValue(id, out var selection) ? selection : _initial)
        : Volatile.Read(ref _default);

    public DatabaseSelection(string initialPath, int maxSessions = 4096)
    {
        if (maxSessions < 1) throw new ArgumentOutOfRangeException(nameof(maxSessions));
        _maxSessions = maxSessions;
        _initial = new Selection(Open(initialPath), NameForPath(initialPath) ?? "");
        _default = _initial;
    }

    /// <summary>Bind a request without changing the caller's surrounding async context.</summary>
    public IDisposable EnterSession(string? id)
    {
        if (id is not null)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(char.IsControl))
                throw new ArgumentException("Query session ids must contain 1-128 non-control characters.", nameof(id));
            lock (_sessionGate)
            {
                if (!_sessions.ContainsKey(id))
                {
                    if (_sessions.Count >= _maxSessions)
                        throw new InvalidOperationException("Query session limit reached; close unused query sessions.");
                    _sessions[id] = _initial;
                }
            }
        }
        var previous = _session.Value;
        _session.Value = id;
        return new Restore(() => _session.Value = previous);
    }

    private sealed class Restore(Action action) : IDisposable
    {
        private Action? _action = action;
        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }

    /// <summary>Reset a named session; already executing requests keep their resolved database.</summary>
    public bool CloseSession(string id)
    {
        lock (_sessionGate) return _sessions.TryRemove(id, out _);
    }

    private Eu4Database Open(string path)
    {
        var key = Path.GetFullPath(path);
        var lazy = _cache.GetOrAdd(key, p => new Lazy<Eu4Database>(() =>
        {
            var db = new Eu4Database(p);
            db.EnsureSchemaVersion();
            return db;
        }));
        try { return lazy.Value; }
        catch { _cache.TryRemove(new KeyValuePair<string, Lazy<Eu4Database>>(key, lazy)); throw; }
    }

    private static string? NameForPath(string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return Registry.load().Databases.FirstOrDefault(e =>
            string.Equals(Path.GetFullPath(e.Path), Path.GetFullPath(path), comparison))?.Name;
    }

    public List<DatabaseInfo> List()
    {
        var selected = CurrentName;
        return Registry.load().Databases.Select(e =>
            new DatabaseInfo(e.Name, e.Game, e.Sources, e.IndexedAt, e.Name == selected)).ToList();
    }

    public string Select(string name)
    {
        var entry = Registry.load().Databases.FirstOrDefault(e => e.Name == name);
        if (entry is null)
            return $"error: no registered index named '{name}'. Call list_databases to see the options.";
        try
        {
            var selection = new Selection(Open(entry.Path), entry.Name);
            if (_session.Value is { } id) _sessions[id] = selection;
            else Volatile.Write(ref _default, selection);
            return $"Selected '{name}' ({entry.Game}, {entry.Sources} sources).";
        }
        catch (Exception ex) { return $"error: could not open index '{name}': {ex.Message}"; }
    }
}

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Eu4Indexer.Mcp;

/// <summary>
/// Tools for choosing which indexed database the other tools query, when an
/// installation holds several (e.g. vanilla vs. a specific mod set / playset).
/// </summary>
[McpServerToolType]
public static class SelectionTools
{
    [McpServerTool(Name = "close_query_session")]
    [Description("Release an unused logical query session. All calls using eu4indexer/sessionId in _meta have isolated database selection; omit it for legacy connection-local selection.")]
    public static bool CloseQuerySession(DatabaseSelection selection, string sessionId) => selection.CloseSession(sessionId);

    [McpServerTool(Name = "list_databases")]
    [Description(
        "List the indexed databases registered for this installation, marking the " +
        "active one for the current query session. When more than one exists and the user's question names a " +
        "particular mod or playset, call select_database to switch to the matching " +
        "index before querying. With a single database you can ignore these tools.")]
    public static List<DatabaseInfo> ListDatabases(DatabaseSelection selection) => selection.List();

    [McpServerTool(Name = "select_database")]
    [Description(
        "Switch the database queried by tools in the current query session. Pass a name from " +
        "list_databases. The selection persists for this session and does not alter other sessions or the registry default.")]
    public static string SelectDatabase(
        DatabaseSelection selection,
        [Description("The registry name of the database to activate (from list_databases).")] string name)
        => selection.Select(name);
}

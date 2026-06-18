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
    [McpServerTool(Name = "list_databases")]
    [Description(
        "List the indexed databases registered for this installation, marking the " +
        "active one. When more than one exists and the user's question names a " +
        "particular mod or playset, call select_database to switch to the matching " +
        "index before querying. With a single database you can ignore these tools.")]
    public static List<DatabaseInfo> ListDatabases(DatabaseSelection selection) => selection.List();

    [McpServerTool(Name = "select_database")]
    [Description(
        "Switch the active database that every other tool queries. Pass a name from " +
        "list_databases. The selection persists for the rest of the session.")]
    public static string SelectDatabase(
        DatabaseSelection selection,
        [Description("The registry name of the database to activate (from list_databases).")] string name)
        => selection.Select(name);
}

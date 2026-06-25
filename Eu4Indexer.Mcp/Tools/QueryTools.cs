using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Eu4Indexer.Mcp;

/// <summary>Escape hatch for ad-hoc read-only SQL when the curated tools fall short.</summary>
[McpServerToolType]
public static class QueryTools
{
    [McpServerTool(Name = "read_query")]
    [Description(
        "Run a single read-only SELECT (or WITH ... SELECT) against the index and return " +
        "the rows. Use this only when the curated tools cannot express what you need; " +
        "prefer describe_schema first to learn the tables. Non-SELECT statements, multiple " +
        "statements, and writes are rejected (the connection is read-only).")]
    public static QueryResult ReadQuery(
        Eu4Database db,
        [Description("A single SELECT/WITH query.")] string sql,
        [Description("Maximum rows to return (default 100, max 1000).")] int limit = 100)
    {
        try
        {
            return db.ReadQuery(sql, Math.Clamp(limit, 1, 1000));
        }
        catch (Exception ex)
        {
            // Surface the real reason — validation message, SQLite error, or query
            // timeout — instead of letting the SDK collapse every failure into a
            // generic "An error occurred invoking 'read_query'." McpException's
            // message is passed through to the client.
            throw new McpException($"read_query failed: {ex.Message}");
        }
    }
}

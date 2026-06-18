using System.Reflection;
using Eu4Indexer.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Eu4Indexer.Mcp;

/// <summary>
/// Hosts the read-only EU4 MCP server over stdio. Extracted from a standalone
/// entry point into a library entry so the merged `eu4indexer` CLI can launch it
/// via its `serve` subcommand (a single binary, no separate MCP executable).
/// </summary>
public static class McpServer
{
    /// <summary>
    /// Resolve the database path (explicit --db wins, else EU4_DB), open it
    /// read-only, validate the schema version, and serve the tools over stdio.
    /// Returns a process exit code.
    /// </summary>
    public static async Task<int> RunAsync(string[] args)
    {
        // Resolve the database path: --db <path> wins, else the EU4_DB environment variable.
        string? dbPath = null;

        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--db")
                dbPath = args[i + 1];

        dbPath ??= Environment.GetEnvironmentVariable("EU4_DB");

        if (string.IsNullOrWhiteSpace(dbPath))
        {
            await Console.Error.WriteLineAsync("error: no database path. Pass --db <path> or set EU4_DB.");
            return 1;
        }

        return await RunWithDatabaseAsync(dbPath, args);
    }

    /// <summary>
    /// Serve against an already-resolved database path. Used by the CLI once it
    /// has resolved the active database from the registry.
    /// </summary>
    public static async Task<int> RunWithDatabaseAsync(string dbPath, string[] args)
    {
        // Open read-only and refuse a schema-version mismatch before serving any
        // tools (the selection opens + validates the initial database).
        DatabaseSelection selection;

        try
        {
            selection = new DatabaseSelection(dbPath);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 2;
        }

        var builder = Host.CreateApplicationBuilder(args);

        // stdout carries the MCP protocol; all logs must go to stderr.
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddSingleton(selection);
        // The existing tools take Eu4Database directly; hand them the active
        // selection each call so switching databases needs no tool changes.
        builder.Services.AddTransient(sp => sp.GetRequiredService<DatabaseSelection>().Current);
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            // Pass this assembly explicitly: the entry assembly is now the F# CLI,
            // so the default (entry-assembly) tool scan would miss the C# tools.
            .WithToolsFromAssembly(Assembly.GetExecutingAssembly());

        await builder.Build().RunAsync();
        return 0;
    }
}

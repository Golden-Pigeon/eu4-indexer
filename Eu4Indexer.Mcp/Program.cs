using Eu4Indexer.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

// Open read-only and refuse a schema-version mismatch before serving any tools.
Eu4Database database;

try
{
    database = new Eu4Database(dbPath);
    database.EnsureSchemaVersion();
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"error: {ex.Message}");
    return 2;
}

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol; all logs must go to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(database);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
return 0;

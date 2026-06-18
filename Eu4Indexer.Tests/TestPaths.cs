namespace Eu4Indexer.Tests;

/// <summary>
/// Resolves optional external resources (real game dir, config repo, example
/// mod). Values come from process environment variables first, then from a
/// <c>.env</c> file at the repository root (copy <c>.env.example</c>). Integration
/// tests depending on them no-op when the value is unset or the path is missing,
/// so the suite stays green on machines without the data.
/// </summary>
public static class TestPaths
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> DotEnv = new(LoadDotEnv);

    public static string? ConfigDir => Resolve("EU4_CONFIG_DIR");

    public static string? GameDir => Resolve("EU4_GAME_DIR");

    public static string? ExampleModDir => Resolve("EU4_EXAMPLE_MOD_DIR");

    /// The synthetic, copyright-safe example game/mod shipped in the repo, used
    /// by the fixture integration tests and CI (no real game files needed).
    public static string? FixtureGameDir => FixtureDir("example-game");

    public static string? FixtureModDir => FixtureDir("example-mod");

    private static string? FixtureDir(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(dir.FullName, "fixtures", name),
                         Path.Combine(dir.FullName, "Eu4Indexer.Tests", "fixtures", name),
                     })
                if (Directory.Exists(candidate))
                    return candidate;
        }

        return null;
    }

    /// PostgreSQL connection string for the export integration test. Unlike the
    /// directory paths, this is a raw value (no filesystem check).
    public static string? PostgresConn => ResolveRaw("EU4_PG_CONN");

    private static string? ResolveRaw(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrEmpty(value))
            DotEnv.Value.TryGetValue(name, out value);

        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? Resolve(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrEmpty(path))
            DotEnv.Value.TryGetValue(name, out path);

        return !string.IsNullOrEmpty(path) && Directory.Exists(path) ? path : null;
    }

    /// Parse the nearest .env file walking up from the test assembly location.
    private static IReadOnlyDictionary<string, string> LoadDotEnv()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var envFile = FindUpwards(".env");

        if (envFile is null)
            return values;

        foreach (var raw in File.ReadAllLines(envFile))
        {
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var eq = line.IndexOf('=');

            if (eq <= 0)
                continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim().Trim('"');

            if (key.Length > 0)
                values[key] = value;
        }

        return values;
    }

    private static string? FindUpwards(string fileName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}

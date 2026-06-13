namespace Eu4Indexer.Tests;

/// <summary>
/// Resolves optional external resources (real game dir, config repo, example
/// mod) from environment variables. Integration tests depending on them no-op
/// when the variable is unset or the path is missing, so the suite stays green
/// on machines without the data. Set EU4_GAME_DIR, EU4_CONFIG_DIR, and
/// EU4_EXAMPLE_MOD_DIR to enable them.
/// </summary>
public static class TestPaths
{
    public static string? ConfigDir => FromEnv("EU4_CONFIG_DIR");

    public static string? GameDir => FromEnv("EU4_GAME_DIR");

    public static string? ExampleModDir => FromEnv("EU4_EXAMPLE_MOD_DIR");

    private static string? FromEnv(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);
        return path is not null && Directory.Exists(path) ? path : null;
    }
}

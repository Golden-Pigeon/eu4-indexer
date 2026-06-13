namespace Eu4Indexer.Tests;

/// <summary>
/// Resolves optional external resources (real game dir, real config repo).
/// Tests depending on them no-op when the resource is absent so the suite
/// stays green on machines without the data.
/// </summary>
public static class TestPaths
{
    public static string? ConfigDir => FirstExisting(
        Environment.GetEnvironmentVariable("EU4_CONFIG_DIR"),
        "/Users/goldenpigeon/git_repos/cwtools-eu4-config");

    public static string? GameDir => FirstExisting(
        Environment.GetEnvironmentVariable("EU4_GAME_DIR"),
        "/Users/goldenpigeon/repos/eu4");

    public static string? ExampleModDir => FirstExisting(
        Environment.GetEnvironmentVariable("EU4_EXAMPLE_MOD_DIR"),
        "/Users/goldenpigeon/repos/example_eu4_mod");

    private static string? FirstExisting(params string?[] candidates) =>
        candidates.FirstOrDefault(p => p is not null && Directory.Exists(p));
}

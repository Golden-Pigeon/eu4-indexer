using System.Text.Json;
using Eu4Indexer.Core;
using Microsoft.Data.Sqlite;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Eu4Indexer.Tests;

public sealed class LauncherOrderTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory().FullName;
    private string DbPath => Path.Combine(root, "launcher-v2.sqlite");
    private string Manifest => Path.Combine(root, "dlc_load.json");
    private readonly GameAdapter adapter;
    private int nextPosition;

    public LauncherOrderTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "mod"));
        var eu4 = GameAdapterModule.eu4;
        adapter = new GameAdapter(eu4.GameId, eu4.ScriptExtensions, eu4.LocalisationFolder,
            eu4.Languages, eu4.SteamAppId, eu4.SteamClientDirs, eu4.SteamGameDir,
            FSharpFunc<Unit, FSharpList<string>>.FromConverter(_ => ListModule.OfSeq(new[] { root })),
            eu4.ValidationSubdirs, eu4.CoreFolders, eu4.LocFileLanguage, eu4.RefKeyRules);
        using var db = new SqliteConnection($"Data Source={DbPath}");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE playsets(id TEXT, name TEXT, isActive INTEGER, isRemoved INTEGER);
            CREATE TABLE mods(id TEXT, displayName TEXT, name TEXT, steamId TEXT, dirPath TEXT, gameRegistryId TEXT);
            CREATE TABLE playsets_mods(playsetId TEXT, modId TEXT, enabled INTEGER, position INTEGER);
            INSERT INTO playsets VALUES('p', 'Test', 1, 0);
            """;
        cmd.ExecuteNonQuery();
    }

    // The workshop, launcher display and registered descriptor names deliberately differ.
    private string Mod(string name, string[]? dependencies = null, bool enabled = true, string? path = null)
    {
        var position = nextPosition++;
        var dir = Directory.CreateDirectory(Path.Combine(root, "mod", position.ToString())).FullName;
        var descriptor = $"mod/{position}.mod";
        File.WriteAllText(Path.Combine(dir, "descriptor.mod"), $"name=\"Workshop copy {position}\"");
        var deps = string.Join(" ", (dependencies ?? []).Select(d => $"\"{d}\""));
        File.WriteAllText(Path.Combine(root, descriptor),
            $"name=\"{name}\"\npath=\"{path ?? $"mod/{position}"}\"\ndependencies={{ {deps} }}");
        using var db = new SqliteConnection($"Data Source={DbPath}");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mods VALUES ($i, 'Launcher display title', NULL, NULL, $dir, $descriptor);
            INSERT INTO playsets_mods VALUES ('p', $i, $enabled, $i);
            """;
        cmd.Parameters.AddWithValue("$i", position);
        cmd.Parameters.AddWithValue("$dir", dir);
        cmd.Parameters.AddWithValue("$descriptor", descriptor);
        cmd.Parameters.AddWithValue("$enabled", enabled);
        cmd.ExecuteNonQuery();
        return descriptor;
    }

    private void Enable(params string[] descriptors) =>
        File.WriteAllText(Manifest, JsonSerializer.Serialize(new { enabled_mods = descriptors }));

    private FSharpResult<FSharpList<Discovery.DiscoveredMod>, string> Resolve(bool playset, bool auto) =>
        Discovery.resolveMods(adapter, Discovery.realProbe, FSharpList<string>.Empty, FSharpList<string>.Empty,
            playset ? FSharpOption<string>.Some("Test") : null, auto);

    private void ExpectOrder(string[] expected, bool playset, bool auto)
    {
        var result = Resolve(playset, auto);
        Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");
        Assert.Equal(expected, result.ResultValue.Select(m => m.Info.Name));
    }

    [Fact]
    public void DependenciesGetTheLongestDependentChainRank_ThenNamesBreakTies()
    {
        const string overhaul = "\u5f52\u589f";
        var core = Mod(overhaul, ["Supplement", "Disabled", "Absent"]);
        var sub = Mod("Submod", [overhaul]);
        var ui = Mod("Zoom", path: "2"); // Also accept a descriptor-relative local path.
        var translation = Mod("Base", []);
        var supplement = Mod("Supplement", ["Base", "Base"]);
        Mod("Disabled", enabled: false);
        Enable(supplement, ui, sub, translation, core);
        string[] expected = ["Base", "Supplement", overhaul, "Zoom", "Submod"];
        ExpectOrder(expected, true, false);
        ExpectOrder(expected, false, true);
        ExpectOrder(expected, true, true);
    }

    [Fact]
    public void IndependentNamesUseUnsignedUtf8BytesIncludingPrefixes()
    {
        string[] input = ["!", "aa", "\uE000", "Z", "a", "\U00010000", "z"];
        var descriptors = input.Select(name => Mod(name)).ToArray();
        string[] expected = ["\U00010000", "\uE000", "z", "aa", "a", "Z", "!"];
        Enable(descriptors);
        ExpectOrder(expected, false, true);
        Enable(descriptors.Reverse().ToArray());
        ExpectOrder(expected, false, true);
    }

    [Theory]
    [InlineData("A", "Z")]
    [InlineData("Z", "A")]
    public void SamePathWinnerSurvivesChangingLauncherPositions(string first, string second)
    {
        if (TestPaths.ConfigDir is not { } config || TestPaths.FixtureGameDir is not { } game) return;
        Mod(first);
        Mod(second);
        var mods = Resolve(true, false).ResultValue;
        foreach (var mod in mods)
        {
            var events = Directory.CreateDirectory(Path.Combine(mod.ContentPath, "events"));
            File.WriteAllText(Path.Combine(events.FullName, "order.txt"), $"# from {mod.Info.Name}");
        }
        var index = Path.Combine(root, "resolved.db");
        var request = new Pipeline.IndexRequest(adapter, game, mods, config, index, true, false,
            FSharpList<string>.Empty, FuncConvert.FromAction<string>(_ => { }),
            FuncConvert.FromAction<Pipeline.IndexProgress>(_ => { }), FSharpList<Tuple<string, string>>.Empty);
        var result = Pipeline.run(request);
        Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");
        using var db = new SqliteConnection($"Data Source={index};Mode=ReadOnly;Pooling=False");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT s.name FROM files f JOIN sources s USING (source_id)
            WHERE f.relative_path='events/order.txt' AND f.is_effective=1
            """;
        using var rows = cmd.ExecuteReader();
        Assert.True(rows.Read());
        Assert.Equal("A", rows.GetString(0));
        Assert.False(rows.Read());
    }

    [Fact]
    public void CombinedSelectionsResolveCrossGroupDependenciesAndSharedDescriptorsOnce()
    {
        var shared = Mod("Independent");
        Mod("Z-Sub", ["A-Base"]);
        var onlyAuto = Mod("A-Base", enabled: false);
        Enable(shared, onlyAuto, shared);
        ExpectOrder(["A-Base", "Z-Sub", "Independent"], true, true);
    }

    [Theory]
    [InlineData("A", "A", "Cyclic")]
    [InlineData("A", "B", "Cyclic")]
    [InlineData("A", "Duplicate", "same mod name")]
    public void UnresolvableOrderReturnsAnError(string name, string dependency, string expected)
    {
        var a = Mod(name, [dependency]);
        var b = dependency == "Duplicate" ? Mod(name) : Mod(dependency == "A" ? "Other" : "B", ["A"]);
        Enable(a, b);
        Assert.Contains(expected, Resolve(true, false).ErrorValue);
        Assert.Contains(expected, Resolve(false, true).ErrorValue);
    }

    [Fact]
    public void StaleRegisteredPathCannotSilentlyUseTheLaunchersCachedDirectory()
    {
        Enable(Mod("Moved", path: "mod/missing"));
        Assert.Contains("Moved", Resolve(true, false).ErrorValue);
        Assert.Contains("Moved", Resolve(false, true).ErrorValue);
    }

    [Fact]
    public void MissingRegisteredDescriptorCannotLoadAResidualWorkshopCopy()
    {
        var descriptor = Mod("Removed");
        Enable(descriptor);
        File.Delete(Path.Combine(root, descriptor));
        Assert.Contains("descriptor not found", Resolve(true, false).ErrorValue);
        Assert.Contains("descriptor not found", Resolve(false, true).ErrorValue);
    }

    [Fact]
    public void WorkshopIdWithoutRegistryPathNeedsItsUserDataDescriptor()
    {
        var descriptor = Mod("Registered");
        using var db = new SqliteConnection($"Data Source={DbPath}");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE mods SET gameRegistryId = NULL";
        cmd.ExecuteNonQuery();
        Assert.Contains("no launcher descriptor", Resolve(true, false).ErrorValue);
        cmd.CommandText = "UPDATE mods SET steamId = '123'";
        cmd.ExecuteNonQuery();
        Assert.Contains("ugc_123.mod", Resolve(true, false).ErrorValue);
        File.Move(Path.Combine(root, descriptor), Path.Combine(root, "mod", "ugc_123.mod"));
        ExpectOrder(["Registered"], true, false);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"enabled_mods":null}""")]
    [InlineData("""{"enabled_mods":[null]}""")]
    [InlineData("""{"enabled_mods":[42]}""")]
    public void MalformedManifestReportsTheManifestPath(string document)
    {
        Mod("Installed");
        File.WriteAllText(Manifest, document);
        Assert.Contains(Manifest, Resolve(false, true).ErrorValue);
    }

    [Fact]
    public void Eu4ReadsTheFirstEnabledListIgnoringCase()
    {
        var descriptor = Mod("Selected");
        File.WriteAllText(Manifest, $$"""{"Enabled_Mods":["{{descriptor}}"],"enabled_mods":[]}""");
        ExpectOrder(["Selected"], false, true);
    }

    [Fact]
    public void NoEnabledListMeansNoActiveEu4Mods()
    {
        Mod("Installed");
        ExpectOrder([], false, true);
        File.WriteAllText(Manifest, """{"disabled_dlcs":[]}""");
        ExpectOrder([], false, true);
        Enable();
        ExpectOrder([], false, true);
    }

    public void Dispose()
    {
        SqliteTestDatabase.Delete(DbPath);
        Directory.Delete(root, true);
    }
}

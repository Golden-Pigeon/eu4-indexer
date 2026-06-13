using Eu4Indexer.Core;

namespace Eu4Indexer.Tests;

public class ConfigCatalogTests
{
    [Fact]
    public void Load_MissingDirectory_ReturnsError()
    {
        var result = ConfigCatalog.load("/nonexistent/path/to/config");
        Assert.True(result.IsError);
    }

    [Fact]
    public void Load_RealEu4Config_ProducesSymbolsTypesAndFolders()
    {
        var configDir = TestPaths.ConfigDir;
        if (configDir is null) return; // config repo not available on this machine

        var result = ConfigCatalog.load(configDir);
        Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");
        var catalog = result.ResultValue;

        // triggers.cwt has ~1351 alias[trigger:*] entries; concrete named ones
        // (excluding <type> refs, enum params, duplicates) are fewer but plentiful
        var triggers = catalog.Symbols.Count(s => s.Kind.IsTriggerSymbol);
        var effects = catalog.Symbols.Count(s => s.Kind.IsEffectSymbol);
        var modifiers = catalog.Symbols.Count(s => s.Kind.IsModifierSymbol);
        Assert.True(triggers > 900, $"expected >900 triggers, got {triggers}");
        Assert.True(effects > 500, $"expected >500 effects, got {effects}");
        Assert.True(modifiers > 400, $"expected >400 modifiers, got {modifiers}");

        // well-known symbols resolve through the lookups
        Assert.True(catalog.TriggerLookup.ContainsKey("is_at_war"));
        Assert.True(catalog.EffectLookup.ContainsKey("add_stability"));
        Assert.True(catalog.ModifierLookup.ContainsKey("discipline"));

        // type definitions cover the core entity types and many generic ones
        var typeNames = catalog.TypeDefs.Select(t => t.TypeName).ToHashSet();
        Assert.Contains("event", typeNames);
        Assert.Contains("mission", typeNames);
        Assert.True(catalog.TypeDefs.Length > 50, $"expected >50 type defs, got {catalog.TypeDefs.Length}");

        // folders.cwt drives indexable-folder enumeration
        Assert.Contains("events", catalog.Folders);
        Assert.Contains("common/ideas", catalog.Folders);
        Assert.True(catalog.Folders.Length > 100, $"expected >100 folders, got {catalog.Folders.Length}");

        // event type def carries path + localisation mapping
        var eventType = catalog.TypeDefs.First(t => t.TypeName == "event");
        Assert.Contains("events", eventType.Paths);
    }
}

using Eu4Indexer.Core;
using Microsoft.FSharp.Core;

namespace Eu4Indexer.Tests;

public class ScriptTreeTests
{
    private const string EventScript = """
        country_event = {
            id = test.1
            title = test.1.t
            trigger = {
                is_at_war = yes
                NOT = { has_country_flag = foo }
            }
            mean_time_to_happen = { months = 12 }
            option = {
                name = test.1.a
                ai_chance = { factor = 10 }
                add_stability = -1
            }
        }
        """;

    private static List<ScriptNodeRow> FlattenEvent()
    {
        var parsed = Parsing.parseText("test.txt", EventScript);
        Assert.True(parsed.IsOk, parsed.IsError ? parsed.ErrorValue.Message : "");
        var eventNode = parsed.ResultValue.Root.Child("country_event").Value;

        var lookups = ScriptTree.makeLookups(
            triggers: [Tuple.Create("is_at_war", 1), Tuple.Create("has_country_flag", 2)],
            effects: [Tuple.Create("add_stability", 10)],
            modifiers: []);

        long id = 0;
        var rows = ScriptTree.flatten(lookups, FuncConvert.FromFunc(() => ++id), 42L, eventNode);
        return rows.ToList();
    }

    [Fact]
    public void Flatten_AssignsContexts()
    {
        var rows = FlattenEvent();

        ScriptNodeRow ByKey(string key) =>
            rows.First(r => OptionModule.ToObj(r.Key) == key);

        Assert.True(ByKey("id").Context.IsMetadataCtx);
        Assert.True(ByKey("trigger").Context.IsTriggerCtx);
        Assert.True(ByKey("is_at_war").Context.IsTriggerCtx);
        Assert.True(ByKey("has_country_flag").Context.IsTriggerCtx); // nested under NOT
        Assert.True(ByKey("months").Context.IsMtthCtx);
        Assert.True(ByKey("option").Context.IsEffectCtx);
        Assert.True(ByKey("name").Context.IsMetadataCtx);            // option name is loc key
        Assert.True(ByKey("factor").Context.IsAiChanceCtx);
        Assert.True(ByKey("add_stability").Context.IsEffectCtx);
    }

    [Fact]
    public void Flatten_TagsSymbolsAndTracksStructure()
    {
        var rows = FlattenEvent();

        ScriptNodeRow ByKey(string key) =>
            rows.First(r => OptionModule.ToObj(r.Key) == key);

        Assert.Equal(1, OptionModule.ToNullable(ByKey("is_at_war").SymbolId));
        Assert.Equal(2, OptionModule.ToNullable(ByKey("has_country_flag").SymbolId));
        Assert.Equal(10, OptionModule.ToNullable(ByKey("add_stability").SymbolId));
        Assert.Null(OptionModule.ToNullable(ByKey("months").SymbolId));

        // depth/parent structure: has_country_flag is NOT's child, NOT is trigger's child
        var trigger = ByKey("trigger");
        var not = ByKey("NOT");
        var flag = ByKey("has_country_flag");
        Assert.Equal(0, trigger.Depth);
        Assert.Equal(1, not.Depth);
        Assert.Equal(2, flag.Depth);
        Assert.Equal(trigger.NodeId, OptionModule.ToNullable(not.ParentId));
        Assert.Equal(not.NodeId, OptionModule.ToNullable(flag.ParentId));

        // every row belongs to the entity
        Assert.All(rows, r => Assert.Equal(42L, r.EntityId));

        // value kinds
        Assert.True(OptionModule.ToObj(ByKey("months").ValueKind)!.IsIntValue);
        Assert.True(OptionModule.ToObj(ByKey("is_at_war").ValueKind)!.IsBoolValue);
        Assert.True(OptionModule.ToObj(ByKey("id").ValueKind)!.IsStringValue);
    }
}

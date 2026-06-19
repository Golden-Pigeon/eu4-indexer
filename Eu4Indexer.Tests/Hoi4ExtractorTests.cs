using Eu4Indexer.Core;
using Eu4Indexer.Core.Extractors;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Eu4Indexer.Tests;

/// Extractor unit tests that parse synthetic HOI4 scripts in memory
/// and verify the resulting entity payloads.
public class Hoi4ExtractorTests
{
    private static ScriptTree.TagLookups MakeLookups(
        (string, int)[]? triggers = null,
        (string, int)[]? effects = null,
        (string, int)[]? modifiers = null)
    {
        var trig = triggers ?? [];
        var eff = effects ?? [];
        var mod = modifiers ?? [];

        var trigSeq = new[] { Tuple.Create("has_country_flag", 1) }.Concat(trig.Select(t => Tuple.Create(t.Item1, t.Item2)));
        var effSeq = new[] { Tuple.Create("add_stability", 2) }.Concat(eff.Select(t => Tuple.Create(t.Item1, t.Item2)));
        var modSeq = new[] { Tuple.Create("political_power_factor", 3) }.Concat(mod.Select(t => Tuple.Create(t.Item1, t.Item2)));

        return ScriptTree.makeLookups(trigSeq, effSeq, modSeq);
    }

    private static (Parsing.ParsedFile, GameFile) Parse(string text, string relPath)
    {
        var parsed = Parsing.parseText("test.txt", text);
        Assert.True(parsed.IsOk, parsed.IsError ? parsed.ErrorValue.Message : "");

        var file = new GameFile(
            fileId: 1, sourceId: 1, absolutePath: "/fake/test.txt",
            relativePath: relPath, folder: "common/national_focus",
            fileName: "test.txt", contentHash: "abc", byteSize: 100L,
            isEffective: true, parseStatus: ParseStatus.ParsedOk);

        return (parsed.ResultValue, file);
    }

    [Fact]
    public void FocusTrees_Extract_ParsesFocusTreeAndFoci()
    {
        const string script = """
            focus_tree = {
                id = test_tree
                focus = {
                    id = test_focus_1
                    icon = GFX_test
                    x = 0  y = 0
                    completion_reward = { add_stability = 1 }
                }
                focus = {
                    id = test_focus_2
                    icon = GFX_test
                    x = 1  y = 0
                    prerequisite = { focus = test_focus_1 }
                    completion_reward = { set_country_flag = test_flag }
                }
            }
            """;

        var (parsed, file) = Parse(script, "common/national_focus/test.txt");
        var lookups = MakeLookups();
        var idGen = Support.makeIdGen();

        var payloads = FocusTrees.extract(lookups, idGen, file, parsed);

        Assert.Equal(3, payloads.Length); // tree + 2 foci
        Assert.Contains(payloads, p => p.Entity.EntityType == "focus_tree");
        Assert.Contains(payloads, p => p.Entity.EntityType == "focus");
        Assert.Equal(2, payloads.Count(p => p.Entity.EntityType == "focus"));
    }

    [Fact]
    public void FocusTrees_Extract_CapturesPrerequisites()
    {
        const string script = """
            focus_tree = {
                id = test_tree
                focus = {
                    id = test_focus_1
                    icon = GFX_test
                    x = 0  y = 0
                    completion_reward = { add_stability = 1 }
                }
                focus = {
                    id = test_focus_2
                    icon = GFX_test
                    x = 1  y = 0
                    prerequisite = { focus = test_focus_1 }
                    completion_reward = {}
                }
            }
            """;

        var (parsed, file) = Parse(script, "common/national_focus/test.txt");
        var lookups = MakeLookups();
        var idGen = Support.makeIdGen();

        var payloads = FocusTrees.extract(lookups, idGen, file, parsed);

        var focus2 = payloads.Single(p => p.Entity.EntityKey == "test_focus_2");
        Assert.True(focus2.GameDetails is GameSpecificDetails.Hoi4Game);
        var fd = ((GameSpecificDetails.Hoi4Game)focus2.GameDetails).Item;
        Assert.Single(fd.FocusReqs);
        Assert.Equal("test_focus_1", fd.FocusReqs[0].RequiredFocus);
    }

    [Fact]
    public void FocusTrees_Extract_AssignsSeriesKeyToTreeId()
    {
        const string script = """
            focus_tree = {
                id = test_tree
                focus = {
                    id = test_focus
                    icon = GFX_test
                    x = 0  y = 0
                    completion_reward = {}
                }
            }
            """;

        var (parsed, file) = Parse(script, "common/national_focus/test.txt");
        var lookups = MakeLookups();
        var idGen = Support.makeIdGen();

        var payloads = FocusTrees.extract(lookups, idGen, file, parsed);

        var focus = payloads.Single(p => p.Entity.EntityType == "focus");
        Assert.True(focus.GameDetails is GameSpecificDetails.Hoi4Game);
        var fd2 = ((GameSpecificDetails.Hoi4Game)focus.GameDetails).Item;
        Assert.NotNull(fd2.Focus);
        Assert.Equal("test_tree", fd2.Focus.Value.TreeId);
    }

    [Fact]
    public void Ideas_Extract_ParsesCategoryAndModifiers()
    {
        const string script = """
            ideas = {
                political_advisor = {
                    test_advisor = {
                        allowed = { always = yes }
                        modifier = {
                            political_power_factor = 0.15
                            stability_factor = 0.10
                        }
                    }
                }
            }
            """;

        var (parsed, file) = Parse(script, "common/ideas/test.txt");
        var lookups = MakeLookups();
        var idGen = Support.makeIdGen();

        var payloads = Ideas.extract(lookups, idGen, file, parsed);

        Assert.Single(payloads);
        var idea = payloads[0];
        Assert.Equal("idea", idea.Entity.EntityType);
        Assert.Equal("test_advisor", idea.Entity.EntityKey);
        Assert.Equal(2, idea.ModifierValues.Length);
        Assert.Contains(idea.ModifierValues, mv => mv.ModifierKey == "political_power_factor");
        Assert.Contains(idea.ModifierValues, mv => mv.ModifierKey == "stability_factor");
    }

    [Fact]
    public void Ideas_Extract_SkipsMultipleCategories()
    {
        const string script = """
            ideas = {
                political_advisor = {
                    advisor_a = {
                        modifier = { political_power_factor = 0.1 }
                    }
                }
                tank_designer = {
                    designer_a = {
                        modifier = { armor_value = 0.05 }
                    }
                }
            }
            """;

        var (parsed, file) = Parse(script, "common/ideas/test.txt");
        var lookups = MakeLookups();
        var idGen = Support.makeIdGen();

        var payloads = Ideas.extract(lookups, idGen, file, parsed);

        Assert.Equal(2, payloads.Length);
        Assert.Contains(payloads, p => p.Entity.EntityKey == "advisor_a");
        Assert.Contains(payloads, p => p.Entity.EntityKey == "designer_a");
    }

    [Fact]
    public void Ideas_Extract_IdeaWithoutModifier_ReturnsEmptyModifiers()
    {
        const string script = """
            ideas = {
                category = {
                    empty_idea = {
                        allowed = { always = yes }
                    }
                }
            }
            """;

        var (parsed, file) = Parse(script, "common/ideas/test.txt");
        var lookups = MakeLookups();
        var idGen = Support.makeIdGen();

        var payloads = Ideas.extract(lookups, idGen, file, parsed);

        Assert.Single(payloads);
        Assert.Empty(payloads[0].ModifierValues);
    }
}

using Eu4Indexer.Core;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Eu4Indexer.Tests;

/// <summary>
/// Pure-function tests for the reference extractor's identity-check edges
/// (has_idea / has_great_project / has_completed_focus). No DB or game data —
/// runs anywhere, including plain CI.
/// </summary>
public class ReferenceExtractorTests
{
    private static readonly FSharpSet<string> NoStrings = new(Array.Empty<string>());
    private static readonly FSharpSet<long> NoIds = new(Array.Empty<long>());

    private static RefNode Leaf(long id, string key, string value, string context = "trigger", long? parent = null) =>
        new(id,
            parent is null ? FSharpOption<long>.None : FSharpOption<long>.Some(parent.Value),
            0, "leaf", context,
            FSharpOption<string>.Some(key), FSharpOption<string>.Some(value));

    private static RefNode Clause(long id, string key, string context = "trigger", long? parent = null) =>
        new(id,
            parent is null ? FSharpOption<long>.None : FSharpOption<long>.Some(parent.Value),
            0, "clause", context,
            FSharpOption<string>.Some(key), FSharpOption<string>.None);

    private static List<ReferenceRow> Extract(GameAdapter adapter, params RefNode[] nodes) =>
        ReferenceExtractor.fromEntity(
            adapter.RefKeyRules, NoStrings, NoStrings, NoIds,
            entityId: 1L, entityType: "decision",
            ListModule.OfSeq(nodes)).ToList();

    [Fact]
    public void Eu4_HasIdea_Leaf_EmitsChecksIdeaEdge()
    {
        var rows = Extract(GameAdapterModule.eu4, Leaf(1, "has_idea", "MFA_byzantine_claimants"));

        Assert.Contains(rows, r =>
            r.RefKind == "checks_idea" && r.TargetType == "idea"
            && r.TargetKey == "MFA_byzantine_claimants");
    }

    [Fact]
    public void Eu4_HasGreatProject_Clause_ReadsTypeChild()
    {
        // has_great_project = { type = X } — the target lives in the `type` child.
        var rows = Extract(GameAdapterModule.eu4,
            Clause(1, "has_great_project"),
            Leaf(2, "type", "kaaba", parent: 1));

        Assert.Contains(rows, r =>
            r.RefKind == "checks_great_project" && r.TargetType == "great_project"
            && r.TargetKey == "kaaba");
    }

    [Fact]
    public void Hoi4_HasCompletedFocus_EmitsChecksFocusEdge()
    {
        var rows = Extract(GameAdapterModule.hoi4, Leaf(1, "has_completed_focus", "fixture_focus_1"));

        Assert.Contains(rows, r =>
            r.RefKind == "checks_focus" && r.TargetType == "focus"
            && r.TargetKey == "fixture_focus_1");
    }
}

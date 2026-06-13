using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Eu4Indexer.Mcp;

/// <summary>Effect-level analysis that connects UI tooltips to hidden script consequences.</summary>
[McpServerToolType]
public static class ConsequenceTools
{
    private const int MaxBlocks = 25;
    private const int MaxFiredEvents = 8;
    private const int MaxConsequencesPerState = 8;
    private const int MaxEffectSummary = 20;

    private static readonly HashSet<string> BlockKeys = new(StringComparer.Ordinal)
    {
        "effect", "immediate", "option", "hidden_effect", "limit",
    };

    private static readonly HashSet<string> ContainerKeys = new(StringComparer.Ordinal)
    {
        "effect", "immediate", "option", "hidden_effect", "if", "else", "else_if", "limit",
    };

    private static readonly HashSet<string> EventEffectKeys = new(StringComparer.Ordinal)
    {
        "country_event", "province_event", "ruler_event", "trade_node_event", "siege_event",
    };

    private static readonly Dictionary<string, string> FlagEffectTargets = new(StringComparer.Ordinal)
    {
        ["set_country_flag"] = "country_flag",
        ["clr_country_flag"] = "country_flag",
        ["set_global_flag"] = "global_flag",
        ["clr_global_flag"] = "global_flag",
        ["set_province_flag"] = "province_flag",
        ["clr_province_flag"] = "province_flag",
        ["set_ruler_flag"] = "ruler_flag",
        ["clr_ruler_flag"] = "ruler_flag",
    };

    private static readonly HashSet<string> VariableEffectKeys = new(StringComparer.Ordinal)
    {
        "set_variable", "change_variable", "add_to_variable", "subtract_variable",
        "multiply_variable", "divide_variable", "export_to_variable",
    };

    [McpServerTool(Name = "analyze_effects")]
    [Description(
        "Analyze an event, decision, mission or similar entity at effect-block level. " +
        "Use this when explaining what content does: it proactively connects custom_tooltip " +
        "UI text to sibling hidden effects, fired events, state changes, and downstream " +
        "entities gated by changed variables or flags.")]
    public static EffectAnalysisResult? AnalyzeEffects(
        Eu4Database db,
        [Description("Entity type, e.g. event, decision, mission.")] string entityType,
        [Description("Entity key/id.")] string entityKey,
        [Description("Maximum fired-event expansion depth (default 2, max 4). Keep low for concise answers.")] int maxDepth = 2)
    {
        maxDepth = Math.Clamp(maxDepth, 0, 4);
        var resolved = db.ResolveEntity(entityType, entityKey);

        if (resolved is null)
            return null;

        var analyzer = new Analyzer(db);
        var blocks = analyzer.AnalyzeEntity(resolved.Value.Id, expandFiredEvents: true, depthRemaining: maxDepth);

        return new EffectAnalysisResult(
            resolved.Value.Ref,
            blocks,
            new List<string>
            {
                "Tooltip text is explanatory UI text; analyze_effects reports effects associated with the same effect block and bounded fired-event expansion, not a full game simulation.",
                "EU4 numeric checks such as check_variable value = 10 generally mean the variable is at least 10.",
            });
    }

    private sealed class Analyzer(Eu4Database db)
    {
        private readonly HashSet<string> _expandedEvents = new(StringComparer.Ordinal);

        public List<EffectBlockAnalysis> AnalyzeEntity(long entityId, bool expandFiredEvents, int depthRemaining)
        {
            var nodes = LoadNodes(entityId);
            var optionLookup = LoadOptions(entityId);
            var blocks = SelectBlocks(nodes).Take(MaxBlocks).ToList();

            return blocks
                .Select(block => AnalyzeBlock(entityId, block, nodes, optionLookup, expandFiredEvents, depthRemaining))
                .Where(block => block.Tooltips.Count > 0 || block.DirectEffects.Count > 0 ||
                                block.StateChanges.Count > 0 || block.FiredEvents.Count > 0 ||
                                block.DownstreamConsequences.Count > 0)
                .ToList();
        }

        private EffectBlockAnalysis AnalyzeBlock(
            long sourceEntityId,
            ScriptNodeRow block,
            List<ScriptNodeRow> nodes,
            Dictionary<long, OptionRow> optionLookup,
            bool expandFiredEvents,
            int depthRemaining)
        {
            var descendants = Descendants(block, nodes).ToList();
            var tooltips = descendants
                .Where(n => n.Key == "custom_tooltip" && !string.IsNullOrWhiteSpace(n.Value))
                .Select(n => BuildTooltip(n.Value!))
                .ToList();
            var scriptedBlocks = ExpandScriptedEffectBlocks(descendants, depthRemaining);
            var directEffects = SummarizeEffects(descendants)
                .Concat(scriptedBlocks.SelectMany(b => b.DirectEffects))
                .Take(MaxEffectSummary)
                .ToList();
            var stateChanges = ExtractStateChanges(descendants)
                .Concat(scriptedBlocks.SelectMany(b => b.StateChanges))
                .ToList();
            var downstream = stateChanges
                .SelectMany(state => FindDownstreamConsequences(state, sourceEntityId))
                .DistinctBy(c => $"{c.StateKind}:{c.StateKey}:{c.Consumer.EntityType}:{c.Consumer.EntityKey}:{c.ConditionSummary}")
                .ToList();
            var firedEvents = expandFiredEvents && depthRemaining > 0
                ? ExtractFiredEventKeys(descendants)
                    .Take(MaxFiredEvents)
                    .SelectMany(key => AnalyzeFiredEvent(key, depthRemaining - 1))
                    .Concat(scriptedBlocks.SelectMany(b => b.FiredEvents))
                    .ToList()
                : new List<FiredEventAnalysis>();

            optionLookup.TryGetValue(block.NodeId, out var option);

            return new EffectBlockAnalysis(
                BlockKind(block),
                option?.Index,
                option?.NameKey,
                option?.NameText,
                tooltips.Concat(scriptedBlocks.SelectMany(b => b.Tooltips)).DistinctBy(t => t.LocKey).ToList(),
                directEffects,
                stateChanges,
                firedEvents,
                downstream.Concat(scriptedBlocks.SelectMany(b => b.DownstreamConsequences))
                    .DistinctBy(c => $"{c.StateKind}:{c.StateKey}:{c.Consumer.EntityType}:{c.Consumer.EntityKey}:{c.ConditionSummary}")
                    .ToList());
        }

        private List<EffectBlockAnalysis> ExpandScriptedEffectBlocks(List<ScriptNodeRow> nodes, int depthRemaining)
        {
            return nodes
                .Where(n => n.Context == "effect" && n.Key is not null && !ContainerKeys.Contains(n.Key))
                .Select(n => n.Key!)
                .Distinct(StringComparer.Ordinal)
                .Select(key => db.ResolveEntity("scripted_effect", key))
                .Where(resolved => resolved is not null)
                .SelectMany(resolved => AnalyzeEntity(resolved!.Value.Id, expandFiredEvents: depthRemaining > 0, depthRemaining))
                .ToList();
        }

        private List<FiredEventAnalysis> AnalyzeFiredEvent(string eventKey, int depthRemaining)
        {
            var resolved = db.ResolveEntity("event", eventKey);

            if (resolved is null)
                return new List<FiredEventAnalysis>();

            var token = $"event:{eventKey}";
            if (!_expandedEvents.Add(token))
                return new List<FiredEventAnalysis>();

            var blocks = AnalyzeEntity(resolved.Value.Id, expandFiredEvents: depthRemaining > 0, depthRemaining);
            _expandedEvents.Remove(token);

            var tooltips = blocks.SelectMany(b => b.Tooltips).DistinctBy(t => t.LocKey).ToList();
            var directEffects = blocks.SelectMany(b => b.DirectEffects).Take(MaxEffectSummary).ToList();
            var stateChanges = blocks.SelectMany(b => b.StateChanges)
                .DistinctBy(s => $"{s.Kind}:{s.Key}:{s.Operation}:{s.Value}")
                .ToList();
            var downstream = blocks.SelectMany(b => b.DownstreamConsequences)
                .DistinctBy(c => $"{c.StateKind}:{c.StateKey}:{c.Consumer.EntityType}:{c.Consumer.EntityKey}")
                .ToList();

            return new List<FiredEventAnalysis>
            {
                new(resolved.Value.Ref, blocks, tooltips, directEffects, stateChanges, downstream),
            };
        }

        private List<ScriptNodeRow> LoadNodes(long entityId) => db.Query(
            """
            SELECT node_id, parent_id, depth, sort_order, context, key, value, line
            FROM script_nodes
            WHERE entity_id = $id
            ORDER BY depth, sort_order
            """,
            r => new ScriptNodeRow(
                r.GetInt64(0),
                r.IsDBNull(1) ? null : r.GetInt64(1),
                r.GetInt32(2),
                r.GetInt32(3),
                r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.GetInt32(7)),
            new Dictionary<string, object?> { ["$id"] = entityId });

        private Dictionary<long, OptionRow> LoadOptions(long entityId) => db.Query(
            """
            SELECT eo.option_idx, eo.name_key, eo.node_id,
                   (SELECT l.value FROM localisation l
                    WHERE l.loc_key = eo.name_key AND l.language = 'english' AND l.is_effective = 1
                    LIMIT 1)
            FROM event_options eo
            WHERE eo.entity_id = $id
            """,
            r => new OptionRow(
                r.GetInt32(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetInt64(2)),
            new Dictionary<string, object?> { ["$id"] = entityId })
            .ToDictionary(o => o.NodeId);

        private static IEnumerable<ScriptNodeRow> SelectBlocks(List<ScriptNodeRow> nodes)
        {
            var childrenByParent = nodes
                .Where(n => n.ParentId is not null)
                .GroupBy(n => n.ParentId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var node in nodes)
            {
                if (node.Context != "effect" || node.Key is null || !BlockKeys.Contains(node.Key))
                    continue;

                if (node.Key == "hidden_effect")
                    continue;

                var descendants = Descendants(node, nodes).ToList();
                var hasNestedBlock = childrenByParent.TryGetValue(node.NodeId, out var children) &&
                                     children.Any(child => child.Key is "option" or "effect" or "immediate");

                if (!hasNestedBlock && descendants.Any(n => n.Context == "effect"))
                    yield return node;
            }
        }

        private static IEnumerable<ScriptNodeRow> Descendants(ScriptNodeRow root, List<ScriptNodeRow> nodes)
        {
            var byParent = nodes
                .Where(n => n.ParentId is not null)
                .GroupBy(n => n.ParentId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderBy(n => n.SortOrder).ToList());
            var stack = new Stack<ScriptNodeRow>();

            if (byParent.TryGetValue(root.NodeId, out var children))
                foreach (var child in children.AsEnumerable().Reverse())
                    stack.Push(child);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;

                if (byParent.TryGetValue(node.NodeId, out var nested))
                    foreach (var child in nested.AsEnumerable().Reverse())
                        stack.Push(child);
            }
        }

        private TooltipInsight BuildTooltip(string locKey)
        {
            var text = db.Query(
                """
                SELECT value FROM localisation
                WHERE loc_key = $k AND language = 'english' AND is_effective = 1
                LIMIT 1
                """,
                r => r.GetString(0),
                new Dictionary<string, object?> { ["$k"] = locKey },
                1).FirstOrDefault();

            return new TooltipInsight(
                locKey,
                text,
                "narrative_or_vague_consequence",
                "custom_tooltip displays UI prose; inspect sibling hidden effects, fired events, and downstream state consumers for gameplay meaning.");
        }

        private static List<EffectStatement> SummarizeEffects(List<ScriptNodeRow> nodes)
        {
            var byId = nodes.ToDictionary(n => n.NodeId);

            return nodes
                .Where(n => n.Context == "effect" && n.Key is not null && !ContainerKeys.Contains(n.Key))
                .Where(n => !IsParameterNode(n, byId))
                .Select(n => new EffectStatement(n.Key!, EffectValue(n, nodes), n.Context))
                .Take(MaxEffectSummary)
                .ToList();
        }

        private static bool IsParameterNode(ScriptNodeRow node, Dictionary<long, ScriptNodeRow> byId) =>
            node.ParentId is long parentId &&
            byId.TryGetValue(parentId, out var parent) &&
            parent.Context == "effect" &&
            parent.Key is not null &&
            (EventEffectKeys.Contains(parent.Key) || VariableEffectKeys.Contains(parent.Key) ||
             FlagEffectTargets.ContainsKey(parent.Key) || parent.Key.Contains("modifier", StringComparison.Ordinal));

        private static string? EffectValue(ScriptNodeRow node, List<ScriptNodeRow> nodes)
        {
            if (EventEffectKeys.Contains(node.Key ?? ""))
                return FirstChildValue(node, nodes, "id") ?? node.Value;

            if ((node.Key ?? "").Contains("modifier", StringComparison.Ordinal))
                return FirstChildValue(node, nodes, "name") ?? node.Value;

            if (VariableEffectKeys.Contains(node.Key ?? ""))
                return FirstChildValue(node, nodes, "which") ?? node.Value;

            if (FlagEffectTargets.ContainsKey(node.Key ?? ""))
                return FirstChildValue(node, nodes, "flag") ?? node.Value;

            return node.Value;
        }

        private static List<string> ExtractFiredEventKeys(List<ScriptNodeRow> nodes) => nodes
            .Where(n => n.Context == "effect" && n.Key is not null && EventEffectKeys.Contains(n.Key))
            .Select(n => FirstChildValue(n, nodes, "id") ?? n.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        private static List<StateChange> ExtractStateChanges(List<ScriptNodeRow> nodes) => nodes
            .Where(n => n.Context == "effect" && n.Key is not null)
            .Select(n =>
            {
                if (VariableEffectKeys.Contains(n.Key!))
                    return BuildVariableChange(n, nodes);

                if (FlagEffectTargets.TryGetValue(n.Key!, out var flagTarget))
                    return new StateChange(flagTarget, FirstChildValue(n, nodes, "flag") ?? n.Value ?? "", FlagOperation(n.Key!), null, n.Context);

                return n.Key switch
                {
                    "add_country_modifier" or "add_province_modifier" or "add_ruler_modifier" or
                    "add_permanent_province_modifier" or "add_province_triggered_modifier" or
                    "add_country_triggered_modifier" =>
                        new StateChange("modifier", FirstChildValue(n, nodes, "name") ?? n.Value ?? "", n.Key, FirstChildValue(n, nodes, "duration"), n.Context),
                    _ => null,
                };
            })
            .Where(change => change is not null && !string.IsNullOrWhiteSpace(change.Key))
            .Select(change => change!)
            .ToList();

        private static StateChange? BuildVariableChange(ScriptNodeRow node, List<ScriptNodeRow> nodes)
        {
            var key = FirstChildValue(node, nodes, "which");
            var value = FirstChildValue(node, nodes, "value");

            return string.IsNullOrWhiteSpace(key)
                ? null
                : new StateChange("variable", key!, node.Key!, value, node.Context);
        }

        private static string FlagOperation(string key) => key.StartsWith("clr_", StringComparison.Ordinal) ? "clear" : "set";

        private List<DownstreamConsequence> FindDownstreamConsequences(StateChange state, long sourceEntityId)
        {
            var (refKind, targetType) = state.Kind switch
            {
                "variable" => ("checks_variable", "variable"),
                "country_flag" => ("checks_flag", "country_flag"),
                "global_flag" => ("checks_flag", "global_flag"),
                "province_flag" => ("checks_flag", "province_flag"),
                "ruler_flag" => ("checks_flag", "ruler_flag"),
                _ => (null, null),
            };

            if (refKind is null || targetType is null)
                return new List<DownstreamConsequence>();

            return db.Query(
                """
                SELECT ce.entity_id, ce.entity_type, ce.entity_key, ce.is_effective,
                       s.name, f.relative_path, r.node_id, r.option_node_id, r.negated
                FROM refs r
                JOIN entities ce ON ce.entity_id = r.from_entity_id
                JOIN sources s ON s.source_id = ce.source_id
                JOIN files f ON f.file_id = ce.file_id
                WHERE r.ref_kind = $rk
                  AND r.target_type = $tt
                  AND r.target_key = $key
                  AND ce.is_effective = 1
                  AND ce.entity_id <> $source
                ORDER BY ce.entity_type, ce.entity_key, r.option_node_id IS NULL, r.option_node_id, r.node_id
                LIMIT $lim
                """,
                r => BuildConsequence(
                    state,
                    r.GetInt64(0),
                    new EntityRef(r.GetString(1), r.GetString(2), r.GetString(4), r.GetInt64(3) != 0, r.GetString(5)),
                    r.GetInt64(6),
                    r.IsDBNull(7) ? null : r.GetInt64(7),
                    r.GetInt64(8) != 0),
                new Dictionary<string, object?>
                {
                    ["$rk"] = refKind,
                    ["$tt"] = targetType,
                    ["$key"] = state.Key,
                    ["$source"] = sourceEntityId,
                    ["$lim"] = MaxConsequencesPerState,
                },
                MaxConsequencesPerState);
        }

        private DownstreamConsequence BuildConsequence(
            StateChange state,
            long consumerEntityId,
            EntityRef consumer,
            long conditionNodeId,
            long? optionNodeId,
            bool negated)
        {
            var nodes = LoadNodes(consumerEntityId);
            var conditionNode = nodes.FirstOrDefault(n => n.NodeId == conditionNodeId);
            var rawConditionSummary = conditionNode is null
                ? $"checks {state.Kind}:{state.Key}"
                : SummarizeCondition(conditionNode, nodes);
            var conditionSummary = negated ? $"NOT ({rawConditionSummary})" : rawConditionSummary;
            var effectSummary = EffectSummaryForCondition(nodes, conditionNode, optionNodeId);

            return new DownstreamConsequence(state.Kind, state.Key, consumer, conditionSummary, effectSummary);
        }

        private static List<EffectStatement> EffectSummaryForCondition(
            List<ScriptNodeRow> nodes,
            ScriptNodeRow? conditionNode,
            long? optionNodeId)
        {
            var scopedBlock = optionNodeId is long oid
                ? nodes.FirstOrDefault(n => n.NodeId == oid)
                : conditionNode is null
                    ? null
                    : FindEnclosingBlock(conditionNode, nodes);

            if (scopedBlock is not null)
                return SummarizeEffects(Descendants(scopedBlock, nodes).ToList())
                    .Take(MaxEffectSummary)
                    .ToList();

            return SelectBlocks(nodes)
                .SelectMany(block => SummarizeEffects(Descendants(block, nodes).ToList()))
                .Take(MaxEffectSummary)
                .ToList();
        }

        private static ScriptNodeRow? FindEnclosingBlock(ScriptNodeRow node, List<ScriptNodeRow> nodes)
        {
            var byId = nodes.ToDictionary(n => n.NodeId);
            var current = node;

            while (current.ParentId is long parentId && byId.TryGetValue(parentId, out var parent))
            {
                if (parent.Context == "effect" && parent.Key is "option" or "effect" or "immediate")
                    return parent;

                current = parent;
            }

            return null;
        }

        private static string SummarizeCondition(ScriptNodeRow node, List<ScriptNodeRow> nodes)
        {
            var which = FirstChildValue(node, nodes, "which");
            var value = FirstChildValue(node, nodes, "value");

            if (which is not null && value is not null)
                return $"{node.Key} {which} value {value}";

            if (node.Value is not null)
                return $"{node.Key} {node.Value}";

            return node.Key ?? "condition";
        }

        private static string? FirstChildValue(ScriptNodeRow node, List<ScriptNodeRow> nodes, string key) => nodes
            .Where(child => child.ParentId == node.NodeId && child.Key == key)
            .OrderBy(child => child.SortOrder)
            .Select(child => child.Value)
            .FirstOrDefault(value => value is not null);

        private static string BlockKind(ScriptNodeRow block) => block.Key switch
        {
            "option" => "option_effect",
            "immediate" => "immediate_effect",
            "effect" => "entity_effect",
            _ => block.Key ?? "effect",
        };
    }

    private sealed record ScriptNodeRow(
        long NodeId,
        long? ParentId,
        int Depth,
        int SortOrder,
        string Context,
        string? Key,
        string? Value,
        int Line);

    private sealed record OptionRow(int Index, string? NameKey, string? NameText, long NodeId);
}

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Eu4Indexer.Mcp;

/// <summary>
/// Higher-order reasoning over the reference graph: backward-chaining toward a
/// goal, and finding references that nothing ever satisfies.
/// </summary>
[McpServerToolType]
public static class PlanningTools
{
    private const string TraceNote =
        "Chains only cover state the player can reach via script: flags, variables, " +
        "events, decisions and missions. Non-symbolic prerequisites on each step " +
        "(country tag, date, province ownership, being at peace, ...) are NOT modelled " +
        "— call explain_entity on a step's entity to read its real conditions before " +
        "treating a chain as achievable.";

    [McpServerTool(Name = "trace_to_goal")]
    [Description(
        "Work backwards from a goal (reach an event, get a flag set, or set a variable) " +
        "and return candidate action chains: e.g. complete mission A -> it fires event B " +
        "-> B sets flag C. Each chain is ordered from a base action (a decision to click, " +
        "a mission to complete, or a self-firing event) down to the goal. This is a " +
        "bounded symbolic search, not a full planner: see the returned note.")]
    public static TraceResult TraceToGoal(
        Eu4Database db,
        [Description("What to reach: 'event', 'flag', or 'variable'.")] string targetKind,
        [Description("The event id, flag name, or variable name to reach.")] string targetKey,
        [Description("Flag scope when targetKind is 'flag': country_flag|global_flag|province_flag|ruler_flag (default country_flag).")] string? flagScope = null,
        [Description("Maximum backward-chaining depth (default 4, max 6).")] int maxDepth = 4)
    {
        maxDepth = Math.Clamp(maxDepth, 1, 6);

        var stateType = targetKind switch
        {
            "event" => "event",
            "variable" => "variable",
            "flag" => flagScope ?? "country_flag",
            _ => null,
        };

        if (stateType is null)
            return new TraceResult($"{targetKind}:{targetKey}", false, new List<TracePath>(), false,
                "Unknown targetKind. Use 'event', 'flag', or 'variable'.");

        var tracer = new Tracer(db, maxDepth);
        tracer.Trace(stateType, targetKey);

        return new TraceResult(
            $"{stateType}:{targetKey}",
            tracer.Paths.Count > 0,
            tracer.Paths,
            tracer.Truncated,
            TraceNote);
    }

    [McpServerTool(Name = "find_dangling")]
    [Description(
        "Find references whose target is never produced/defined anywhere in script: " +
        "flags checked but never set, events fired but not defined, scripted calls with " +
        "no definition, variables checked but never set. These are candidate bugs or " +
        "unreachable conditions. Heuristic: engine-set flags, hardcoded events and " +
        "dynamically-named targets can show up as false positives — verify before " +
        "concluding it is a bug. kind: flag|event|scripted|variable|all.")]
    public static List<DanglingRef> FindDangling(
        Eu4Database db,
        [Description("Which dangling kind to report: flag|event|scripted|variable|all (default all).")] string kind = "all",
        [Description("Maximum results (default 50, max 300).")] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 300);
        var results = new List<DanglingRef>();

        if (kind is "flag" or "all")
            results.AddRange(db.Query(
                """
                SELECT 'flag', c.target_type, c.target_key, count(*),
                       (SELECT fe.entity_type || ':' || fe.entity_key
                        FROM refs r2 JOIN entities fe ON fe.entity_id = r2.from_entity_id
                        WHERE r2.ref_kind = 'checks_flag' AND r2.target_type = c.target_type
                          AND r2.target_key = c.target_key AND fe.is_effective = 1 LIMIT 1)
                FROM refs c
                WHERE c.ref_kind = 'checks_flag'
                  AND NOT EXISTS (SELECT 1 FROM refs s WHERE s.ref_kind = 'sets_flag'
                                  AND s.target_type = c.target_type AND s.target_key = c.target_key)
                GROUP BY c.target_type, c.target_key
                ORDER BY count(*) DESC LIMIT $lim
                """,
                MapDangling, new Dictionary<string, object?> { ["$lim"] = limit }, limit));

        if (kind is "event" or "all")
            results.AddRange(db.Query(
                """
                SELECT 'event', 'event', c.target_key, count(*),
                       (SELECT fe.entity_type || ':' || fe.entity_key
                        FROM refs r2 JOIN entities fe ON fe.entity_id = r2.from_entity_id
                        WHERE r2.target_key = c.target_key AND fe.is_effective = 1 LIMIT 1)
                FROM refs c
                WHERE c.ref_kind IN ('fires_event', 'on_action_fires')
                  AND NOT EXISTS (SELECT 1 FROM entities e WHERE e.entity_type = 'event' AND e.entity_key = c.target_key)
                GROUP BY c.target_key
                ORDER BY count(*) DESC LIMIT $lim
                """,
                MapDangling, new Dictionary<string, object?> { ["$lim"] = limit }, limit));

        if (kind is "scripted" or "all")
            results.AddRange(db.Query(
                """
                SELECT 'scripted', c.target_type, c.target_key, count(*),
                       (SELECT fe.entity_type || ':' || fe.entity_key
                        FROM refs r2 JOIN entities fe ON fe.entity_id = r2.from_entity_id
                        WHERE r2.target_key = c.target_key AND fe.is_effective = 1 LIMIT 1)
                FROM refs c
                WHERE c.ref_kind IN ('calls_scripted_trigger', 'calls_scripted_effect')
                  AND NOT EXISTS (SELECT 1 FROM entities e
                                  WHERE e.entity_type IN ('scripted_trigger', 'scripted_effect', 'scripted_function')
                                    AND e.entity_key = c.target_key)
                GROUP BY c.target_type, c.target_key
                ORDER BY count(*) DESC LIMIT $lim
                """,
                MapDangling, new Dictionary<string, object?> { ["$lim"] = limit }, limit));

        if (kind is "variable" or "all")
            results.AddRange(db.Query(
                """
                SELECT 'variable', 'variable', c.target_key, count(*),
                       (SELECT fe.entity_type || ':' || fe.entity_key
                        FROM refs r2 JOIN entities fe ON fe.entity_id = r2.from_entity_id
                        WHERE r2.ref_kind = 'checks_variable' AND r2.target_key = c.target_key
                          AND fe.is_effective = 1 LIMIT 1)
                FROM refs c
                WHERE c.ref_kind = 'checks_variable'
                  AND NOT EXISTS (SELECT 1 FROM refs s WHERE s.ref_kind = 'sets_variable' AND s.target_key = c.target_key)
                GROUP BY c.target_key
                ORDER BY count(*) DESC LIMIT $lim
                """,
                MapDangling, new Dictionary<string, object?> { ["$lim"] = limit }, limit));

        return results;
    }

    private static DanglingRef MapDangling(Microsoft.Data.Sqlite.SqliteDataReader r) =>
        new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.IsDBNull(4) ? null : r.GetString(4));

    /// <summary>
    /// Bounded backward-chaining search over the reference graph. Builds chains
    /// from the goal back to a base action (decision/mission/self-firing event).
    /// </summary>
    private sealed class Tracer(Eu4Database db, int maxDepth)
    {
        private const int MaxPaths = 25;
        private const int ProducerCap = 8;

        private readonly Dictionary<string, bool> _selfFiring = new();
        private int _budget = 2000;

        public List<TracePath> Paths { get; } = new();
        public bool Truncated { get; private set; }

        public void Trace(string stateType, string key)
        {
            // An event goal may already happen on its own.
            if (stateType == "event" && IsSelfFiring(key))
                Paths.Add(new TracePath(new List<TraceStep>
                {
                    new($"event:{key}", "self", "event", key, "event fires on its own (MTTH/on_action)"),
                }));

            Dfs(stateType, key, new List<TraceStep>(), new HashSet<string>(), 0);
        }

        private void Dfs(string stateType, string key, List<TraceStep> chain, HashSet<string> visited, int depth)
        {
            if (Paths.Count >= MaxPaths)
                return;

            if (depth >= maxDepth || _budget-- <= 0)
            {
                Truncated = true;
                return;
            }

            foreach (var producer in ProducersOf(stateType, key))
            {
                if (Paths.Count >= MaxPaths)
                    return;

                var token = $"{producer.EntityType}:{producer.EntityKey}";

                if (!visited.Add(token))
                    continue;

                var (isBase, role, recurse) = Classify(producer.EntityType, producer.EntityKey);
                var step = new TraceStep($"{stateType}:{key}", producer.ViaKind, producer.EntityType, producer.EntityKey, role);
                var nextChain = new List<TraceStep>(chain) { step };

                if (isBase || recurse is null)
                {
                    var ordered = new List<TraceStep>(nextChain);
                    ordered.Reverse();
                    Paths.Add(new TracePath(ordered));
                }
                else
                {
                    Dfs(recurse.Value.Type, recurse.Value.Key, nextChain, visited, depth + 1);
                }

                visited.Remove(token);
            }
        }

        private (bool IsBase, string Role, (string Type, string Key)? Recurse) Classify(string entityType, string entityKey) =>
            entityType switch
            {
                "decision" => (true, "click decision", null),
                "mission" => (true, "complete mission", null),
                "on_action" => (true, $"fires via on_action {entityKey}", null),
                "scripted_effect" => (false, "call scripted effect", ("scripted_call", entityKey)),
                "event" when IsSelfFiring(entityKey) => (true, "event fires on its own (MTTH/on_action)", null),
                "event" => (false, "trigger event", ("event", entityKey)),
                _ => (true, $"active state ({entityType})", null),
            };

        private bool IsSelfFiring(string eventKey)
        {
            if (_selfFiring.TryGetValue(eventKey, out var cached))
                return cached;

            var details = db.Query(
                """
                SELECT ed.is_triggered_only, ed.has_mtth
                FROM entities e JOIN event_details ed ON ed.entity_id = e.entity_id
                WHERE e.entity_type = 'event' AND e.entity_key = $k AND e.is_effective = 1
                LIMIT 1
                """,
                r => new { TriggeredOnly = r.GetInt64(0) != 0, HasMtth = r.GetInt64(1) != 0 },
                new Dictionary<string, object?> { ["$k"] = eventKey },
                1).FirstOrDefault();

            bool result;

            if (details is null)
            {
                result = true; // unknown event: treat as terminal to avoid infinite chains
            }
            else
            {
                var firedByAction = db.QueryScalar<long>(
                    "SELECT count(*) FROM refs WHERE ref_kind = 'on_action_fires' AND target_type = 'event' AND target_key = $k",
                    new Dictionary<string, object?> { ["$k"] = eventKey }) > 0;

                result = details.HasMtth || !details.TriggeredOnly || firedByAction;
            }

            _selfFiring[eventKey] = result;
            return result;
        }

        private List<Producer> ProducersOf(string stateType, string key)
        {
            var where = stateType switch
            {
                "event" => "r.ref_kind IN ('fires_event','on_action_fires') AND r.target_type = 'event' AND r.target_key = $k",
                "scripted_call" => "r.ref_kind = 'calls_scripted_effect' AND r.target_key = $k",
                "variable" => "r.ref_kind = 'sets_variable' AND r.target_key = $k",
                _ => "r.ref_kind = 'sets_flag' AND r.target_type = $tt AND r.target_key = $k",
            };

            var parameters = new Dictionary<string, object?> { ["$k"] = key };

            if (where.Contains("$tt"))
                parameters["$tt"] = stateType;

            return db.Query(
                $"""
                 SELECT DISTINCT r.ref_kind, fe.entity_type, fe.entity_key
                 FROM refs r
                 JOIN entities fe ON fe.entity_id = r.from_entity_id
                 WHERE {where} AND fe.is_effective = 1
                 LIMIT {ProducerCap}
                 """,
                r => new Producer(r.GetString(0), r.GetString(1), r.GetString(2)),
                parameters,
                ProducerCap);
        }

        private sealed record Producer(string ViaKind, string EntityType, string EntityKey);
    }
}

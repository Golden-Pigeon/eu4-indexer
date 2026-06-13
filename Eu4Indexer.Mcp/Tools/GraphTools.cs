using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Eu4Indexer.Mcp;

/// <summary>Directed traversal of the reference graph: backwards, forwards, and by condition.</summary>
[McpServerToolType]
public static class GraphTools
{
    [McpServerTool(Name = "what_triggers")]
    [Description(
        "Reverse lookup: what causes this entity to happen. Lists every entity that " +
        "fires or references it (other events' effects, decisions, on_actions), and for " +
        "an event also describes how it can fire on its own (triggered-only vs " +
        "mean-time-to-happen). Use to trace why something occurs. Returns null if not found.")]
    public static InboundResult? WhatTriggers(
        Eu4Database db,
        [Description("Entity key/id, e.g. 'my_events.1'.")] string entityKey,
        [Description("Entity type; omit to infer (events preferred).")] string? entityType = null)
    {
        var resolved = db.ResolveEntity(entityType, entityKey);

        if (resolved is null)
            return null;

        var (entityId, entityRef) = resolved.Value;
        var targetType = Eu4Database.InboundTargetType(entityRef.EntityType);

        var inbound = targetType is null
            ? new List<InboundEdge>()
            : db.Query(
                """
                SELECT r.ref_kind, fe.entity_type, fe.entity_key, r.from_context
                FROM refs r
                JOIN entities fe ON fe.entity_id = r.from_entity_id
                WHERE r.target_key = $k AND r.target_type = $tt AND fe.is_effective = 1
                ORDER BY r.ref_kind, fe.entity_key
                """,
                r => new InboundEdge(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)),
                new Dictionary<string, object?> { ["$k"] = entityRef.EntityKey, ["$tt"] = targetType },
                500);

        string? firingModel = null;

        if (entityRef.EntityType == "event")
        {
            var details = db.Query(
                "SELECT is_triggered_only, has_mtth FROM event_details WHERE entity_id = $id",
                r => new { TriggeredOnly = r.GetInt64(0) != 0, HasMtth = r.GetInt64(1) != 0 },
                new Dictionary<string, object?> { ["$id"] = entityId },
                1).FirstOrDefault();

            if (details is not null)
                firingModel = (details.TriggeredOnly, details.HasMtth) switch
                {
                    (true, _) => "triggered_only: must be fired by another event, decision or on_action",
                    (false, true) => "mean_time_to_happen: can fire on its own over time while its conditions hold",
                    _ => "neither triggered_only nor MTTH: fired via on_action or similar engine hook",
                };
        }

        return new InboundResult(entityRef, firingModel, inbound);
    }

    [McpServerTool(Name = "what_does_it_do")]
    [Description(
        "Forward lookup: what this entity references — events it fires, flags/variables " +
        "it sets or checks, modifiers it applies or checks, and scripted triggers/effects " +
        "it calls. Returns null if the entity is not found.")]
    public static OutboundResult? WhatDoesItDo(
        Eu4Database db,
        [Description("Entity key/id.")] string entityKey,
        [Description("Entity type; omit to infer.")] string? entityType = null)
    {
        var resolved = db.ResolveEntity(entityType, entityKey);

        if (resolved is null)
            return null;

        var (entityId, entityRef) = resolved.Value;

        var outbound = db.Query(
            """
            SELECT ref_kind, target_type, target_key, negated, from_context
            FROM refs
            WHERE from_entity_id = $id
            ORDER BY ref_kind, target_key
            """,
            r => new RefEdge(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3) != 0, r.GetString(4)),
            new Dictionary<string, object?> { ["$id"] = entityId },
            1000);

        return new OutboundResult(entityRef, outbound);
    }

    [McpServerTool(Name = "find_by_condition")]
    [Description(
        "Find every entity whose conditions check a given flag, variable, or scripted " +
        "trigger — i.e. what is gated by it. Useful for 'what depends on flag X' and for " +
        "understanding why content is locked. Optionally filter by flag scope and whether " +
        "the check is negated (inside a NOT).")]
    public static List<ConditionUser> FindByCondition(
        Eu4Database db,
        [Description("Flag, variable, or scripted-trigger name being checked.")] string name,
        [Description("Optional flag scope: country_flag|global_flag|province_flag|ruler_flag.")] string? scope = null,
        [Description("Optional: true for checks inside a NOT, false for non-negated checks.")] bool? negated = null,
        [Description("Maximum results (default 100, max 500).")] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);

        var sql =
            """
            SELECT fe.entity_type, fe.entity_key, r.ref_kind, r.from_context, r.negated
            FROM refs r
            JOIN entities fe ON fe.entity_id = r.from_entity_id
            WHERE r.target_key = $n
              AND r.ref_kind IN ('checks_flag', 'checks_variable', 'calls_scripted_trigger')
              AND fe.is_effective = 1
            """
            + (scope is null ? "" : " AND r.target_type = $scope")
            + (negated is null ? "" : " AND r.negated = $neg")
            + " ORDER BY fe.entity_type, fe.entity_key LIMIT $lim";

        var parameters = new Dictionary<string, object?> { ["$n"] = name, ["$lim"] = limit };

        if (scope is not null)
            parameters["$scope"] = scope;

        if (negated is not null)
            parameters["$neg"] = negated.Value ? 1 : 0;

        return db.Query(
            sql,
            r => new ConditionUser(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4) != 0),
            parameters,
            limit);
    }
}

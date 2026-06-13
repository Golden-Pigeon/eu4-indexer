using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Eu4Indexer.Mcp;

/// <summary>Tools that explain a single entity or resolve a symbol name.</summary>
[McpServerToolType]
public static class EntityTools
{
    [McpServerTool(Name = "explain_entity")]
    [Description(
        "Explain one entity end to end: provenance (source, effective?), localised " +
        "title/description, the full condition/effect script tree (each node tagged " +
        "with its context: trigger/effect/mtth/...), event options with their " +
        "node ids, what it references (outbound: events fired, flags/variables set " +
        "or checked, modifiers, scripted calls), and what fires or references it " +
        "(inbound). Returns null if the entity is not found.")]
    public static EntityExplanation? ExplainEntity(
        Eu4Database db,
        [Description("Entity type, e.g. 'event', 'decision', 'mission', 'scripted_trigger'.")] string entityType,
        [Description("Entity key/id, e.g. 'my_events.1'.")] string entityKey)
    {
        var parms = new Dictionary<string, object?> { ["$t"] = entityType, ["$k"] = entityKey };

        var entity = db.Query(
            """
            SELECT e.entity_id, e.entity_type, e.entity_key, e.is_effective, s.name, f.relative_path
            FROM entities e
            JOIN sources s USING (source_id)
            JOIN files f USING (file_id)
            WHERE e.entity_type = $t AND e.entity_key = $k
            ORDER BY e.is_effective DESC
            LIMIT 1
            """,
            r => new
            {
                Id = r.GetInt64(0),
                Type = r.GetString(1),
                Key = r.GetString(2),
                Effective = r.GetInt64(3) != 0,
                Source = r.GetString(4),
                Path = r.GetString(5),
            },
            parms,
            1).FirstOrDefault();

        if (entity is null)
            return null;

        var idParam = new Dictionary<string, object?> { ["$id"] = entity.Id };

        var localisation = db.Query(
            """
            SELECT el.role, el.loc_key,
                   (SELECT l.value FROM localisation l
                    WHERE l.loc_key = el.loc_key AND l.language = 'english' AND l.is_effective = 1
                    LIMIT 1)
            FROM entity_localisation el
            WHERE el.entity_id = $id
            """,
            r => new LocText(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)),
            idParam);

        var (roots, truncated) = db.LoadScriptTree(entity.Id);

        var options = db.Query(
            """
            SELECT eo.option_idx, eo.name_key, eo.node_id,
                   (SELECT l.value FROM localisation l
                    WHERE l.loc_key = eo.name_key AND l.language = 'english' AND l.is_effective = 1
                    LIMIT 1)
            FROM event_options eo
            WHERE eo.entity_id = $id
            ORDER BY eo.option_idx
            """,
            r => new OptionInfo(
                r.GetInt32(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetInt64(2)),
            idParam);

        var outbound = db.Query(
            """
            SELECT ref_kind, target_type, target_key, negated, from_context
            FROM refs
            WHERE from_entity_id = $id
            ORDER BY ref_kind, target_key
            """,
            r => new RefEdge(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3) != 0, r.GetString(4)),
            idParam,
            500);

        var inboundType = InboundTargetType(entity.Type);

        var inbound = inboundType is null
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
                new Dictionary<string, object?> { ["$k"] = entity.Key, ["$tt"] = inboundType },
                500);

        return new EntityExplanation(
            new EntityRef(entity.Type, entity.Key, entity.Source, entity.Effective, entity.Path),
            localisation,
            roots,
            truncated,
            options,
            outbound,
            inbound);
    }

    [McpServerTool(Name = "resolve_symbol")]
    [Description(
        "Explain an identifier: its trigger/effect/modifier definition(s) from the cwt " +
        "dictionary (kind, scope, source file), and — if the name is a scripted " +
        "trigger/effect/function — its definition tree so nested conditions can be expanded.")]
    public static SymbolResolution ResolveSymbol(
        Eu4Database db,
        [Description("Identifier name, e.g. 'add_stability' or a scripted trigger name.")] string name)
    {
        var nameParam = new Dictionary<string, object?> { ["$n"] = name };

        var matches = db.Query(
            "SELECT kind, scope, cwt_file FROM symbols WHERE name = $n",
            r => new SymbolMatch(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2)),
            nameParam);

        var scripted = db.Query(
            """
            SELECT entity_id, entity_type
            FROM entities
            WHERE entity_key = $n
              AND entity_type IN ('scripted_trigger', 'scripted_effect', 'scripted_function')
              AND is_effective = 1
            LIMIT 1
            """,
            r => new { Id = r.GetInt64(0), Type = r.GetString(1) },
            nameParam,
            1).FirstOrDefault();

        if (scripted is null)
            return new SymbolResolution(name, matches, null, null);

        return new SymbolResolution(name, matches, scripted.Type, db.LoadScriptTree(scripted.Id).Roots);
    }

    /// The refs.target_type to look for when finding inbound references to an entity.
    private static string? InboundTargetType(string entityType) => entityType switch
    {
        "event" => "event",
        "scripted_trigger" => "scripted_trigger",
        "scripted_effect" => "scripted_effect",
        "event_modifier" or "static_modifier" or "triggered_modifier" => "modifier",
        _ => null,
    };
}

using System.ComponentModel;
using Eu4Indexer.Core.Database;
using ModelContextProtocol.Server;

namespace Eu4Indexer.Mcp;

/// <summary>Catalog-level tools: sources, overrides, and the schema/dictionary.</summary>
[McpServerToolType]
public static class CatalogTools
{
    [McpServerTool(Name = "list_sources")]
    [Description(
        "List the indexed content sources — the base game and each mod — in load " +
        "order, with mod version, workshop id, and the folders each mod replaces " +
        "wholesale (replace_path). Later sources override earlier ones.")]
    public static List<SourceInfo> ListSources(Eu4Database db)
    {
        var sources = db.Query(
            """
            SELECT source_id, load_order, kind, name, mod_version, remote_file_id
            FROM sources
            ORDER BY load_order
            """,
            r => new
            {
                Id = r.GetInt64(0),
                LoadOrder = r.GetInt32(1),
                Kind = r.GetString(2),
                Name = r.GetString(3),
                ModVersion = r.IsDBNull(4) ? null : r.GetString(4),
                RemoteFileId = r.IsDBNull(5) ? null : r.GetString(5),
            });

        return sources
            .Select(s => new SourceInfo(
                s.LoadOrder,
                s.Kind,
                s.Name,
                s.ModVersion,
                s.RemoteFileId,
                db.Query(
                    "SELECT path FROM source_replace_paths WHERE source_id = $id ORDER BY path",
                    r => r.GetString(0),
                    new Dictionary<string, object?> { ["$id"] = s.Id })))
            .ToList();
    }

    [McpServerTool(Name = "get_overrides")]
    [Description(
        "List override relationships across all three levels (file, entity, " +
        "localisation): who overrode whom and exactly what. Optionally filter by the " +
        "winning source's load order and/or level. identical_content marks a copy that " +
        "did not actually change anything.")]
    public static List<OverrideRow> GetOverrides(
        Eu4Database db,
        [Description("Optional winner load order (1 = first mod). Omit for all.")] int? winnerLoadOrder = null,
        [Description("Optional level filter: file|entity|localisation.")] string? level = null,
        [Description("Maximum results (default 100, max 1000).")] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 1000);

        var sql =
            """
            SELECT v.level, v.kind, v.what, ws.name, ls.name, v.identical_content
            FROM v_override_summary v
            LEFT JOIN sources ws ON ws.source_id = v.winner_source_id
            JOIN sources ls ON ls.source_id = v.loser_source_id
            WHERE 1 = 1
            """
            + (winnerLoadOrder is null ? "" : " AND ws.load_order = $wlo")
            + (level is null ? "" : " AND v.level = $level")
            + " ORDER BY v.level, v.kind LIMIT $lim";

        var parameters = new Dictionary<string, object?> { ["$lim"] = limit };

        if (winnerLoadOrder is not null)
            parameters["$wlo"] = winnerLoadOrder.Value;

        if (level is not null)
            parameters["$level"] = level;

        return db.Query(
            sql,
            r => new OverrideRow(
                r.GetString(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4),
                r.GetInt64(5) != 0),
            parameters,
            limit);
    }

    [McpServerTool(Name = "describe_schema")]
    [Description(
        "Return the database schema (table DDL and views) plus a data dictionary: " +
        "entity types with counts, reference kinds, symbol kinds, override levels and " +
        "languages present. Call this first to learn what is queryable and which " +
        "entity_type / ref_kind values exist.")]
    public static SchemaInfo DescribeSchema(Eu4Database db)
    {
        List<NameCount> counts(string sql) =>
            db.Query(sql, r => new NameCount(r.GetString(0), r.GetInt64(1)));

        return new SchemaInfo(
            Schema.tablesSql + "\n" + Schema.viewsSql,
            counts("SELECT entity_type, count(*) FROM entities WHERE is_effective = 1 GROUP BY 1 ORDER BY 2 DESC"),
            counts("SELECT ref_kind, count(*) FROM refs GROUP BY 1 ORDER BY 2 DESC"),
            counts("SELECT kind, count(*) FROM symbols GROUP BY 1 ORDER BY 2 DESC"),
            counts("SELECT level, count(*) FROM v_override_summary GROUP BY 1 ORDER BY 2 DESC"),
            db.Query("SELECT DISTINCT language FROM localisation ORDER BY 1", r => r.GetString(0)));
    }
}

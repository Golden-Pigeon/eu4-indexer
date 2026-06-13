using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Eu4Indexer.Mcp;

/// <summary>Full-text search tools for when you know, or don't know, the content type.</summary>
[McpServerToolType]
public static class SearchTools
{
    [McpServerTool(Name = "search_localisation")]
    [Description(
        "Full-text search over localisation display text. Formatting markup (colour " +
        "codes, icons) is stripped and the index is CJK-friendly, so colour-split or " +
        "Chinese text is findable. Returns matching keys, languages and values.")]
    public static List<LocHit> SearchLocalisation(
        Eu4Database db,
        [Description("Text to search for. Substring match; works for CJK.")] string text,
        [Description("Optional language filter, e.g. 'english'. Default: all languages.")] string? language = null,
        [Description("Maximum results (default 30, max 200).")] int limit = 30)
    {
        limit = Math.Clamp(limit, 1, 200);

        var sql =
            """
            SELECT l.loc_key, l.language, l.value
            FROM loc_fts f
            JOIN localisation l ON l.loc_id = f.rowid
            WHERE loc_fts MATCH $q AND l.is_effective = 1
            """
            + (language is null ? "" : " AND l.language = $lang")
            + " LIMIT $lim";

        var parameters = new Dictionary<string, object?>
        {
            ["$q"] = Eu4Database.FtsPhrase(text),
            ["$lim"] = limit,
        };

        if (language is not null)
            parameters["$lang"] = language;

        return db.Query(
            sql,
            r => new LocHit(r.GetString(0), r.GetString(1), r.GetString(2)),
            parameters,
            limit);
    }

    [McpServerTool(Name = "search_everything")]
    [Description(
        "Search across entity script and localisation text at once, for when you do " +
        "not know whether the thing is an event, decision, mission or just a piece of " +
        "text. Returns mixed hits tagged with their kind (entity or localisation).")]
    public static List<SearchHit> SearchEverything(
        Eu4Database db,
        [Description("Text to search for.")] string text,
        [Description("Maximum results per source (default 15, max 100).")] int limit = 15)
    {
        limit = Math.Clamp(limit, 1, 100);
        var query = Eu4Database.FtsPhrase(text);
        var hits = new List<SearchHit>();

        hits.AddRange(db.Query(
            """
            SELECT e.entity_type, e.entity_key, substr(e.raw_text, 1, 200)
            FROM entity_fts f
            JOIN entities e ON e.entity_id = f.rowid
            WHERE entity_fts MATCH $q AND e.is_effective = 1
            LIMIT $lim
            """,
            r => new SearchHit("entity", r.GetString(0), r.GetString(1), r.GetString(2)),
            new Dictionary<string, object?> { ["$q"] = query, ["$lim"] = limit },
            limit));

        hits.AddRange(db.Query(
            """
            SELECT l.language, l.loc_key, substr(l.value, 1, 200)
            FROM loc_fts f
            JOIN localisation l ON l.loc_id = f.rowid
            WHERE loc_fts MATCH $q AND l.is_effective = 1
            LIMIT $lim
            """,
            r => new SearchHit("localisation", r.GetString(0), r.GetString(1), r.GetString(2)),
            new Dictionary<string, object?> { ["$q"] = query, ["$lim"] = limit },
            limit));

        return hits;
    }
}

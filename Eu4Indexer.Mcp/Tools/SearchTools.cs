using System.ComponentModel;
using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Eu4Indexer.Mcp;

/// <summary>Full-text search tools for when you know, or don't know, the content type.</summary>
[McpServerToolType]
public static class SearchTools
{
    /// Both search tools need the FTS indexes; surface a clear, actionable error
    /// (rather than a raw "no such table") when the index was built with --no-fts.
    private static void RequireFts(Eu4Database db)
    {
        if (!db.FtsAvailable)
            throw new McpException(
                "This index was built without full-text search (--no-fts), so text search is " +
                "unavailable. Re-index without --no-fts, or use the entity/graph tools instead.");
    }

    private static (string FromWhere, string Parameter) LocalisationMatch(string text)
    {
        // Trigram MATCH cannot produce a token from one or two Unicode scalars.
        if (text.Length > 0 && text.EnumerateRunes().Take(3).Count() < 3)
            return ("FROM localisation l WHERE l.is_effective = 1 AND instr(lower(l.value_plain), lower($q)) > 0", text);

        return ("""
            FROM loc_fts f JOIN localisation l ON l.loc_id = f.rowid
            WHERE loc_fts MATCH $q AND l.is_effective = 1
            """, Eu4Database.FtsPhrase(text));
    }

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
        RequireFts(db);
        limit = Math.Clamp(limit, 1, 200);

        var match = LocalisationMatch(text);
        var sql = "SELECT l.loc_key, l.language, l.value " + match.FromWhere
            + (language is null ? "" : " AND l.language = $lang")
            + " LIMIT $lim";

        var parameters = new Dictionary<string, object?>
        {
            ["$q"] = match.Parameter,
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
        RequireFts(db);
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

        var match = LocalisationMatch(text);
        hits.AddRange(db.Query(
            "SELECT l.language, l.loc_key, substr(l.value, 1, 200) " + match.FromWhere + " LIMIT $lim",
            r => new SearchHit("localisation", r.GetString(0), r.GetString(1), r.GetString(2)),
            new Dictionary<string, object?> { ["$q"] = match.Parameter, ["$lim"] = limit },
            limit));

        return hits;
    }
}

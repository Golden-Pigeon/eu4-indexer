namespace Eu4Indexer.Core

/// Reads back the inputs of a previously-built SQLite index from its `meta`
/// table, so `refresh` can replay the original `index` invocation without the
/// user re-specifying game dir, mods, config dir, languages, or flags. Opened
/// read-only so the served index is never disturbed.
module IndexInputs =

    open System
    open Microsoft.Data.Sqlite

    /// meta keys recording the original source selection. The CLI writes these as
    /// ExtraMeta on the index request; this module reads them back. Defined here
    /// so the writer and reader share one set of strings (no drift).
    [<Literal>]
    let GameDirKey = "idx_game_dir"

    [<Literal>]
    let ModsKey = "idx_mods"

    [<Literal>]
    let WorkshopIdsKey = "idx_workshop_ids"

    [<Literal>]
    let PlaysetKey = "idx_playset"

    [<Literal>]
    let AutoModsKey = "idx_auto_mods"

    // List-valued meta (mods / workshop ids) is stored as a newline-joined
    // string; newline never appears inside a path or a numeric workshop id.
    let private listSep = '\n'

    /// Inputs recovered from a built index's meta table.
    type Inputs =
        { GameId: string
          ConfigDir: string
          Languages: string list
          SkipGeneric: bool
          WithFts: bool
          /// Explicit base-game dir the user passed, or None when it was
          /// auto-detected (refresh re-detects so a relocated game still works).
          ExplicitGameDir: string option
          ExplicitMods: string list
          WorkshopIds: string list
          Playset: string option
          AutoMods: bool }

    /// Encode a source selection into the ExtraMeta rows the index writes. Kept
    /// next to `read` so the two stay in lockstep.
    let toExtraMeta
        (explicitGameDir: string option)
        (explicitMods: string list)
        (workshopIds: string list)
        (playset: string option)
        (autoMods: bool)
        : (string * string) list =
        [ GameDirKey, Option.defaultValue "" explicitGameDir
          ModsKey, String.Join(listSep, explicitMods)
          WorkshopIdsKey, String.Join(listSep, workshopIds)
          PlaysetKey, Option.defaultValue "" playset
          AutoModsKey, (if autoMods then "1" else "0") ]

    let private readMeta (dbPath: string) : Map<string, string> =
        use conn = new SqliteConnection(sprintf "Data Source=%s;Mode=ReadOnly" dbPath)
        conn.Open()
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "SELECT key, value FROM meta"
        use r = cmd.ExecuteReader()

        [ while r.Read() do
              yield r.GetString 0, r.GetString 1 ]
        |> Map.ofList

    let private splitOn (sep: char) (s: string) =
        s.Split(sep)
        |> Array.map (fun x -> x.Trim())
        |> Array.filter (fun x -> x <> "")
        |> List.ofArray

    /// Read the inputs back from a built SQLite index. Missing keys fall back to
    /// safe defaults so an index built before these keys existed still refreshes
    /// (with an empty mod selection rather than crashing).
    let read (dbPath: string) : Inputs =
        let meta = readMeta dbPath
        let getOr k d = Map.tryFind k meta |> Option.defaultValue d
        let optNonEmpty k = Map.tryFind k meta |> Option.bind (fun v -> if v = "" then None else Some v)

        { GameId = getOr "game_id" "eu4"
          ConfigDir = getOr "config_repo_path" ""
          Languages = getOr "languages" "" |> splitOn ','
          SkipGeneric = getOr "skip_generic" "0" = "1"
          WithFts = getOr "with_fts" "1" = "1"
          ExplicitGameDir = optNonEmpty GameDirKey
          ExplicitMods = getOr ModsKey "" |> splitOn listSep
          WorkshopIds = getOr WorkshopIdsKey "" |> splitOn listSep
          Playset = optNonEmpty PlaysetKey
          AutoMods = getOr AutoModsKey "0" = "1" }

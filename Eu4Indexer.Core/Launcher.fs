namespace Eu4Indexer.Core

/// Reads the Paradox launcher's playset database (launcher-v2.sqlite): the list
/// of playsets and, per playset, its mods with enabled flag and load order.
/// Opened read-only so the launcher's own copy is never disturbed.
module Launcher =

    open System
    open Microsoft.Data.Sqlite

    type PlaysetInfo = { Id: string; Name: string; IsActive: bool }

    /// One mod entry of a playset, straight from the launcher db. Paths point at
    /// Steam Workshop content (DirPath, absolute) or a local descriptor
    /// (Descriptor, relative to the user-data dir); SteamId is the Workshop id.
    /// Any of these may be missing for an unsubscribed/broken mod.
    type PlaysetMod =
        { Name: string
          SteamId: string option
          DirPath: string option
          Descriptor: string option
          Enabled: bool
          Position: int }

    let private connect (dbPath: string) =
        let conn = new SqliteConnection(sprintf "Data Source=%s;Mode=ReadOnly" dbPath)
        conn.Open()
        conn

    let private optStr (r: SqliteDataReader) (i: int) =
        if r.IsDBNull i then
            None
        else
            match r.GetString i with
            | "" -> None
            | s -> Some s

    /// All playsets (excluding removed ones), active first then by name.
    let listPlaysets (dbPath: string) : PlaysetInfo list =
        use conn = connect dbPath
        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            "SELECT id, name, COALESCE(isActive, 0) FROM playsets \
             WHERE COALESCE(isRemoved, 0) = 0 ORDER BY isActive DESC, name"

        use r = cmd.ExecuteReader()

        [ while r.Read() do
              { Id = r.GetString 0
                Name = r.GetString 1
                IsActive = r.GetInt64 2 <> 0L } ]

    /// Find a playset by id or (case-insensitive) name; active wins ties.
    let findPlayset (dbPath: string) (nameOrId: string) : PlaysetInfo option =
        listPlaysets dbPath
        |> List.tryFind (fun p ->
            String.Equals(p.Id, nameOrId, StringComparison.OrdinalIgnoreCase)
            || String.Equals(p.Name, nameOrId, StringComparison.OrdinalIgnoreCase))

    /// Mods of a playset in load order (position ascending).
    let playsetMods (dbPath: string) (playsetId: string) : PlaysetMod list =
        use conn = connect dbPath
        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            "SELECT m.displayName, m.name, m.steamId, m.dirPath, m.gameRegistryId, \
                    COALESCE(pm.enabled, 1), COALESCE(pm.position, 0) \
             FROM playsets_mods pm JOIN mods m ON m.id = pm.modId \
             WHERE pm.playsetId = $id ORDER BY pm.position"

        cmd.Parameters.AddWithValue("$id", playsetId) |> ignore
        use r = cmd.ExecuteReader()

        [ while r.Read() do
              let name =
                  optStr r 0
                  |> Option.orElse (optStr r 1)
                  |> Option.orElse (optStr r 2)
                  |> Option.defaultValue "(unnamed)"

              { Name = name
                SteamId = optStr r 2
                DirPath = optStr r 3
                Descriptor = optStr r 4
                Enabled = r.GetInt64 5 <> 0L
                Position = int (r.GetInt64 6) } ]

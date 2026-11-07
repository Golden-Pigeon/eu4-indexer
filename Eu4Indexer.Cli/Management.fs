namespace Eu4Indexer.Cli

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Eu4Indexer.Core
open Eu4Indexer.Core.Database
open Microsoft.Data.Sqlite

/// Versioned JSONL management surface. The existing human CLI is unchanged.
module Management =
    let private options = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    let private emit kind data =
        Console.Out.WriteLine(JsonSerializer.Serialize({| apiVersion = 1; ``type`` = kind; data = data |}, options))
        Console.Out.Flush()
    let private prop (name: string) (r: JsonElement) =
        let mutable p = Unchecked.defaultof<JsonElement>
        if r.TryGetProperty(name, &p) then Some p else None
    let private str name fallback r =
        match prop name r with
        | None -> fallback
        | Some p when p.ValueKind = JsonValueKind.String -> p.GetString()
        | _ -> invalidArg name "Expected a string"
    let private opt name r = let s = str name "" r in if s = "" then None else Some s
    let private flag name fallback r =
        match prop name r with
        | None -> fallback
        | Some p when p.ValueKind = JsonValueKind.True -> true
        | Some p when p.ValueKind = JsonValueKind.False -> false
        | _ -> invalidArg name "Expected a boolean"
    let private strings name r =
        match prop name r with
        | None -> []
        | Some p when p.ValueKind = JsonValueKind.Array ->
            p.EnumerateArray() |> Seq.map (fun v ->
                if v.ValueKind <> JsonValueKind.String then invalidArg name "Expected an array of strings"
                v.GetString()) |> List.ofSeq
        | _ -> invalidArg name "Expected an array of strings"
    let private unwrap = function Result.Ok x -> x | Result.Error e -> invalidOp e
    let private adapter r =
        let id = str "game" "eu4" r
        GameAdapter.byId id |> Option.defaultWith (fun () -> invalidArg "game" ("Unsupported game: " + id))
    let private nameOf r =
        let name = str "name" "default" r
        if not (Regex.IsMatch(name, "^[A-Za-z0-9][A-Za-z0-9_.-]{0,79}$")) then
            invalidArg "name" "Use 1-80 ASCII letters, digits, dots, underscores or hyphens"
        name
    let private resolveMods a r =
        let resolve resolver values = values |> List.map (resolver >> unwrap)
        let explicit = resolve (Discovery.resolveExplicitMod Discovery.realProbe) (strings "mods" r)
        let workshop = resolve (Discovery.resolveWorkshopMod a Discovery.realProbe) (strings "workshopIds" r)
        let playset, pw = match opt "playset" r with
                          | Some p -> Discovery.resolvePlayset a Discovery.realProbe p |> unwrap
                          | None -> [], []
        let auto, aw = if flag "autoMods" false r then Discovery.discoverMods a Discovery.realProbe else [], []
        // Unresolved selected mods must never silently produce a different index.
        if not pw.IsEmpty || not aw.IsEmpty then invalidOp (String.concat "; " (pw @ aw))
        explicit @ workshop @ playset @ auto
    let private configDir game r =
        str "configDir" (AppPaths.configDir game) r |> Path.GetFullPath
    let private dbEntry name =
        Registry.load() |> Registry.findByName name |> Option.defaultWith (fun () -> invalidArg "name" ("Unknown index: " + name))
    let private connection path =
        let c = new SqliteConnection(SqliteConnectionStringBuilder(DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false).ToString())
        c.Open()
        c
    let private inspectPage path offset limit =
        use c = connection path
        use cmd = c.CreateCommand()
        cmd.CommandText <- "PRAGMA user_version"
        let version = Convert.ToInt32(cmd.ExecuteScalar())
        if version <> Schema.UserVersion then invalidOp "Index schema does not match this engine; rebuild the index"
        cmd.CommandText <- "PRAGMA quick_check"
        if string (cmd.ExecuteScalar()) <> "ok" then invalidOp "Index integrity check failed"
        cmd.CommandText <- "SELECT key,value FROM meta"
        use reader = cmd.ExecuteReader()
        let meta = Collections.Generic.Dictionary<string,string>()
        while reader.Read() do meta[reader.GetString 0] <- reader.GetString 1
        reader.Close()
        cmd.CommandText <- "SELECT p.file_id,p.line,p.message,f.relative_path,p.col FROM parse_errors p JOIN files f ON f.file_id=p.file_id ORDER BY p.file_id,p.rowid LIMIT $limit OFFSET $offset"
        cmd.Parameters.AddWithValue("$limit", limit) |> ignore
        cmd.Parameters.AddWithValue("$offset", offset) |> ignore
        use errors = cmd.ExecuteReader()
        let rows = [| while errors.Read() do
                          yield {| fileId = errors.GetInt64 0; line = if errors.IsDBNull 1 then Nullable<int64>() else Nullable(errors.GetInt64 1)
                                   message = errors.GetString 2; path = errors.GetString 3
                                   column = if errors.IsDBNull 4 then Nullable<int64>() else Nullable(errors.GetInt64 4) |} |]
        errors.Close()
        cmd.CommandText <- "SELECT COUNT(*) FROM parse_errors"
        let errorCount = Convert.ToInt64(cmd.ExecuteScalar())
        {| path = path; schemaVersion = version; meta = meta; parseErrors = rows; parseErrorCount = errorCount
           errorOffset = offset; errorLimit = limit; hasMoreErrors = int64 offset + int64 rows.Length < errorCount |}
    let private inspect path = inspectPage path 0 100
    let private withLock (action: unit -> 'a) =
        AppPaths.ensureDir (AppPaths.home()) |> ignore
        let deadline = DateTime.UtcNow.AddSeconds 30.
        let rec acquire () =
            try new FileStream(Path.Combine(AppPaths.home(), "management.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
            with :? IOException when DateTime.UtcNow < deadline -> Threading.Thread.Sleep 50; acquire()
        use held = acquire()
        action()
    let private register name path active =
        let info = inspect path
        let game = if info.meta.ContainsKey "game_id" then info.meta["game_id"] else "eu4"
        use c = connection path
        use cmd = c.CreateCommand()
        cmd.CommandText <- "SELECT COUNT(*) FROM sources"
        let entry: Registry.DbEntry =
            { Name = name; Game = game; Path = Path.GetFullPath path; SchemaVersion = info.schemaVersion
              Sources = Convert.ToInt32(cmd.ExecuteScalar()); IndexedAt = DateTime.UtcNow.ToString("o") }
        withLock (fun () -> Registry.upsert entry active)
        entry
    let private build (r: JsonElement) =
        let a = adapter r
        let name = nameOf r
        let gameDir = Discovery.resolveGameDir a Discovery.realProbe (opt "gameDir" r) |> unwrap
        let mods = resolveMods a r
        let cfg = configDir a.GameId r
        if not (Directory.Exists cfg) then invalidOp "Config rules are missing; run setup first"
        let dir = AppPaths.ensureDir (AppPaths.dbDir a.GameId)
        let id = Guid.NewGuid().ToString("N")
        let staging = Path.Combine(dir, name + "-" + id + ".building")
        let target = Path.Combine(dir, name + "-" + id + ".db")
        try
            let request: Pipeline.IndexRequest =
                { Adapter = a; GameDir = gameDir; Mods = mods; ConfigDir = cfg; DbPath = staging
                  SkipGeneric = flag "skipGeneric" false r; WithFts = not (flag "noFts" false r)
                  Languages = strings "languages" r
                  Log = fun message -> emit "log" {| message = message |}
                  Progress = fun p -> emit "progress" p
                  ExtraMeta = IndexInputs.toExtraMeta (opt "gameDir" r) (strings "mods" r) (strings "workshopIds" r) (opt "playset" r) (flag "autoMods" false r) }
            let report = Pipeline.run request |> unwrap
            if report.ForeignKeyViolations <> 0 then invalidOp "Index failed foreign-key validation"
            inspect staging |> ignore
            SqliteConnection.ClearAllPools()
            File.Move(staging, target)
            let entry = register name target (flag "setActive" false r)
            box {| index = entry; report = report |}
        with _ ->
            SqliteConnection.ClearAllPools()
            if File.Exists staging then File.Delete staging
            reraise()
    let private refresh r =
        let old = dbEntry (nameOf r)
        let input = IndexInputs.read old.Path
        let json = JsonSerializer.Serialize(
            {| game = input.GameId; name = old.Name; gameDir = Option.defaultValue "" input.ExplicitGameDir
               mods = List.toArray input.ExplicitMods; workshopIds = List.toArray input.WorkshopIds
               playset = Option.defaultValue "" input.Playset; autoMods = input.AutoMods
               configDir = input.ConfigDir; languages = List.toArray input.Languages
               skipGeneric = input.SkipGeneric; noFts = not input.WithFts |}, options)
        use doc = JsonDocument.Parse json
        build doc.RootElement
    let private dispatch r : obj =
        match str "operation" "capabilities" r with
        | "capabilities" -> box {| engineVersion = AppInfo.Version; schemaVersion = Schema.UserVersion; apiVersion = 1
                                   games = [| "eu4"; "hoi4" |]; querySessionMetadata = "eu4indexer/sessionId"
                                   operations = [| "capabilities"; "status"; "detect"; "workshop"; "playsets"; "list"; "inspect"; "setup"; "index"; "refresh"; "register"; "remove" |] |}
        | "status" -> box {| home = AppPaths.home(); engineVersion = AppInfo.Version; schemaVersion = Schema.UserVersion
                             games = GameAdapter.allAdapters |> List.map (fun a -> {| game = a.GameId; configDir = AppPaths.configDir a.GameId; configReady = Directory.Exists(AppPaths.configDir a.GameId) |}) |> List.toArray |}
        | "list" -> box (Registry.load())
        | "detect" ->
            let a = adapter r
            let gameDir = Discovery.resolveGameDir a Discovery.realProbe (opt "gameDir" r) |> unwrap
            let mods = resolveMods a r
            box {| game = a.GameId; gameDir = gameDir
                   mods = mods |> List.mapi (fun i m -> {| name = m.Info.Name; path = m.ContentPath; loadOrder = i + 1; replacePaths = List.toArray m.Info.ReplacePaths; dependencies = List.toArray m.Info.Dependencies |}) |> List.toArray |}
        | "workshop" -> box (Discovery.discoverWorkshopItems (adapter r) Discovery.realProbe |> List.toArray)
        | "playsets" ->
            let a = adapter r
            match Discovery.launcherDbPath a Discovery.realProbe with
            | None -> box [||]
            | Some path ->
                Launcher.listPlaysets path |> List.map (fun ps ->
                    {| id = ps.Id; name = ps.Name; isActive = ps.IsActive
                       mods = Launcher.playsetMods path ps.Id |> List.map (fun m ->
                           {| name = m.Name; workshopId = Option.defaultValue "" m.SteamId; path = Option.defaultValue "" m.DirPath; enabled = m.Enabled; position = m.Position |}) |> List.toArray |}) |> List.toArray |> box
        | "inspect" ->
            let number name fallback =
                match prop name r with
                | None -> fallback
                | Some p when p.ValueKind = JsonValueKind.Number -> p.GetInt32()
                | _ -> invalidArg name "Expected an integer"
            let offset, limit = number "errorOffset" 0, number "errorLimit" 100
            if offset < 0 || limit < 1 || limit > 1000 then invalidArg "pagination" "Expected offset >= 0 and limit between 1 and 1000"
            box (inspectPage (opt "path" r |> Option.defaultWith (fun () -> (dbEntry (nameOf r)).Path)) offset limit)
        | "register" -> box (register (nameOf r) (str "path" "" r) (flag "setActive" false r))
        | "remove" ->
            let name = nameOf r
            let entry = dbEntry name
            if flag "deleteFile" false r then
                let root = Path.GetFullPath(AppPaths.dbDir entry.Game) + string Path.DirectorySeparatorChar
                if not ((Path.GetFullPath entry.Path).StartsWith(root, StringComparison.Ordinal)) then invalidOp "External index files may only be unregistered"
            withLock (fun () ->
                Registry.update (fun c ->
                    { c with Databases = c.Databases |> Array.filter (fun e -> e.Name <> name)
                             ActiveDb = if c.ActiveDb = name then "" else c.ActiveDb }, ()))
            if flag "deleteFile" false r && File.Exists entry.Path then
                SqliteConnection.ClearAllPools()
                File.Delete entry.Path
            box {| removed = name |}
        | "setup" ->
            let a = adapter r
            Setup.fetchConfig a.GameId (opt "ref" r) (fun message -> emit "log" {| message = message |}) |> unwrap |> ignore
            box {| game = a.GameId; configDir = AppPaths.configDir a.GameId |}
        | "index" -> build r
        | "refresh" -> refresh r
        | op -> invalidArg "operation" ("Unknown management operation: " + op)
    /// Accept one JSON request and write JSONL events followed by exactly one terminal result.
    let run (request: string) =
        try
            use doc = JsonDocument.Parse request
            if doc.RootElement.ValueKind <> JsonValueKind.Object then invalidArg "request" "Expected a JSON object"
            let data = dispatch doc.RootElement
            emit "result" data
            0
        with ex ->
            emit "error" {| code = "management_failed"; message = ex.Message |}
            1

namespace Eu4Indexer.Core.Database

open System
open System.Collections.Generic
open System.Text.Json
open Microsoft.Data.Sqlite
open Eu4Indexer.Core

/// Single-writer bulk loader. Creates a fresh database, inserts with prepared
/// statements inside caller-scoped transactions, and applies indexes/FTS/views
/// at finalize time (bulk-load-then-index is far faster than maintaining
/// indexes during load).
type Writer(dbPath: string) =

    do
        if IO.File.Exists dbPath then
            IO.File.Delete dbPath

        for suffix in [ "-wal"; "-shm" ] do
            let p = dbPath + suffix
            if IO.File.Exists p then IO.File.Delete p

    let connection =
        let conn = new SqliteConnection(sprintf "Data Source=%s" dbPath)
        conn.Open()
        conn

    let exec (sql: string) =
        use cmd = connection.CreateCommand()
        cmd.CommandText <- sql
        cmd.ExecuteNonQuery() |> ignore

    do
        // fresh-build pragmas; durability is restored in Finalize
        exec "PRAGMA journal_mode=OFF"
        exec "PRAGMA synchronous=OFF"
        exec "PRAGMA temp_store=MEMORY"
        exec "PRAGMA cache_size=-200000"
        exec "PRAGMA foreign_keys=OFF"
        exec Schema.tablesSql

    let preparedCommands = Dictionary<string, SqliteCommand>()

    /// Prepared command with positional parameters $1..$n, cached per SQL text.
    let prepared (sql: string) (paramCount: int) =
        match preparedCommands.TryGetValue sql with
        | true, cmd -> cmd
        | _ ->
            let cmd = connection.CreateCommand()
            cmd.CommandText <- sql

            for i in 1..paramCount do
                cmd.Parameters.Add(cmd.CreateParameter(ParameterName = sprintf "$%d" i)) |> ignore

            cmd.Prepare()
            preparedCommands[sql] <- cmd
            cmd

    let bind (cmd: SqliteCommand) (values: obj list) =
        values
        |> List.iteri (fun i v ->
            cmd.Parameters[i].Value <- if isNull v then box DBNull.Value else v)

        cmd.ExecuteNonQuery() |> ignore

    let opt (o: 'a option) : obj =
        match o with
        | Some v -> box v
        | None -> box DBNull.Value

    let boolInt (b: bool) : obj = box (if b then 1 else 0)

    let json (o: 'a) : obj = box (JsonSerializer.Serialize o)

    member _.InTransaction(work: unit -> unit) =
        exec "BEGIN"

        try
            work ()
            exec "COMMIT"
        with _ ->
            exec "ROLLBACK"
            reraise ()

    member _.InsertSource(source: Source) =
        let cmd =
            prepared
                "INSERT INTO sources VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)"
                10

        let d = source.Descriptor

        bind
            cmd
            [ box source.SourceId
              box source.Kind.DbValue
              box source.LoadOrder
              box source.Name
              box source.RootPath
              opt source.DescriptorPath
              opt (d |> Option.bind (fun x -> x.Version))
              opt (d |> Option.bind (fun x -> x.SupportedVersion))
              opt (d |> Option.bind (fun x -> x.RemoteFileId))
              opt (d |> Option.bind (fun x -> x.Picture)) ]

        match d with
        | Some info ->
            for tag in List.distinct info.Tags do
                bind (prepared "INSERT INTO source_tags VALUES ($1,$2)" 2) [ box source.SourceId; box tag ]

            for dep in List.distinct info.Dependencies do
                bind
                    (prepared "INSERT INTO source_dependencies VALUES ($1,$2)" 2)
                    [ box source.SourceId; box dep ]

            for path in List.distinct info.ReplacePaths do
                bind
                    (prepared "INSERT INTO source_replace_paths VALUES ($1,$2)" 2)
                    [ box source.SourceId; box path ]
        | None -> ()

    member _.InsertFile(file: GameFile) =
        bind
            (prepared "INSERT INTO files VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9)" 9)
            [ box file.FileId
              box file.SourceId
              box file.RelativePath
              box file.Folder
              box file.FileName
              box file.ContentHash
              box file.ByteSize
              boolInt file.IsEffective
              box file.ParseStatus.DbValue ]

    member _.UpdateFileParseStatus(fileId: int, status: ParseStatus) =
        bind
            (prepared "UPDATE files SET parse_status = $1 WHERE file_id = $2" 2)
            [ box status.DbValue; box fileId ]

    member _.InsertParseError(error: ParseErrorRow) =
        bind
            (prepared "INSERT INTO parse_errors VALUES ($1,$2,$3,$4)" 4)
            [ box error.FileId; box error.Message; opt error.Line; opt error.Col ]

    member _.InsertFileOverride(ov: FileOverride) =
        bind
            (prepared "INSERT INTO file_overrides VALUES (NULL,$1,$2,$3,$4,$5,$6,$7)" 7)
            [ box ov.Kind.DbValue
              box ov.RelativePath
              box ov.LoserFileId
              opt ov.WinnerFileId
              box ov.WinnerSourceId
              box ov.LoserSourceId
              boolInt ov.IdenticalContent ]

    member _.InsertSymbol(symbol: Symbol) =
        bind
            (prepared "INSERT INTO symbols VALUES ($1,$2,$3,$4,$5)" 5)
            [ box symbol.SymbolId
              box symbol.Name
              box symbol.Kind.DbValue
              opt symbol.Scope
              box symbol.CwtFile ]

    member _.InsertConfigType(t: ConfigTypeInfo) =
        bind
            (prepared "INSERT OR IGNORE INTO config_types VALUES ($1,$2,$3,$4,$5,$6)" 6)
            [ box t.TypeName
              opt t.NameField
              json t.Paths
              boolInt t.TypePerFile
              json (t.SkipRootKeys |> List.map (sprintf "%A"))
              json (t.LocMappings |> List.map (fun m -> {| role = m.Role; prefix = m.Prefix; suffix = m.Suffix |})) ]

    member _.InsertPayload(payload: EntityPayload, isEffective: bool) =
        let e = payload.Entity

        bind
            (prepared "INSERT INTO entities VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)" 11)
            [ box e.EntityId
              box e.EntityType
              box e.EntityKey
              box e.FileId
              box e.SourceId
              box e.StartLine
              box e.EndLine
              box e.StmtIndex
              (if e.Subtypes.IsEmpty then box DBNull.Value else json e.Subtypes)
              box e.RawText
              boolInt isEffective ]

        let nodeCmd =
            prepared "INSERT INTO script_nodes VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13)" 13

        for n in payload.Nodes do
            bind
                nodeCmd
                [ box n.NodeId
                  box n.EntityId
                  opt n.ParentId
                  box n.Depth
                  box n.SortOrder
                  box n.NodeKind.DbValue
                  box n.Context.DbValue
                  opt n.Key
                  opt n.Operator
                  opt n.Value
                  opt (n.ValueKind |> Option.map (fun k -> k.DbValue))
                  opt n.SymbolId
                  box n.Line ]

        match payload.EventDetails with
        | Some d ->
            bind
                (prepared "INSERT INTO event_details VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12)" 12)
                [ box d.EntityId
                  box d.Namespace
                  box d.EventKind.DbValue
                  opt d.TitleKey
                  opt d.DescKey
                  opt d.Picture
                  boolInt d.IsTriggeredOnly
                  boolInt d.Hidden
                  boolInt d.FireOnlyOnce
                  boolInt d.Major
                  boolInt d.HasMtth
                  box d.OptionCount ]
        | None -> ()

        for o in payload.EventOptions do
            bind
                (prepared "INSERT INTO event_options VALUES (NULL,$1,$2,$3,$4)" 4)
                [ box o.EntityId; box o.OptionIdx; opt o.NameKey; box o.NodeId ]

        match payload.MissionDetails with
        | Some d ->
            bind
                (prepared "INSERT INTO mission_details VALUES ($1,$2,$3,$4,$5,$6,$7,$8)" 8)
                [ box d.EntityId
                  box d.SeriesKey
                  opt d.Slot
                  boolInt d.IsGeneric
                  boolInt d.Ai
                  opt d.Icon
                  opt d.Position
                  boolInt d.HasHighlight ]
        | None -> ()

        for r in payload.MissionRequirements do
            bind
                (prepared "INSERT INTO mission_requirements VALUES ($1,$2)" 2)
                [ box r.EntityId; box r.RequiredMission ]

        match payload.DecisionDetails with
        | Some d ->
            bind
                (prepared "INSERT INTO decision_details VALUES ($1,$2,$3)" 3)
                [ box d.EntityId; boolInt d.Major; opt d.AiImportance ]
        | None -> ()

        for mv in payload.ModifierValues do
            bind
                (prepared "INSERT INTO modifier_values VALUES ($1,$2,$3,$4)" 4)
                [ box mv.EntityId; box mv.ModifierKey; box mv.Value; opt mv.SymbolId ]

        for loc in payload.EntityLocs do
            bind
                (prepared "INSERT OR IGNORE INTO entity_localisation VALUES ($1,$2,$3)" 3)
                [ box loc.EntityId; box loc.Role; box loc.LocKey ]

    member _.InsertEntityOverride(ov: EntityOverride) =
        bind
            (prepared "INSERT INTO entity_overrides VALUES (NULL,$1,$2,$3,$4,$5,$6,$7,$8)" 8)
            [ box ov.Kind.DbValue
              box ov.EntityType
              box ov.EntityKey
              box ov.LoserEntityId
              opt ov.WinnerEntityId
              opt ov.WinnerSourceId
              box ov.LoserSourceId
              boolInt ov.IdenticalContent ]

    member _.InsertLocRow(row: LocRow, isEffective: bool) =
        bind
            (prepared "INSERT INTO localisation VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9)" 9)
            [ box row.LocId
              box row.LocKey
              box row.Language
              box row.Value
              opt row.VersionNum
              box row.FileId
              box row.SourceId
              boolInt row.IsReplace
              boolInt isEffective ]

    member _.InsertLocOverride(ov: LocOverride) =
        bind
            (prepared "INSERT INTO loc_overrides VALUES (NULL,$1,$2,$3,$4,$5,$6,$7,$8)" 8)
            [ box ov.LocKey
              box ov.Language
              box ov.Kind.DbValue
              box ov.LoserLocId
              opt ov.WinnerLocId
              opt ov.WinnerSourceId
              box ov.LoserSourceId
              boolInt ov.IdenticalContent ]

    /// Build indexes, optional FTS, views; record metadata; restore durability.
    member this.Finalize(meta: (string * string) list, withFts: bool) =
        exec Schema.indexesSql

        if withFts then
            exec Schema.ftsSql

        exec Schema.viewsSql

        this.InTransaction(fun () ->
            let cmd = prepared "INSERT INTO meta VALUES ($1,$2)" 2

            for key, value in meta do
                bind cmd [ box key; box value ])

        // verify referential integrity since foreign_keys was OFF during load
        let violations =
            use cmd = connection.CreateCommand()
            cmd.CommandText <- "PRAGMA foreign_key_check"
            use reader = cmd.ExecuteReader()
            let mutable count = 0
            while reader.Read() do
                count <- count + 1
            count

        exec "ANALYZE"
        exec (sprintf "PRAGMA user_version=%d" Schema.UserVersion)
        exec "PRAGMA journal_mode=WAL"
        exec "PRAGMA synchronous=NORMAL"
        violations

    interface IDisposable with
        member _.Dispose() =
            for KeyValue(_, cmd) in preparedCommands do
                cmd.Dispose()

            connection.Dispose()
            SqliteConnection.ClearAllPools()

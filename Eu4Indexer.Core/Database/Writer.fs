namespace Eu4Indexer.Core.Database

open System
open System.Collections.Generic
open System.Data.Common
open System.Text.Json
open Microsoft.Data.Sqlite
open Npgsql
open Eu4Indexer.Core

/// Backend-agnostic surface the pipeline writes the index through. Implemented
/// once by RelationalWriter; the concrete backend is chosen at construction.
type IIndexWriter =
    inherit IDisposable
    abstract InTransaction: (unit -> unit) -> unit
    abstract InsertSource: Source -> unit
    abstract InsertFile: GameFile -> unit
    abstract UpdateFileParseStatus: int * ParseStatus -> unit
    abstract InsertParseError: ParseErrorRow -> unit
    abstract InsertFileOverride: FileOverride -> unit
    abstract InsertSymbol: Symbol -> unit
    abstract InsertConfigType: ConfigTypeInfo -> unit
    abstract InsertPayload: EntityPayload * bool -> unit
    abstract InsertEntityOverride: EntityOverride -> unit
    abstract InsertLocRow: LocRow * bool -> unit
    abstract InsertReference: ReferenceRow -> unit
    abstract InsertLocOverride: LocOverride -> unit
    abstract Finalize: (string * string) list * bool -> int

/// Single-writer bulk loader over an open ADO.NET connection. Inserts with
/// prepared statements inside caller-scoped transactions, and applies
/// indexes/search/views at finalize time (bulk-load-then-index is far faster
/// than maintaining indexes during load). The SQLite and PostgreSQL backends
/// differ only by the injected Dialect.
type internal RelationalWriter(connection: DbConnection, dialect: Dialect) =

    // Active transaction, assigned to every command so both backends agree on
    // scope. Raw BEGIN/COMMIT works for SQLite but Npgsql tracks transaction
    // state internally and breaks on it, so we use real ADO.NET transactions.
    let mutable currentTx: DbTransaction = null

    let exec (sql: string) =
        use cmd = connection.CreateCommand()
        cmd.CommandText <- sql
        cmd.Transaction <- currentTx
        cmd.ExecuteNonQuery() |> ignore

    do
        for s in dialect.SetupSql do
            exec s

        exec dialect.TablesSql

    let preparedCommands = Dictionary<string, DbCommand>()

    /// Prepared command with positional parameters $1..$n, cached per SQL text.
    let prepared (sql: string) (paramCount: int) =
        match preparedCommands.TryGetValue sql with
        | true, cmd -> cmd
        | _ ->
            let cmd = connection.CreateCommand()
            cmd.CommandText <- sql

            for i in 1..paramCount do
                let p = cmd.CreateParameter()
                dialect.NameParam p i
                cmd.Parameters.Add p |> ignore

            dialect.PrepareCommand cmd
            preparedCommands[sql] <- cmd
            cmd

    let bind (cmd: DbCommand) (values: obj list) =
        cmd.Transaction <- currentTx

        values
        |> List.iteri (fun i v -> cmd.Parameters[i].Value <- if isNull v then box DBNull.Value else v)

        cmd.ExecuteNonQuery() |> ignore

    let opt (o: 'a option) : obj =
        match o with
        | Some v -> box v
        | None -> box DBNull.Value

    let boolInt (b: bool) : obj = box (if b then 1 else 0)

    let json (o: 'a) : obj = box (JsonSerializer.Serialize o)

    /// INSERT whose leading primary key is filled by the backend (NULL for
    /// SQLite rowid, DEFAULT for a Postgres identity column).
    let autoSql (table: string) (cols: string) =
        sprintf "INSERT INTO %s VALUES (%s,%s)" table dialect.AutoId cols

    member _.InTransaction(work: unit -> unit) =
        use tx = connection.BeginTransaction()
        currentTx <- tx

        try
            try
                work ()
                tx.Commit()
            with _ ->
                tx.Rollback()
                reraise ()
        finally
            currentTx <- null

    member _.InsertSource(source: Source) =
        let cmd = prepared "INSERT INTO sources VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)" 10

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
                bind (prepared "INSERT INTO source_dependencies VALUES ($1,$2)" 2) [ box source.SourceId; box dep ]

            for path in List.distinct info.ReplacePaths do
                bind (prepared "INSERT INTO source_replace_paths VALUES ($1,$2)" 2) [ box source.SourceId; box path ]
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
            (prepared (autoSql "file_overrides" "$1,$2,$3,$4,$5,$6,$7") 7)
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
            (prepared (dialect.IgnoreConflict "INSERT INTO config_types VALUES ($1,$2,$3,$4,$5,$6)") 6)
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
                (prepared (autoSql "event_options" "$1,$2,$3,$4") 4)
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
                (prepared (dialect.IgnoreConflict "INSERT INTO entity_localisation VALUES ($1,$2,$3)") 3)
                [ box loc.EntityId; box loc.Role; box loc.LocKey ]

    member _.InsertEntityOverride(ov: EntityOverride) =
        bind
            (prepared (autoSql "entity_overrides" "$1,$2,$3,$4,$5,$6,$7,$8") 8)
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
            (prepared "INSERT INTO localisation VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)" 10)
            [ box row.LocId
              box row.LocKey
              box row.Language
              box row.Value
              box row.ValuePlain
              opt row.VersionNum
              box row.FileId
              box row.SourceId
              boolInt row.IsReplace
              boolInt isEffective ]

    member _.InsertReference(r: ReferenceRow) =
        bind
            (prepared (autoSql "refs" "$1,$2,$3,$4,$5,$6,$7,$8") 8)
            [ box r.FromEntityId
              box r.FromContext
              box r.RefKind
              box r.TargetType
              box r.TargetKey
              box r.NodeId
              opt r.OptionNodeId
              boolInt r.Negated ]

    member _.InsertLocOverride(ov: LocOverride) =
        bind
            (prepared (autoSql "loc_overrides" "$1,$2,$3,$4,$5,$6,$7,$8") 8)
            [ box ov.LocKey
              box ov.Language
              box ov.Kind.DbValue
              box ov.LoserLocId
              opt ov.WinnerLocId
              opt ov.WinnerSourceId
              box ov.LoserSourceId
              boolInt ov.IdenticalContent ]

    /// Build indexes, optional search, views; record metadata; restore
    /// durability. Returns the referential-integrity violation count.
    member this.Finalize(meta: (string * string) list, withFts: bool) =
        exec dialect.IndexesSql

        if withFts then
            exec dialect.SearchSql

        exec dialect.ViewsSql

        this.InTransaction(fun () ->
            let cmd = prepared "INSERT INTO meta VALUES ($1,$2)" 2

            for key, value in meta do
                bind cmd [ box key; box value ])

        let violations = dialect.VerifyIntegrity connection

        for s in dialect.FinalizeSql do
            exec s

        dialect.RecordVersion exec
        violations

    interface IIndexWriter with
        member this.InTransaction work = this.InTransaction work
        member this.InsertSource source = this.InsertSource source
        member this.InsertFile file = this.InsertFile file
        member this.UpdateFileParseStatus(fileId, status) = this.UpdateFileParseStatus(fileId, status)
        member this.InsertParseError error = this.InsertParseError error
        member this.InsertFileOverride ov = this.InsertFileOverride ov
        member this.InsertSymbol symbol = this.InsertSymbol symbol
        member this.InsertConfigType t = this.InsertConfigType t
        member this.InsertPayload(payload, isEffective) = this.InsertPayload(payload, isEffective)
        member this.InsertEntityOverride ov = this.InsertEntityOverride ov
        member this.InsertLocRow(row, isEffective) = this.InsertLocRow(row, isEffective)
        member this.InsertReference r = this.InsertReference r
        member this.InsertLocOverride ov = this.InsertLocOverride ov
        member this.Finalize(meta, withFts) = this.Finalize(meta, withFts)

    interface IDisposable with
        member _.Dispose() =
            for KeyValue(_, cmd) in preparedCommands do
                cmd.Dispose()

            connection.Dispose()
            dialect.OnDispose()

/// Construction of the backend-specific writer.
module Writer =

    let private deleteSqliteFiles (dbPath: string) =
        if IO.File.Exists dbPath then
            IO.File.Delete dbPath

        for suffix in [ "-wal"; "-shm" ] do
            let p = dbPath + suffix
            if IO.File.Exists p then IO.File.Delete p

    /// Fresh SQLite file database.
    let createSqlite (dbPath: string) : IIndexWriter =
        deleteSqliteFiles dbPath
        let conn = new SqliteConnection(sprintf "Data Source=%s" dbPath)
        conn.Open()
        new RelationalWriter(conn, Dialect.sqlite) :> IIndexWriter

    /// Convert a libpq URI (postgres://user:pass@host:port/db) to a keyword
    /// connection string, since Npgsql expects the keyword form.
    let private pgConnFromUri (uri: string) =
        let u = Uri uri
        let userInfo = u.UserInfo.Split(':')
        let b = NpgsqlConnectionStringBuilder()
        b.Host <- u.Host

        if u.Port > 0 then
            b.Port <- u.Port

        if userInfo.Length > 0 && userInfo[0] <> "" then
            b.Username <- Uri.UnescapeDataString userInfo[0]

        if userInfo.Length > 1 then
            b.Password <- Uri.UnescapeDataString userInfo[1]

        let db = u.AbsolutePath.TrimStart('/')

        if db <> "" then
            b.Database <- db

        b.ToString()

    /// PostgreSQL target; accepts either a keyword connection string or a
    /// postgres:// URI. Existing eu4-indexer tables are dropped and rebuilt.
    let createPostgres (target: string) : IIndexWriter =
        let lower = target.ToLowerInvariant()

        let connString =
            if lower.StartsWith "postgres://" || lower.StartsWith "postgresql://" then
                pgConnFromUri target
            else
                target

        // Note: we deliberately do not enable auto-prepare. These positional
        // parameters carry no explicit NpgsqlDbType, so a server-prepared
        // statement would lock a parameter's type from its first value — and a
        // nullable column whose first row is NULL would then reject a later
        // typed value. Unprepared execution lets Postgres infer each parameter
        // type from the target column on every insert, which is always correct.
        let conn = new NpgsqlConnection(connString)
        conn.Open()
        new RelationalWriter(conn, PostgresSchema.dialect) :> IIndexWriter

    /// True when the target string denotes a PostgreSQL database rather than a
    /// SQLite file path.
    let isPostgresTarget (target: string) =
        let lower = target.Trim().ToLowerInvariant()
        lower.StartsWith "postgres://"
        || lower.StartsWith "postgresql://"
        || lower.Contains "host="

    /// Pick the backend from the target string: a postgres:// URI or a keyword
    /// connection string (containing host=) goes to PostgreSQL; anything else is
    /// treated as a SQLite file path.
    let create (target: string) : IIndexWriter =
        if isPostgresTarget target then
            createPostgres target
        else
            createSqlite target

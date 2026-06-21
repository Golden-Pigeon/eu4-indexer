module Eu4Indexer.Cli.Program

open System
open System.IO
open Argu
open Eu4Indexer.Core

// Exit codes: 0 ok, 1 usage, 2 game dir not found, 3 config invalid, 4 fatal IO.
[<Literal>]
let private ExitOk = 0

[<Literal>]
let private ExitUsage = 1

[<Literal>]
let private ExitGameNotFound = 2

[<Literal>]
let private ExitConfigInvalid = 3

type IndexArgs =
    | [<AltCommandLine("-g")>] Game_Dir of path: string
    | [<AltCommandLine("-m")>] Mod of path: string
    | [<AltCommandLine("-w")>] Workshop_Id of id: string
    | [<AltCommandLine("-p")>] Playset of name_or_id: string
    | Auto_Mods
    | [<AltCommandLine("-c")>] Config_Dir of path: string
    | [<AltCommandLine("-o")>] Db of path: string
    | [<AltCommandLine("-n")>] Name of name: string
    | Languages of langs: string
    | Skip_Generic
    | No_Fts
    | Verbose
    | Progress
    | Game of game_id: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Game_Dir _ -> "base game directory (if omitted, auto-detected via Steam libraryfolders.vdf)"
            | Mod _ -> "mod directory or .mod descriptor; repeatable, order = load order"
            | Workshop_Id _ -> "Steam Workshop item id to include as a mod; repeatable (see the 'workshop' command)"
            | Playset _ -> "launcher playset name or id; indexes its enabled mods in load order (see the 'playset' command)"
            | Auto_Mods -> "auto-discover enabled mods from the launcher / mod dir"
            | Config_Dir _ -> "cwtools config repo dir (default: $EU4_CONFIG_DIR, then ~/.eu4indexer/config/<game>)"
            | Db _ ->
                "output target (overwritten): a SQLite file path, or a PostgreSQL "
                + "connection string ('Host=...;Database=...;Username=...;Password=...' "
                + "or postgres://user:pass@host/db). Default: ~/.eu4indexer/db/<game>/<name>.db"
            | Name _ -> "registry name for this index (default: 'default'); the default db file is named after it"
            | Languages _ -> "comma-separated localisation languages (default: all)"
            | Skip_Generic -> "index only events/missions/decisions/modifiers"
            | No_Fts -> "skip building full-text search tables"
            | Verbose -> "print per-stage progress"
            | Progress -> "show a live counter of files / entities / loc entries"
            | Game _ -> "game id (default: eu4); supported: eu4, hoi4"

type DetectArgs =
    | [<AltCommandLine("-g")>] Game_Dir of path: string
    | [<AltCommandLine("-m")>] Mod of path: string
    | [<AltCommandLine("-w")>] Workshop_Id of id: string
    | [<AltCommandLine("-p")>] Playset of name_or_id: string
    | Auto_Mods
    | Game of game_id: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Game_Dir _ -> "base game directory (if omitted, auto-detected via Steam libraryfolders.vdf)"
            | Mod _ -> "mod directory or .mod descriptor; repeatable"
            | Workshop_Id _ -> "Steam Workshop item id to include as a mod; repeatable"
            | Playset _ -> "launcher playset name or id; resolves its enabled mods"
            | Auto_Mods -> "auto-discover enabled mods"
            | Game _ -> "game id (default: eu4); supported: eu4, hoi4"

type WorkshopArgs =
    | [<MainCommand>] Ids of workshop_id: string list
    | Game of game_id: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Ids _ -> "workshop ids to show (default: all installed)"
            | Game _ -> "game id (default: eu4); supported: eu4, hoi4"

type PlaysetArgs =
    | [<MainCommand>] Name of name_or_id: string list
    | Game of game_id: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Name _ -> "playset name or id to show mods for (default: list all playsets)"
            | Game _ -> "game id (default: eu4); supported: eu4, hoi4"

type VersionArgs =
    | [<Hidden>] Unused

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Unused -> ""

type ServeArgs =
    | [<AltCommandLine("-o")>] Db of path: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Db _ -> "index database to serve (default: the active database, or $EU4_DB)"

type SetupArgs =
    | Ref of ref: string
    | Game of game_id: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Ref _ -> "override the pinned cwtools config commit/branch to download"
            | Game _ -> "only download config for this game (default: all known games)"

type InstallArgs =
    | Agents of csv: string
    | Yes
    | Language of lang: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Agents _ -> "comma-separated agents to register with (default: claude,codex)"
            | Yes -> "assume yes to prompts (non-interactive)"
            | Language _ -> "skill language (default: en); supported: en, zh"

type UseArgs =
    | [<MainCommand>] Name of name: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Name _ -> "registry name of the database to make active"

type ListArgs =
    | [<Hidden>] Unused

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Unused -> ""

type UpdateArgs =
    | Check
    | Force
    | [<Hidden>] Finish_Config

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Check -> "only report whether a newer release is available; don't download"
            | Force -> "reinstall the latest release even if already up to date"
            | Finish_Config -> ""

type RefreshArgs =
    | [<AltCommandLine("-n")>] Name of name: string
    | Verbose
    | Progress

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Name _ -> "registry name of the index to re-index (default: refresh every registered index)"
            | Verbose -> "print per-stage progress"
            | Progress -> "show a live counter of files / entities / loc entries"

type CliArgs =
    | [<CliPrefix(CliPrefix.None)>] Index of ParseResults<IndexArgs>
    | [<CliPrefix(CliPrefix.None)>] Refresh of ParseResults<RefreshArgs>
    | [<CliPrefix(CliPrefix.None)>] Detect of ParseResults<DetectArgs>
    | [<CliPrefix(CliPrefix.None)>] Workshop of ParseResults<WorkshopArgs>
    | [<CliPrefix(CliPrefix.None)>] Playset of ParseResults<PlaysetArgs>
    | [<CliPrefix(CliPrefix.None)>] Serve of ParseResults<ServeArgs>
    | [<CliPrefix(CliPrefix.None)>] Setup of ParseResults<SetupArgs>
    | [<CliPrefix(CliPrefix.None)>] Install of ParseResults<InstallArgs>
    | [<CliPrefix(CliPrefix.None)>] Use of ParseResults<UseArgs>
    | [<CliPrefix(CliPrefix.None)>] List of ParseResults<ListArgs>
    | [<CliPrefix(CliPrefix.None)>] Update of ParseResults<UpdateArgs>
    | [<CliPrefix(CliPrefix.None)>] Version of ParseResults<VersionArgs>

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Index _ -> "parse game + mods and write the SQLite index"
            | Refresh _ -> "re-index registered databases in place (after a game or mod update)"
            | Detect _ -> "show resolved game dir, mods, and predicted file overrides"
            | Workshop _ -> "list installed Steam Workshop items (id and mod name)"
            | Playset _ -> "list launcher playsets, or the mods of one playset"
            | Serve _ -> "run the read-only MCP server over stdio"
            | Setup _ -> "download the cwtools config rules for the game into ~/.eu4indexer/config/<game>"
            | Install _ -> "register the MCP server + skill with local agents (Claude Code, Codex)"
            | Use _ -> "set the active index (by registry name) the MCP server serves by default"
            | List _ -> "list registered indexes (* marks the active one)"
            | Update _ -> "self-update the eu4indexer binary to the latest release and update stale config"
            | Version _ -> "print the eu4indexer version and exit"

// Config dir resolution: $EU4_CONFIG_DIR, then the game-namespaced install dir
// ~/.eu4indexer/config/<game> (populated by `eu4indexer setup`).
let private defaultConfigDir (gameId: string) =
    match Environment.GetEnvironmentVariable "EU4_CONFIG_DIR" with
    | null | "" ->
        let installed = AppPaths.configDir gameId
        if Directory.Exists installed then Some installed else None
    | dir -> Some dir

let private resolveAdapter (gameIdOpt: string option) =
    let id = gameIdOpt |> Option.defaultValue "eu4"
    match GameAdapter.byId id with
    | Some a -> a
    | None ->
        eprintfn "warning: unknown game '%s', falling back to eu4" id
        GameAdapter.eu4

let private resolveMods
    (probe: Discovery.FsProbe)
    (explicit: string list)
    (workshopIds: string list)
    (playset: string option)
    (auto: bool)
    (adapter: GameAdapter)
    =
    // Resolve each item with the given resolver, accumulating mods (in order)
    // and any warnings.
    let resolveAll resolver items =
        items
        |> List.fold
            (fun (mods, warns) item ->
                match resolver item with
                | Result.Ok m -> mods @ [ m ], warns
                | Result.Error e -> mods, warns @ [ e ])
            ([], [])

    let explicitMods, explicitWarnings = resolveAll (Discovery.resolveExplicitMod probe) explicit
    let workshopMods, workshopWarnings = resolveAll (Discovery.resolveWorkshopMod adapter probe) workshopIds

    let playsetMods, playsetWarnings =
        match playset with
        | None -> [], []
        | Some p ->
            match Discovery.resolvePlayset adapter probe p with
            | Result.Ok(mods, warns) -> mods, warns
            | Result.Error e -> [], [ e ]

    let autoMods, autoWarnings =
        if auto then Discovery.discoverMods adapter probe else [], []

    // Load order: explicit --mod paths, then --workshop-id, then --playset, then auto.
    explicitMods @ workshopMods @ playsetMods @ autoMods,
    explicitWarnings @ workshopWarnings @ playsetWarnings @ autoWarnings

let private runDetect (args: ParseResults<DetectArgs>) =
    let adapter = resolveAdapter (args.TryGetResult DetectArgs.Game)
    let probe = Discovery.realProbe
    let explicitGameDir = args.TryGetResult DetectArgs.Game_Dir
    let explicitMods = args.GetResults DetectArgs.Mod
    let workshopIds = args.GetResults DetectArgs.Workshop_Id
    let playset = args.TryGetResult DetectArgs.Playset
    let auto = args.Contains DetectArgs.Auto_Mods

    match Discovery.resolveGameDir adapter probe explicitGameDir with
    | Result.Error e ->
        eprintfn "game dir: %s" e
        ExitGameNotFound
    | Result.Ok gameDir ->
        printfn "Game dir: %s" gameDir
        let mods, warnings = resolveMods probe explicitMods workshopIds playset auto adapter

        warnings |> List.iter (eprintfn "warning: %s")

        printfn "Mods (%d), in load order:" mods.Length

        mods
        |> List.iteri (fun i m ->
            printfn "  [%d] %s" (i + 1) m.Info.Name
            printfn "      path: %s" m.ContentPath

            if not m.Info.ReplacePaths.IsEmpty then
                printfn "      replace_path: %s" (String.concat ", " m.Info.ReplacePaths)

            if not m.Info.Dependencies.IsEmpty then
                printfn "      dependencies: %s" (String.concat ", " m.Info.Dependencies))

        ExitOk

// Shared indexing core: run the pipeline for an already-resolved set of inputs,
// render progress, print the summary, and register the SQLite result. Used by
// both `index` (inputs parsed from the CLI) and `refresh` (inputs replayed from
// a previously-built index's meta).
let private runIndexCore
    (adapter: GameAdapter)
    (verbose: bool)
    (showProgress: bool)
    (gameDir: string)
    (mods: Discovery.DiscoveredMod list)
    (configDir: string)
    (dbPath: string)
    (dbName: string)
    (skipGeneric: bool)
    (withFts: bool)
    (languages: string list)
    // `index` marks the result active; `refresh` re-indexes in place and leaves
    // the active selection untouched (false), so a batch refresh doesn't flip it.
    (setActive: bool)
    (extraMeta: (string * string) list)
    : int =

    // In-place line refresh only makes sense on a real terminal; when stderr is
    // redirected (pipe / CI), fall back to periodic full lines.
    let progressInteractive = showProgress && not System.Console.IsErrorRedirected

    // A milestone line must clear the in-place status line before printing, or it
    // overwrites a partial frame.
    let log (msg: string) =
        if verbose then
            if progressInteractive then eprintf "\r\027[K"
            eprintfn "%s" msg

    let progressSw = System.Diagnostics.Stopwatch.StartNew()
    let mutable lastProgressKey = ""

    let renderProgress (p: Pipeline.IndexProgress) =
        // Force a draw on phase/sub-step change or at end of a per-file phase, so
        // the finalize labels (which share one phase) and the final frame aren't
        // swallowed by the throttle.
        let key = p.Phase + "/" + p.Detail
        let phaseEnd = p.FilesTotal > 0 && p.FilesDone >= p.FilesTotal

        if key <> lastProgressKey || phaseEnd || progressSw.ElapsedMilliseconds >= 100L then
            progressSw.Restart()
            lastProgressKey <- key
            let line = Pipeline.formatProgress p
            if progressInteractive then eprintf "\r\027[K%s" line else eprintfn "%s" line

    let request: Pipeline.IndexRequest =
        { Adapter = adapter
          GameDir = gameDir
          Mods = mods
          ConfigDir = configDir
          DbPath = dbPath
          SkipGeneric = skipGeneric
          WithFts = withFts
          Languages = languages
          Log = log
          Progress = if showProgress then renderProgress else ignore
          ExtraMeta = extraMeta }

    let result = Pipeline.run request
    // Finish the in-place status line so the summary starts on its own line.
    if progressInteractive then eprintfn ""

    match result with
    | Result.Error e ->
        eprintfn "error: %s" e
        ExitConfigInvalid
    | Result.Ok report ->
        printfn "Indexed %d sources, %d files, %d entities (%d effective), %d loc entries."
            report.SourceCount report.FileCount report.EntityCount report.EffectiveEntityCount report.LocCount
        printfn "Overrides: %d. Parse errors: %d. FK violations: %d."
            report.OverrideCount report.ParseErrorCount report.ForeignKeyViolations

        // Register SQLite indexes so `serve`/`use`/`list` and the MCP server can
        // find them. Postgres targets aren't local files the MCP server reads.
        if not (Eu4Indexer.Core.Database.Writer.isPostgresTarget dbPath) then
            let entry: Registry.DbEntry =
                { Name = dbName
                  Game = adapter.GameId
                  Path = Path.GetFullPath dbPath
                  SchemaVersion = Eu4Indexer.Core.Database.Schema.UserVersion
                  Sources = report.SourceCount
                  IndexedAt = DateTime.UtcNow.ToString("o") }

            Registry.upsert entry setActive
            let activeNote = if setActive then " (active)" else ""
            printfn "Registered index '%s'%s at %s" dbName activeNote (AppPaths.normalize (Path.GetFullPath dbPath))

        if report.ForeignKeyViolations > 0 then 4 else ExitOk

let private runIndex (args: ParseResults<IndexArgs>) =
    let adapter = resolveAdapter (args.TryGetResult IndexArgs.Game)
    let probe = Discovery.realProbe
    let verbose = args.Contains IndexArgs.Verbose
    let showProgress = args.Contains IndexArgs.Progress

    let configDir =
        match args.TryGetResult Config_Dir |> Option.orElse (defaultConfigDir adapter.GameId) with
        | Some dir -> dir
        | None -> ""

    if configDir = "" || not (Directory.Exists configDir) then
        eprintfn "config dir not found; pass --config-dir, set EU4_CONFIG_DIR, or run 'eu4indexer setup'%s"
            (if adapter.GameId <> "eu4" then sprintf " --game %s" adapter.GameId else "")
        ExitConfigInvalid
    else

    // Registry name and resolved output target. Without --db, default to the
    // game-namespaced install dir and register the result as the active index.
    let dbName = args.TryGetResult IndexArgs.Name |> Option.defaultValue "default"

    let dbPath =
        match args.TryGetResult IndexArgs.Db with
        | Some target -> target
        | None ->
            let dir = AppPaths.ensureDir (AppPaths.dbDir adapter.GameId)
            Path.Combine(dir, dbName + ".db")

    // The explicit game-dir arg is recorded as-is; when absent, refresh later
    // re-auto-detects so a relocated game install still resolves.
    let explicitGameDir = args.TryGetResult IndexArgs.Game_Dir

    match Discovery.resolveGameDir adapter probe explicitGameDir with
    | Result.Error e ->
        eprintfn "game dir: %s" e
        ExitGameNotFound
    | Result.Ok gameDir ->

    let explicitMods = args.GetResults IndexArgs.Mod
    let workshopIds = args.GetResults IndexArgs.Workshop_Id
    let playset = args.TryGetResult IndexArgs.Playset
    let auto = args.Contains IndexArgs.Auto_Mods

    let mods, warnings = resolveMods probe explicitMods workshopIds playset auto adapter
    warnings |> List.iter (eprintfn "warning: %s")

    let languages =
        match args.TryGetResult Languages with
        | Some s -> s.Split(',') |> Array.map (fun x -> x.Trim().ToLowerInvariant()) |> Array.filter ((<>) "") |> List.ofArray
        | None -> []

    // Persist the original source selection so `refresh` can replay it.
    let extraMeta = IndexInputs.toExtraMeta explicitGameDir explicitMods workshopIds playset auto

    runIndexCore
        adapter
        verbose
        showProgress
        gameDir
        mods
        configDir
        dbPath
        dbName
        (args.Contains Skip_Generic)
        (not (args.Contains No_Fts))
        languages
        true
        extraMeta

// Re-index one registered database (or all) in place. Each index's original
// source selection was persisted to its meta at `index` time, so we replay it
// through the same resolvers: workshop ids are re-located, a playset is
// re-expanded (new mod list / order / enabled state), and auto-discovery re-runs
// — so a game or mod update, or a relocated install, is picked up automatically.
let private runRefresh (args: ParseResults<RefreshArgs>) =
    let verbose = args.Contains RefreshArgs.Verbose
    let showProgress = args.Contains RefreshArgs.Progress
    let probe = Discovery.realProbe
    let config = Registry.load ()

    // Target set: the named index, or every registered (SQLite) index. Postgres
    // targets aren't registered, so they are never refreshed here.
    let targets =
        match args.TryGetResult RefreshArgs.Name with
        | Some name ->
            match Registry.findByName name config with
            | Some e -> Result.Ok [ e ]
            | None -> Result.Error name
        | None -> Result.Ok(List.ofArray config.Databases)

    match targets with
    | Result.Error name ->
        eprintfn "no registered index named '%s' (see 'eu4indexer list')" name
        ExitUsage
    | Result.Ok [] ->
        printfn "No indexes registered. Build one with 'eu4indexer index'."
        ExitOk
    | Result.Ok entries ->
        // Refresh each in turn; one failure is reported and skipped, not fatal.
        let refreshOne (e: Registry.DbEntry) : Result<unit, string> =
            if not (File.Exists e.Path) then
                Result.Error(sprintf "database file missing: %s" e.Path)
            else

            let inputs = IndexInputs.read e.Path
            let adapter = resolveAdapter (Some inputs.GameId)

            // Prefer the recorded config dir; fall back to the install default if
            // it moved or was wiped.
            let configDir =
                if inputs.ConfigDir <> "" && Directory.Exists inputs.ConfigDir then
                    inputs.ConfigDir
                else
                    defaultConfigDir adapter.GameId |> Option.defaultValue ""

            if configDir = "" || not (Directory.Exists configDir) then
                Result.Error(sprintf "config dir not found (was '%s'); run 'eu4indexer setup'" inputs.ConfigDir)
            else

            match Discovery.resolveGameDir adapter probe inputs.ExplicitGameDir with
            | Result.Error ge -> Result.Error(sprintf "game dir: %s" ge)
            | Result.Ok gameDir ->
                let mods, warnings =
                    resolveMods probe inputs.ExplicitMods inputs.WorkshopIds inputs.Playset inputs.AutoMods adapter

                warnings |> List.iter (eprintfn "warning: %s")

                let extraMeta =
                    IndexInputs.toExtraMeta
                        inputs.ExplicitGameDir
                        inputs.ExplicitMods
                        inputs.WorkshopIds
                        inputs.Playset
                        inputs.AutoMods

                printfn "Refreshing '%s' (%s)..." e.Name e.Game

                let code =
                    runIndexCore
                        adapter
                        verbose
                        showProgress
                        gameDir
                        mods
                        configDir
                        e.Path
                        e.Name
                        inputs.SkipGeneric
                        inputs.WithFts
                        inputs.Languages
                        false
                        extraMeta

                // 0 = clean, 4 = built with FK violations (already warned). Both
                // mean the database was rebuilt; anything else is a real failure.
                if code = ExitOk || code = 4 then Result.Ok() else Result.Error "indexing failed"

        let results = entries |> List.map (fun e -> e.Name, refreshOne e)
        let failures = results |> List.choose (fun (n, r) -> match r with Result.Error m -> Some(n, m) | _ -> None)

        for n, m in failures do
            eprintfn "warning: refresh '%s' failed: %s" n m

        printfn "Refreshed %d/%d index(es)." (results.Length - failures.Length) results.Length
        if failures.IsEmpty then ExitOk else ExitConfigInvalid

let private runWorkshop (args: ParseResults<WorkshopArgs>) =
    let adapter = resolveAdapter (args.TryGetResult WorkshopArgs.Game)
    let probe = Discovery.realProbe
    let filter = args.TryGetResult WorkshopArgs.Ids |> Option.defaultValue []

    let items = Discovery.discoverWorkshopItems adapter probe

    let shown =
        if filter.IsEmpty then items else items |> List.filter (fun w -> List.contains w.WorkshopId filter)

    if shown.IsEmpty then
        eprintfn "no Steam Workshop items found for %s" adapter.GameId
        ExitOk
    else
        printfn "Workshop items (%d):" shown.Length
        shown |> List.iter (fun w -> printfn "  %-12s %s" w.WorkshopId w.Name)
        ExitOk

let private runPlayset (args: ParseResults<PlaysetArgs>) =
    let adapter = resolveAdapter (args.TryGetResult PlaysetArgs.Game)
    let probe = Discovery.realProbe

    match Discovery.launcherDbPath adapter probe with
    | None ->
        eprintfn "launcher database (launcher-v2.sqlite) not found in the Paradox user-data dir"
        ExitGameNotFound
    | Some dbPath ->
        // positional tokens are joined so playset names with spaces work unquoted
        let query =
            args.TryGetResult PlaysetArgs.Name
            |> Option.map (String.concat " ")
            |> Option.filter (fun s -> s <> "")

        match query with
        | None ->
            let playsets = Launcher.listPlaysets dbPath
            printfn "Playsets (%d) [* = active]:" playsets.Length
            playsets |> List.iter (fun p -> printfn "  %s %s" (if p.IsActive then "*" else " ") p.Name)
            ExitOk
        | Some q ->
            match Launcher.findPlayset dbPath q with
            | None ->
                eprintfn "playset '%s' not found" q
                ExitUsage
            | Some ps ->
                let mods = Launcher.playsetMods dbPath ps.Id
                let enabledCount = mods |> List.filter (fun m -> m.Enabled) |> List.length

                printfn
                    "Playset '%s'%s — %d mods (%d enabled) [x = enabled]:"
                    ps.Name
                    (if ps.IsActive then " (active)" else "")
                    mods.Length
                    enabledCount

                mods
                |> List.iter (fun m ->
                    printfn
                        "  [%s] %-12s %s"
                        (if m.Enabled then "x" else " ")
                        (m.SteamId |> Option.defaultValue "-")
                        m.Name)

                ExitOk

let private runServe (args: ParseResults<ServeArgs>) =
    // Database precedence: explicit --db, then the registry's active index,
    // then $EU4_DB (handled inside the MCP host when no path is passed).
    match args.TryGetResult ServeArgs.Db |> Option.orElse (Registry.activePath ()) with
    | Some path -> Eu4Indexer.Mcp.McpServer.RunWithDatabaseAsync(path, [||]).GetAwaiter().GetResult()
    | None -> Eu4Indexer.Mcp.McpServer.RunAsync([||]).GetAwaiter().GetResult()

let private runSetup (args: ParseResults<SetupArgs>) =
    let refOverride = args.TryGetResult SetupArgs.Ref

    let fetchOne gameId =
        Setup.fetchConfig gameId refOverride (eprintfn "%s")

    match args.TryGetResult SetupArgs.Game with
    | Some gameId ->
        match fetchOne gameId with
        | Result.Ok _ ->
            printfn "Config ready. Build an index with 'eu4indexer index --game %s'." gameId
            ExitOk
        | Result.Error e ->
            eprintfn "error: %s" e
            ExitConfigInvalid
    | None ->
        let results =
            GameAdapter.allAdapters
            |> List.map (fun a ->
                eprintfn "--- %s ---" a.GameId
                a.GameId, fetchOne a.GameId)

        let failures = results |> List.choose (fun (g, r) -> match r with Result.Error e -> Some(g, e) | _ -> None)
        let okCount = results.Length - failures.Length

        for g, e in failures do
            eprintfn "warning: failed to download config for %s: %s" g e

        if okCount = 0 then
            eprintfn "error: no configs were downloaded."
            ExitConfigInvalid
        else
            printfn "Configs ready (%d/%d). Build an index with 'eu4indexer index'." okCount results.Length
            ExitOk

// Locate the bundled skill. Walk up from the binary's own directory looking for
// skills/eu4-indexer — this finds it in both the packaged layout (skills next to
// or just above the binary) and a source checkout (running the build apphost,
// e.g. bin/Debug/net9.0/eu4indexer, walks up to the repo root). Falls back to the
// current directory.
let private resolveSkillSrc () =
    let rec walkUp (dir: DirectoryInfo) =
        if isNull dir then
            None
        else
            let candidate = Path.Combine(dir.FullName, "skills", "eu4-indexer")
            if Directory.Exists candidate then Some candidate else walkUp dir.Parent

    walkUp (DirectoryInfo AppContext.BaseDirectory)
    |> Option.orElse (
        let cwd = Path.Combine(Directory.GetCurrentDirectory(), "skills", "eu4-indexer")
        if Directory.Exists cwd then Some cwd else None)
    |> Option.defaultValue (Path.Combine(AppContext.BaseDirectory, "skills", "eu4-indexer"))

let private runInstall (args: ParseResults<InstallArgs>) =
    let agents =
        match args.TryGetResult InstallArgs.Agents with
        | Some csv -> csv.Split(',') |> Array.map (fun s -> s.Trim()) |> Array.filter ((<>) "") |> List.ofArray
        | None -> [ "claude"; "codex" ]

    let lang =
        match args.TryGetResult InstallArgs.Language with
        | Some l -> l.Trim().ToLowerInvariant()
        | None -> "en"

    let results = AgentInstall.run agents (resolveSkillSrc ()) lang

    results
    |> List.iter (fun r ->
        let mark = if r.Ok then "ok" else "FAILED"
        printfn "  [%s] %s: %s" mark r.Agent r.Message)

    if results |> List.forall (fun r -> r.Ok) then ExitOk else ExitConfigInvalid

let private runUse (args: ParseResults<UseArgs>) =
    let name = args.GetResult UseArgs.Name

    if Registry.setActive name then
        printfn "Active index set to '%s'." name
        ExitOk
    else
        eprintfn "no registered index named '%s' (see 'eu4indexer list')" name
        ExitUsage

let private runList (_: ParseResults<ListArgs>) =
    let config = Registry.load ()

    if config.Databases.Length = 0 then
        printfn "No indexes registered. Build one with 'eu4indexer index'."
    else
        printfn "Indexes (%d) [* = active]:" config.Databases.Length

        config.Databases
        |> Array.iter (fun e ->
            let marker = if e.Name = config.ActiveDb then "*" else " "
            printfn "  %s %-16s %-6s %d sources  %s" marker e.Name e.Game e.Sources e.Path)

    ExitOk

// Update any installed config whose pinned ref has drifted from this build's.
// Runs under the (possibly just-swapped) binary, so its compiled refs are the
// ones compared against what's on disk.
let private runFinishConfig () =
    let stale = Setup.allGameIds |> List.filter Setup.isStale

    if stale.IsEmpty then
        printfn "Config rules are up to date."
        ExitOk
    else
        let results = stale |> List.map (fun g -> g, Setup.fetchConfig g None (eprintfn "%s"))
        let failures = results |> List.choose (fun (g, r) -> match r with Result.Error e -> Some(g, e) | _ -> None)

        for g, e in failures do
            eprintfn "warning: failed to update %s config: %s" g e

        printfn "Updated config for %d/%d game(s)." (results.Length - failures.Length) results.Length
        if failures.IsEmpty then ExitOk else ExitConfigInvalid

let private runUpdate (args: ParseResults<UpdateArgs>) =
    if args.Contains Finish_Config then
        runFinishConfig ()
    else

    let current = AppInfo.Version

    match Updater.latestVersion () with
    | Result.Error e ->
        eprintfn "error: %s" e
        ExitConfigInvalid
    | Result.Ok latest ->

    let updateAvailable = Updater.isNewer latest current

    if args.Contains Check then
        printfn "Current: %s. Latest: %s." current latest
        printfn "%s" (if updateAvailable then "An update is available. Run 'eu4indexer update'." else "Up to date.")
        ExitOk
    elif not updateAvailable && not (args.Contains Force) then
        printfn "Already up to date (%s)." current
        ExitOk
    else
        printfn "Updating %s -> %s..." current latest

        match Updater.applyBinaryUpdate (eprintfn "%s") with
        | Result.Error e ->
            eprintfn "error: %s" e
            ExitConfigInvalid
        | Result.Ok Updater.Deferred ->
            // Windows: the detached helper swaps and relaunches --finish-config.
            printfn "Binary update staged; it will complete once this process exits."
            ExitOk
        | Result.Ok Updater.Applied ->
            // Unix: binary is swapped in place. Run the new binary to update any
            // config whose pinned ref changed in this release.
            printfn "Binary updated. Updating config rules..."
            let exe = Path.Combine(AppPaths.binDir (), "eu4indexer")

            try
                let psi = Diagnostics.ProcessStartInfo(exe)
                psi.ArgumentList.Add "update"
                psi.ArgumentList.Add "--finish-config"
                psi.UseShellExecute <- false
                use p = Diagnostics.Process.Start psi
                p.WaitForExit()
            with ex ->
                eprintfn "warning: config update failed: %s (run 'eu4indexer setup' manually)" ex.Message

            printfn "Update complete."
            ExitOk

let private runVersion (_: ParseResults<VersionArgs>) =
    printfn "eu4indexer %s" AppInfo.Version
    ExitOk

[<EntryPoint>]
let main argv =
    // Keep CJK mod names / localisation legible on legacy Windows consoles.
    Console.OutputEncoding <- Text.Encoding.UTF8

    let parser =
        ArgumentParser.Create<CliArgs>(programName = "eu4indexer", errorHandler = ProcessExiter())

    try
        let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)

        match results.GetSubCommand() with
        | Index indexArgs -> runIndex indexArgs
        | Refresh refreshArgs -> runRefresh refreshArgs
        | Detect detectArgs -> runDetect detectArgs
        | Workshop workshopArgs -> runWorkshop workshopArgs
        | Playset playsetArgs -> runPlayset playsetArgs
        | Serve serveArgs -> runServe serveArgs
        | Setup setupArgs -> runSetup setupArgs
        | Install installArgs -> runInstall installArgs
        | Use useArgs -> runUse useArgs
        | List listArgs -> runList listArgs
        | Update updateArgs -> runUpdate updateArgs
        | Version versionArgs -> runVersion versionArgs
    with
    | :? ArguParseException as ex ->
        printfn "%s" ex.Message
        ExitUsage

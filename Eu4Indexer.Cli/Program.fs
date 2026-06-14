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
    | [<Mandatory; AltCommandLine("-o")>] Db of path: string
    | Languages of langs: string
    | Skip_Generic
    | No_Fts
    | Verbose

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Game_Dir _ -> "base game directory (if omitted, auto-detected via Steam libraryfolders.vdf)"
            | Mod _ -> "mod directory or .mod descriptor; repeatable, order = load order"
            | Workshop_Id _ -> "Steam Workshop item id to include as a mod; repeatable (see the 'workshop' command)"
            | Playset _ -> "launcher playset name or id; indexes its enabled mods in load order (see the 'playset' command)"
            | Auto_Mods -> "auto-discover enabled mods from the launcher / mod dir"
            | Config_Dir _ -> "cwtools config repo dir (default: $EU4_CONFIG_DIR)"
            | Db _ ->
                "output target (overwritten): a SQLite file path, or a PostgreSQL "
                + "connection string ('Host=...;Database=...;Username=...;Password=...' "
                + "or postgres://user:pass@host/db)"
            | Languages _ -> "comma-separated localisation languages (default: all)"
            | Skip_Generic -> "index only events/missions/decisions/modifiers"
            | No_Fts -> "skip building full-text search tables"
            | Verbose -> "print per-stage progress"

type DetectArgs =
    | [<AltCommandLine("-g")>] Game_Dir of path: string
    | [<AltCommandLine("-m")>] Mod of path: string
    | [<AltCommandLine("-w")>] Workshop_Id of id: string
    | [<AltCommandLine("-p")>] Playset of name_or_id: string
    | Auto_Mods

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Game_Dir _ -> "base game directory (if omitted, auto-detected via Steam libraryfolders.vdf)"
            | Mod _ -> "mod directory or .mod descriptor; repeatable"
            | Workshop_Id _ -> "Steam Workshop item id to include as a mod; repeatable"
            | Playset _ -> "launcher playset name or id; resolves its enabled mods"
            | Auto_Mods -> "auto-discover enabled mods"

type WorkshopArgs =
    | [<MainCommand>] Ids of workshop_id: string list

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Ids _ -> "workshop ids to show (default: all installed)"

type PlaysetArgs =
    | [<MainCommand>] Name of name_or_id: string list

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Name _ -> "playset name or id to show mods for (default: list all playsets)"

type CliArgs =
    | [<CliPrefix(CliPrefix.None)>] Index of ParseResults<IndexArgs>
    | [<CliPrefix(CliPrefix.None)>] Detect of ParseResults<DetectArgs>
    | [<CliPrefix(CliPrefix.None)>] Workshop of ParseResults<WorkshopArgs>
    | [<CliPrefix(CliPrefix.None)>] Playset of ParseResults<PlaysetArgs>

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Index _ -> "parse game + mods and write the SQLite index"
            | Detect _ -> "show resolved game dir, mods, and predicted file overrides"
            | Workshop _ -> "list installed Steam Workshop items (id and mod name)"
            | Playset _ -> "list launcher playsets, or the mods of one playset"

let private defaultConfigDir () =
    match Environment.GetEnvironmentVariable "EU4_CONFIG_DIR" with
    | null | "" -> None
    | dir -> Some dir

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
    let adapter = GameAdapter.eu4
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

let private runIndex (args: ParseResults<IndexArgs>) =
    let adapter = GameAdapter.eu4
    let probe = Discovery.realProbe
    let verbose = args.Contains Verbose
    let log (msg: string) = if verbose then eprintfn "%s" msg

    let configDir =
        match args.TryGetResult Config_Dir |> Option.orElse (defaultConfigDir ()) with
        | Some dir -> dir
        | None -> ""

    if configDir = "" || not (Directory.Exists configDir) then
        eprintfn "config dir not found; pass --config-dir or set EU4_CONFIG_DIR"
        ExitConfigInvalid
    else

    match Discovery.resolveGameDir adapter probe (args.TryGetResult IndexArgs.Game_Dir) with
    | Result.Error e ->
        eprintfn "game dir: %s" e
        ExitGameNotFound
    | Result.Ok gameDir ->

    let mods, warnings =
        resolveMods
            probe
            (args.GetResults IndexArgs.Mod)
            (args.GetResults IndexArgs.Workshop_Id)
            (args.TryGetResult IndexArgs.Playset)
            (args.Contains IndexArgs.Auto_Mods)
            adapter

    warnings |> List.iter (eprintfn "warning: %s")

    let languages =
        match args.TryGetResult Languages with
        | Some s -> s.Split(',') |> Array.map (fun x -> x.Trim().ToLowerInvariant()) |> Array.filter ((<>) "") |> List.ofArray
        | None -> []

    let request: Pipeline.IndexRequest =
        { Adapter = adapter
          GameDir = gameDir
          Mods = mods
          ConfigDir = configDir
          DbPath = args.GetResult Db
          SkipGeneric = args.Contains Skip_Generic
          WithFts = not (args.Contains No_Fts)
          Languages = languages
          Log = log }

    match Pipeline.run request with
    | Result.Error e ->
        eprintfn "error: %s" e
        ExitConfigInvalid
    | Result.Ok report ->
        printfn "Indexed %d sources, %d files, %d entities (%d effective), %d loc entries."
            report.SourceCount report.FileCount report.EntityCount report.EffectiveEntityCount report.LocCount
        printfn "Overrides: %d. Parse errors: %d. FK violations: %d."
            report.OverrideCount report.ParseErrorCount report.ForeignKeyViolations

        if report.ForeignKeyViolations > 0 then 4 else ExitOk

let private runWorkshop (args: ParseResults<WorkshopArgs>) =
    let adapter = GameAdapter.eu4
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
    let adapter = GameAdapter.eu4
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

[<EntryPoint>]
let main argv =
    let parser =
        ArgumentParser.Create<CliArgs>(programName = "eu4indexer", errorHandler = ProcessExiter())

    try
        let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)

        match results.GetSubCommand() with
        | Index indexArgs -> runIndex indexArgs
        | Detect detectArgs -> runDetect detectArgs
        | Workshop workshopArgs -> runWorkshop workshopArgs
        | Playset playsetArgs -> runPlayset playsetArgs
    with
    | :? ArguParseException as ex ->
        printfn "%s" ex.Message
        ExitUsage

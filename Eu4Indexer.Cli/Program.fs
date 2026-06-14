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
            | Game_Dir _ -> "base game directory (auto-detected if omitted)"
            | Mod _ -> "mod directory or .mod descriptor; repeatable, order = load order"
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
    | Auto_Mods

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Game_Dir _ -> "base game directory (auto-detected if omitted)"
            | Mod _ -> "mod directory or .mod descriptor; repeatable"
            | Auto_Mods -> "auto-discover enabled mods"

type CliArgs =
    | [<CliPrefix(CliPrefix.None)>] Index of ParseResults<IndexArgs>
    | [<CliPrefix(CliPrefix.None)>] Detect of ParseResults<DetectArgs>

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Index _ -> "parse game + mods and write the SQLite index"
            | Detect _ -> "show resolved game dir, mods, and predicted file overrides"

let private defaultConfigDir () =
    match Environment.GetEnvironmentVariable "EU4_CONFIG_DIR" with
    | null | "" -> None
    | dir -> Some dir

let private resolveMods (probe: Discovery.FsProbe) (explicit: string list) (auto: bool) (adapter: GameAdapter) =
    let explicitMods, explicitWarnings =
        explicit
        |> List.fold
            (fun (mods, warns) path ->
                match Discovery.resolveExplicitMod probe path with
                | Result.Ok m -> mods @ [ m ], warns
                | Result.Error e -> mods, warns @ [ e ])
            ([], [])

    let autoMods, autoWarnings =
        if auto then Discovery.discoverMods adapter probe else [], []

    explicitMods @ autoMods, explicitWarnings @ autoWarnings

let private runDetect (args: ParseResults<DetectArgs>) =
    let adapter = GameAdapter.eu4
    let probe = Discovery.realProbe
    let explicitGameDir = args.TryGetResult DetectArgs.Game_Dir
    let explicitMods = args.GetResults DetectArgs.Mod
    let auto = args.Contains DetectArgs.Auto_Mods

    match Discovery.resolveGameDir adapter probe explicitGameDir with
    | Result.Error e ->
        eprintfn "game dir: %s" e
        ExitGameNotFound
    | Result.Ok gameDir ->
        printfn "Game dir: %s" gameDir
        let mods, warnings = resolveMods probe explicitMods auto adapter

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
        resolveMods probe (args.GetResults IndexArgs.Mod) (args.Contains IndexArgs.Auto_Mods) adapter

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

[<EntryPoint>]
let main argv =
    let parser =
        ArgumentParser.Create<CliArgs>(programName = "eu4indexer", errorHandler = ProcessExiter())

    try
        let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)

        match results.GetSubCommand() with
        | Index indexArgs -> runIndex indexArgs
        | Detect detectArgs -> runDetect detectArgs
    with
    | :? ArguParseException as ex ->
        printfn "%s" ex.Message
        ExitUsage

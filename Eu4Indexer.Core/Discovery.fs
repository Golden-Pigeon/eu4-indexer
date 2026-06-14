namespace Eu4Indexer.Core

/// Resolves where the base game and mods live. All filesystem access goes
/// through an injected probe so detection logic is testable on machines
/// without Steam or the game installed.
module Discovery =

    open System.IO

    type FsProbe =
        { DirExists: string -> bool
          FileExists: string -> bool
          /// dir -> glob pattern -> matching file paths (non-recursive)
          ListFiles: string -> string -> string list
          /// dir -> subdirectory paths
          ListDirs: string -> string list
          ReadAllText: string -> string }

    let realProbe: FsProbe =
        { DirExists = Directory.Exists
          FileExists = File.Exists
          ListFiles =
            fun dir pattern ->
                if Directory.Exists dir then
                    Directory.EnumerateFiles(dir, pattern) |> List.ofSeq |> List.sort
                else
                    []
          ListDirs =
            fun dir ->
                if Directory.Exists dir then
                    Directory.EnumerateDirectories dir |> List.ofSeq |> List.sort
                else
                    []
          ReadAllText = File.ReadAllText }

    /// A mod whose descriptor parsed and whose content directory exists.
    type DiscoveredMod =
        { DescriptorPath: string option
          ContentPath: string
          Info: ModDescriptorInfo }

    /// Validates that a directory looks like the game (has the expected subdirs).
    let private isGameDir (adapter: GameAdapter) (probe: FsProbe) (dir: string) =
        probe.DirExists dir
        && adapter.ValidationSubdirs
           |> List.forall (fun sub -> probe.DirExists(Path.Combine(dir, sub)))

    /// Library-folder paths declared in a Steam `libraryfolders.vdf`. Handles
    /// the modern format (`"path"  "<dir>"` inside numbered blocks) and the
    /// legacy format (`"<n>"  "<dir>"`). Returned paths are library roots (the
    /// dirs that contain `steamapps`), with `\\` unescaped. App-id/size pairs in
    /// the legacy `apps` block are filtered out by requiring a path-like value.
    let parseSteamLibraryPaths (vdf: string) : string list =
        let unescape (s: string) = s.Replace("\\\\", "\\")
        let looksLikePath (s: string) = s.Contains '\\' || s.Contains '/' || s.Contains ':'

        let matchPaths pattern =
            System.Text.RegularExpressions.Regex.Matches(vdf, pattern)
            |> Seq.map (fun m -> m.Groups[1].Value)

        let modern = matchPaths "\"path\"\\s+\"([^\"]+)\""
        let legacy = matchPaths "(?m)^\\s*\"\\d+\"\\s+\"([^\"]+)\""

        Seq.append modern legacy
        |> Seq.map unescape
        |> Seq.filter looksLikePath
        |> Seq.distinct
        |> List.ofSeq

    /// All Steam library roots (dirs that contain a `steamapps` folder),
    /// discovered from each client dir's libraryfolders.vdf (so libraries on
    /// non-default drives are found), plus the client dirs themselves as a
    /// fallback when no vdf is present.
    let steamLibraryRoots (adapter: GameAdapter) (probe: FsProbe) : string list =
        let clientDirs = adapter.SteamClientDirs()

        let fromVdf =
            clientDirs
            |> List.collect (fun client ->
                [ Path.Combine(client, "steamapps", "libraryfolders.vdf")
                  Path.Combine(client, "config", "libraryfolders.vdf") ]
                |> List.filter probe.FileExists
                |> List.collect (fun vdf ->
                    try
                        parseSteamLibraryPaths (probe.ReadAllText vdf)
                    with _ ->
                        []))

        (clientDirs @ fromVdf) |> List.distinct

    /// All candidate game-install dirs across every Steam library.
    let private steamGameDirCandidates (adapter: GameAdapter) (probe: FsProbe) =
        steamLibraryRoots adapter probe
        |> List.map (fun root -> Path.Combine(root, "steamapps", "common", adapter.SteamGameDir))
        |> List.distinct

    /// Resolve the base game directory: explicit value (validated) or probe
    /// the platform's Steam conventions.
    let resolveGameDir (adapter: GameAdapter) (probe: FsProbe) (explicitDir: string option) =
        match explicitDir with
        | Some dir ->
            if isGameDir adapter probe dir then
                Result.Ok dir
            else
                Result.Error(
                    sprintf
                        "'%s' does not look like a %s install (missing %s)"
                        dir
                        adapter.GameId
                        (String.concat "/" adapter.ValidationSubdirs)
                )
        | None ->
            steamGameDirCandidates adapter probe
            |> List.tryFind (isGameDir adapter probe)
            |> function
                | Some dir -> Result.Ok dir
                | None ->
                    Result.Error(
                        "game directory not found via Steam libraries (libraryfolders.vdf); pass it explicitly with --game-dir"
                    )

    /// Resolve one explicitly-given mod: either a content directory (containing
    /// descriptor.mod) or a path to a .mod descriptor file.
    let resolveExplicitMod (probe: FsProbe) (path: string) : Result<DiscoveredMod, string> =
        let fromDescriptor descriptorPath (contentFallback: string option) =
            match ModDescriptor.parseText (Path.GetFileName(descriptorPath: string)) (probe.ReadAllText descriptorPath) with
            | Result.Error e -> Result.Error(sprintf "failed to parse %s: %s" descriptorPath e)
            | Result.Ok info ->
                match info.Archive with
                | Some a -> Result.Error(sprintf "mod '%s' is an archive (%s); unzip it first" info.Name a)
                | None ->
                    let contentPath =
                        match info.Path with
                        | Some p when probe.DirExists p -> Some p
                        | Some p ->
                            // relative 'path=' entries are relative to the dir holding the descriptor
                            let candidate = Path.Combine(Path.GetDirectoryName(descriptorPath: string), p)
                            if probe.DirExists candidate then Some candidate else contentFallback
                        | None -> contentFallback

                    match contentPath with
                    | Some cp ->
                        Result.Ok
                            { DescriptorPath = Some descriptorPath
                              ContentPath = cp
                              Info = info }
                    | None -> Result.Error(sprintf "mod '%s': content directory not found" info.Name)

        if probe.DirExists path then
            let descriptor = Path.Combine(path, "descriptor.mod")

            if probe.FileExists descriptor then
                fromDescriptor descriptor (Some path)
            else
                // bare content dir without descriptor: accept with minimal info
                Result.Ok
                    { DescriptorPath = None
                      ContentPath = path
                      Info =
                        { Name = Path.GetFileName(path.TrimEnd('/', '\\'))
                          Version = None
                          SupportedVersion = None
                          RemoteFileId = None
                          Picture = None
                          Path = None
                          Archive = None
                          Tags = []
                          Dependencies = []
                          ReplacePaths = [] } }
        elif probe.FileExists path then
            fromDescriptor path None
        else
            Result.Error(sprintf "mod path not found: %s" path)

    /// Auto-discover mods from the user-data dir: prefers dlc_load.json
    /// (the launcher's enabled-mods list, in load order); falls back to all
    /// mod/*.mod descriptors in alphabetical order.
    let discoverMods (adapter: GameAdapter) (probe: FsProbe) : DiscoveredMod list * string list =
        let userDataDir =
            adapter.ModDirCandidates() |> List.tryFind (fun d -> probe.DirExists(Path.Combine(d, "mod")))

        match userDataDir with
        | None -> [], [ "no Paradox user data dir with a mod/ folder found; pass mods explicitly with --mod" ]
        | Some dataDir ->
            let descriptorPaths =
                let fromDlcLoad =
                    let dlcLoad = Path.Combine(dataDir, "dlc_load.json")

                    if probe.FileExists dlcLoad then
                        try
                            let json = System.Text.Json.JsonDocument.Parse(probe.ReadAllText dlcLoad)

                            match json.RootElement.TryGetProperty "enabled_mods" with
                            | true, arr ->
                                arr.EnumerateArray()
                                |> Seq.choose (fun e ->
                                    let rel = e.GetString()
                                    if isNull rel then None else Some(Path.Combine(dataDir, rel)))
                                |> List.ofSeq
                                |> Some
                            | _ -> None
                        with _ ->
                            None
                    else
                        None

                match fromDlcLoad with
                | Some paths when not paths.IsEmpty -> paths
                | _ -> probe.ListFiles (Path.Combine(dataDir, "mod")) "*.mod"

            let results, warnings =
                descriptorPaths
                |> List.fold
                    (fun (mods, warns) descriptorPath ->
                        if not (probe.FileExists descriptorPath) then
                            mods, sprintf "descriptor not found: %s" descriptorPath :: warns
                        else
                            match resolveExplicitMod probe descriptorPath with
                            | Result.Ok m -> m :: mods, warns
                            | Result.Error e -> mods, e :: warns)
                    ([], [])

            List.rev results, List.rev warnings

    /// A Steam Workshop item for the game: numeric id, content directory, and
    /// mod name. The name is a cheap read of descriptor.mod only — mod contents
    /// are not enumerated or parsed.
    type WorkshopItem =
        { WorkshopId: string
          ContentPath: string
          Name: string }

    /// The workshop content dirs (`steamapps/workshop/content/<appid>`) across
    /// all Steam libraries.
    let private workshopContentDirs (adapter: GameAdapter) (probe: FsProbe) =
        steamLibraryRoots adapter probe
        |> List.map (fun root -> Path.Combine(root, "steamapps", "workshop", "content", adapter.SteamAppId))
        |> List.distinct

    /// Enumerate installed Steam Workshop items, reading each descriptor.mod
    /// name only (no content parsing). Sorted by name.
    let discoverWorkshopItems (adapter: GameAdapter) (probe: FsProbe) : WorkshopItem list =
        workshopContentDirs adapter probe
        |> List.collect probe.ListDirs
        |> List.choose (fun itemDir ->
            let workshopId = Path.GetFileName(itemDir.TrimEnd('/', '\\'))

            // workshop item dirs are numeric ids; skip anything else
            if workshopId = "" || not (workshopId |> Seq.forall System.Char.IsDigit) then
                None
            else
                let descriptor = Path.Combine(itemDir, "descriptor.mod")

                let name =
                    if probe.FileExists descriptor then
                        match ModDescriptor.parseText "descriptor.mod" (probe.ReadAllText descriptor) with
                        | Result.Ok info -> info.Name
                        | Result.Error _ -> workshopId
                    else
                        workshopId

                Some
                    { WorkshopId = workshopId
                      ContentPath = itemDir
                      Name = name })
        |> List.sortBy (fun w -> w.Name)

    /// Resolve a workshop id to a mod by locating its content dir across all
    /// Steam libraries, then resolving its descriptor.
    let resolveWorkshopMod (adapter: GameAdapter) (probe: FsProbe) (workshopId: string) : Result<DiscoveredMod, string> =
        workshopContentDirs adapter probe
        |> List.map (fun content -> Path.Combine(content, workshopId))
        |> List.tryFind probe.DirExists
        |> function
            | Some dir -> resolveExplicitMod probe dir
            | None -> Result.Error(sprintf "workshop item %s not found in any Steam library" workshopId)

    /// Path to the launcher's playset db (launcher-v2.sqlite), if a Paradox
    /// user-data dir holding one is present.
    let launcherDbPath (adapter: GameAdapter) (probe: FsProbe) : string option =
        adapter.ModDirCandidates()
        |> List.map (fun d -> Path.Combine(d, "launcher-v2.sqlite"))
        |> List.tryFind probe.FileExists

    /// Resolve one launcher playset mod row to a DiscoveredMod. Prefers the
    /// absolute dirPath, then the Workshop id, then the descriptor relative to
    /// the user-data dir.
    let private resolvePlaysetMod
        (adapter: GameAdapter)
        (probe: FsProbe)
        (dataDir: string)
        (m: Launcher.PlaysetMod)
        : Result<DiscoveredMod, string> =
        let viaDescriptor () =
            match m.Descriptor with
            | Some rel -> resolveExplicitMod probe (Path.Combine(dataDir, rel))
            | None -> Result.Error(sprintf "mod '%s': no resolvable path (unsubscribed?)" m.Name)

        match m.DirPath with
        | Some d when probe.DirExists d -> resolveExplicitMod probe d
        | _ ->
            match m.SteamId |> Option.map (resolveWorkshopMod adapter probe) with
            | Some(Result.Ok r) -> Result.Ok r
            | _ -> viaDescriptor ()

    /// Resolve the *enabled* mods of a playset (by name or id) to DiscoveredMods
    /// in load order. Returns mods + per-mod warnings; Error if the launcher db
    /// or the named playset is missing.
    let resolvePlayset
        (adapter: GameAdapter)
        (probe: FsProbe)
        (nameOrId: string)
        : Result<DiscoveredMod list * string list, string> =
        match launcherDbPath adapter probe with
        | None -> Result.Error "launcher database (launcher-v2.sqlite) not found in the Paradox user-data dir"
        | Some dbPath ->
            match Launcher.findPlayset dbPath nameOrId with
            | None -> Result.Error(sprintf "playset '%s' not found" nameOrId)
            | Some ps ->
                let dataDir = Path.GetDirectoryName dbPath

                let mods, warnings =
                    Launcher.playsetMods dbPath ps.Id
                    |> List.filter (fun m -> m.Enabled)
                    |> List.fold
                        (fun (acc, warns) m ->
                            match resolvePlaysetMod adapter probe dataDir m with
                            | Result.Ok dm -> acc @ [ dm ], warns
                            | Result.Error e -> acc, warns @ [ e ])
                        ([], [])

                Result.Ok(mods, warnings)

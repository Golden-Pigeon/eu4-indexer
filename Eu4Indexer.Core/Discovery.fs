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

    let private readDescriptor (probe: FsProbe) (userDataDir: string option) descriptorPath contentFallback =
        let parsed =
            try ModDescriptor.parseText (Path.GetFileName(descriptorPath: string)) (probe.ReadAllText descriptorPath)
            with
            | :? IOException as ex -> Result.Error ex.Message
            | :? System.UnauthorizedAccessException as ex -> Result.Error ex.Message
        match parsed with
        | Result.Error e -> Result.Error(sprintf "failed to parse %s: %s" descriptorPath e)
        | Result.Ok info ->
            match info.Archive with
            | Some a -> Result.Error(sprintf "mod '%s' is an archive (%s); unzip it first" info.Name a)
            | None ->
                let contentPath =
                    match info.Path with
                    | Some p when userDataDir.IsNone && probe.DirExists p -> Some p
                    | Some p ->
                        let descriptorDir = Path.GetDirectoryName(descriptorPath: string)
                        [ yield! Option.toList userDataDir
                          yield descriptorDir ]
                        |> Seq.map (fun root -> Path.Combine(root, p))
                        |> Seq.tryFind probe.DirExists
                        |> Option.orElse contentFallback
                    | None -> contentFallback

                match contentPath with
                | Some cp ->
                    Result.Ok
                        { DescriptorPath = Some descriptorPath
                          ContentPath = cp
                          Info = info }
                | None -> Result.Error(sprintf "mod '%s': content directory not found" info.Name)

    /// Resolve one explicitly-given mod: either a content directory (containing
    /// descriptor.mod) or a path to a .mod descriptor file.
    let resolveExplicitMod (probe: FsProbe) (path: string) : Result<DiscoveredMod, string> =
        if probe.DirExists path then
            let descriptor = Path.Combine(path, "descriptor.mod")

            if probe.FileExists descriptor then
                readDescriptor probe None descriptor (Some path)
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
            readDescriptor probe None path None
        else
            Result.Error(sprintf "mod path not found: %s" path)

    let private collectResults results =
        List.foldBack (fun result (mods, errors) ->
            match result with
            | Result.Ok m -> m :: mods, errors
            | Result.Error e -> mods, e :: errors) results ([], [])

    let private pathComparer =
        if System.OperatingSystem.IsWindows() then System.StringComparer.OrdinalIgnoreCase
        else System.StringComparer.Ordinal

    // EU4 ranks a prerequisite above every mod that depends on it. Peeling off
    // the mods nobody depends on gives successive ranks, starting at zero.
    // The mount loop visits larger ranks first, then names in descending byte order.
    let private orderLauncherMods (adapter: GameAdapter) (mods: DiscoveredMod list, warnings: string list) =
        if adapter.GameId <> "eu4" then mods, warnings
        else
            let seen = System.Collections.Generic.HashSet<string>(pathComparer)
            let mods = mods |> List.filter (fun m -> seen.Add(Path.GetFullPath(defaultArg m.DescriptorPath m.ContentPath)))
            let duplicate = mods |> List.countBy (fun m -> m.Info.Name) |> List.tryFind (fun (_, count) -> count > 1)
            let nameBytes (m: DiscoveredMod) = System.Text.Encoding.UTF8.GetBytes(m.Info.Name) |> Array.toList
            let rec order sorted pending =
                match pending with
                | [] -> sorted, warnings
                | _ ->
                    let required = pending |> Seq.collect (fun m -> m.Info.Dependencies) |> Set.ofSeq
                    let leaves, rest = pending |> List.partition (fun m -> not (required.Contains m.Info.Name))
                    if leaves.IsEmpty then
                        let names = pending |> List.map (fun m -> m.Info.Name) |> String.concat ", "
                        [], warnings @ [ $"Cyclic EU4 mod dependencies prevent ordering: {names}" ]
                    else
                        let layer = leaves |> List.sortByDescending nameBytes
                        order (layer @ sorted) rest
            match duplicate with
            | Some(name, _) -> [], warnings @ [ $"Multiple enabled EU4 descriptors have the same mod name: {name}" ]
            | None -> order [] mods

    let private readAutoMods (adapter: GameAdapter) (probe: FsProbe) : DiscoveredMod list * string list =
        let isEu4 = adapter.GameId = "eu4"
        let candidates = adapter.ModDirCandidates()
        let hasManifest dir = probe.FileExists(Path.Combine(dir, "dlc_load.json"))
        let hasModDir dir = probe.DirExists(Path.Combine(dir, "mod"))
        let userDataDir =
            if isEu4 then candidates |> List.tryFind hasManifest |> Option.orElseWith (fun () -> List.tryFind hasModDir candidates)
            else List.tryFind hasModDir candidates

        match userDataDir with
        | None when isEu4 -> [], []
        | None -> [], [ "no Paradox user data dir with a mod/ folder found; pass mods explicitly with --mod" ]
        | Some dataDir ->
            let manifest = Path.Combine(dataDir, "dlc_load.json")
            let fallback () = if isEu4 then [] else probe.ListFiles (Path.Combine(dataDir, "mod")) "*.mod"
            let paths, warnings =
                if not (probe.FileExists manifest) then fallback (), []
                else
                    try
                        use json = System.Text.Json.JsonDocument.Parse(probe.ReadAllText manifest)
                        let enabledMods =
                            if isEu4 then
                                json.RootElement.EnumerateObject()
                                |> Seq.tryFind (fun field -> System.String.Equals(field.Name, "enabled_mods", System.StringComparison.OrdinalIgnoreCase))
                                |> Option.map (fun field -> field.Value)
                            else
                                match json.RootElement.TryGetProperty "enabled_mods" with
                                | true, value -> Some value
                                | _ -> None
                        match enabledMods with
                        | None -> fallback (), []
                        | Some arr ->
                            let enabled =
                                [ for item in arr.EnumerateArray() do
                                    let rel = item.GetString()
                                    if isEu4 && System.String.IsNullOrWhiteSpace rel then
                                        raise (System.FormatException("Expected a non-empty descriptor path in enabled_mods"))
                                    if not (isNull rel) then yield Path.Combine(dataDir, rel) ]
                            if not isEu4 && enabled.IsEmpty then fallback (), [] else enabled, []
                    with ex ->
                        if isEu4 then [], [ $"Cannot read enabled mods from {manifest}: {ex.Message}" ]
                        else fallback (), []
            let resolved =
                paths |> List.map (fun path ->
                    if not (probe.FileExists path) then Result.Error(sprintf "descriptor not found: %s" path)
                    elif isEu4 then readDescriptor probe (Some dataDir) path None
                    else resolveExplicitMod probe path)
            let mods, errors = collectResults resolved
            mods, warnings @ errors

    let discoverMods adapter probe = readAutoMods adapter probe |> orderLauncherMods adapter

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

    /// EU4 reads user-data .mod descriptors. A cached Workshop directory alone
    /// cannot establish the name, dependencies or content path used by the game.
    let private resolvePlaysetMod
        (adapter: GameAdapter)
        (probe: FsProbe)
        (dataDir: string)
        (m: Launcher.PlaysetMod)
        : Result<DiscoveredMod, string> =
        if adapter.GameId = "eu4" then
            let descriptor =
                m.Descriptor
                |> Option.orElseWith (fun () -> m.SteamId |> Option.map (fun id -> Path.Combine("mod", $"ugc_{id}.mod")))
            match descriptor with
            | None -> Result.Error(sprintf "mod '%s': no launcher descriptor" m.Name)
            | Some rel ->
                let path = Path.Combine(dataDir, rel)
                if probe.FileExists path then readDescriptor probe (Some dataDir) path None
                else Result.Error(sprintf "descriptor not found: %s" path)
        else
            match m.DirPath with
            | Some d when probe.DirExists d -> resolveExplicitMod probe d
            | _ ->
                match m.SteamId |> Option.map (resolveWorkshopMod adapter probe), m.Descriptor with
                | Some(Result.Ok r), _ -> Result.Ok r
                | _, Some rel -> resolveExplicitMod probe (Path.Combine(dataDir, rel))
                | _ -> Result.Error(sprintf "mod '%s': no resolvable path (unsubscribed?)" m.Name)

    /// Resolve the *enabled* mods of a playset (by name or id) to DiscoveredMods
    /// in launcher display order. Returns mods + per-mod warnings; Error if the launcher db
    /// or the named playset is missing.
    let private readPlayset
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
                    |> List.map (resolvePlaysetMod adapter probe dataDir)
                    |> collectResults

                Result.Ok(mods, warnings)

    let resolvePlayset adapter probe selector =
        readPlayset adapter probe selector |> Result.map (orderLauncherMods adapter)

    /// Manual inputs keep their supplied order. Build a single dependency graph for
    /// all launcher-selected EU4 mods, including selections combined on refresh.
    let selectMods adapter probe explicitPaths workshopIds playset auto =
        let manual =
            List.map (resolveExplicitMod probe) explicitPaths @ List.map (resolveWorkshopMod adapter probe) workshopIds
        let selected, pw =
            match playset with
            | None -> [], []
            | Some selector ->
                match readPlayset adapter probe selector with
                | Result.Ok selection -> selection
                | Result.Error e -> [], [ e ]
        let automatic, aw = if auto then readAutoMods adapter probe else [], []
        let ordered, warnings = orderLauncherMods adapter (selected @ automatic, pw @ aw)
        let manualMods, manualErrors = collectResults manual
        manualMods @ ordered, manualErrors @ warnings

    let resolveMods adapter probe explicitPaths workshopIds playset auto =
        match selectMods adapter probe explicitPaths workshopIds playset auto with
        | mods, [] -> Result.Ok mods
        | _, errors -> Result.Error(String.concat "\n" errors)

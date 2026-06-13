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
            adapter.GameDirCandidates()
            |> List.tryFind (isGameDir adapter probe)
            |> function
                | Some dir -> Result.Ok dir
                | None ->
                    Result.Error(
                        "game directory not found in default Steam locations; pass it explicitly with --game-dir"
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

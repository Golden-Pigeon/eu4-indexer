namespace Eu4Indexer.Core

/// Enumerates files across all sources and resolves file-level overrides:
/// replace_path wipes first, then same-relative-path shadowing by load order.
/// Shadowed/wiped files stay in the result with IsEffective=false so the DB
/// can answer "who overrode whom and what".
module FileResolution =

    open System.IO
    open System.Security.Cryptography

    type Resolution =
        { Files: GameFile list
          Overrides: FileOverride list }

    let private sha256Hex (path: string) =
        use stream = File.OpenRead path
        use sha = SHA256.Create()
        sha.ComputeHash stream |> Array.map (sprintf "%02x") |> String.concat ""

    let private normalize (p: string) = p.Replace('\\', '/').ToLowerInvariant()

    /// Enumerate candidate files of one source over the folder set.
    /// Folders may overlap (folders.cwt lists both "common" and its subdirs);
    /// each file is assigned to its most specific matching folder.
    let private enumerateSource
        (scriptExtensions: Set<string>)
        (localisationFolder: string)
        (folders: string list)
        (source: Source)
        =
        let allFolders = localisationFolder :: folders |> List.distinct

        let candidates =
            allFolders
            |> List.collect (fun folder ->
                let dir = Path.Combine(source.RootPath, folder)

                if Directory.Exists dir then
                    Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    |> Seq.filter (fun f ->
                        let ext = Path.GetExtension(f).ToLowerInvariant()

                        if normalize folder = normalize localisationFolder then
                            ext = ".yml"
                        else
                            Set.contains ext scriptExtensions)
                    |> Seq.map (fun f -> folder, f)
                    |> List.ofSeq
                else
                    [])

        candidates
        // Group by the normalized relative path (the DB's uniqueness key), not
        // the raw absolute path: enumerating a parent folder and a nested child
        // folder yields the same file under two path strings that differ only by
        // separator/case (e.g. ...\common\x\f.txt vs ...\common/x\f.txt), which a
        // raw-string groupBy would fail to collapse into one row.
        |> List.map (fun (folder, absPath) ->
            folder, absPath, normalize (Path.GetRelativePath(source.RootPath, absPath)))
        |> List.groupBy (fun (_, _, relPath) -> relPath)
        |> List.map (fun (relPath, group) ->
            // most specific (longest) folder wins the attribution
            let folder = group |> List.map (fun (f, _, _) -> f) |> List.maxBy String.length
            let absPath = group |> List.map (fun (_, a, _) -> a) |> List.head
            folder, absPath, relPath)
        |> List.sortBy (fun (_, _, relPath) -> relPath)

    /// Builds the full file list (with hashes) and override records for the
    /// given sources, which must be ordered by LoadOrder ascending.
    let resolve
        (scriptExtensions: Set<string>)
        (localisationFolder: string)
        (folders: string list)
        (sources: Source list)
        : Resolution =

        let mutable nextId = 0

        let files =
            sources
            |> List.collect (fun source ->
                enumerateSource scriptExtensions localisationFolder folders source
                |> List.map (fun (folder, absPath, relPath) ->
                    nextId <- nextId + 1

                    { FileId = nextId
                      SourceId = source.SourceId
                      AbsolutePath = absPath
                      RelativePath = relPath
                      Folder = normalize folder
                      FileName = Path.GetFileName absPath
                      ContentHash = sha256Hex absPath
                      ByteSize = FileInfo(absPath).Length
                      IsEffective = true
                      ParseStatus = ParsedOk }))

        let loadOrderOf =
            sources |> List.map (fun s -> s.SourceId, s.LoadOrder) |> Map.ofList

        // 1. replace_path wipes: a source's replace_path removes every file of
        //    *earlier* sources under that path.
        let replaceDirectives =
            sources
            |> List.collect (fun s ->
                match s.Descriptor with
                | Some d -> d.ReplacePaths |> List.map (fun p -> s.SourceId, s.LoadOrder, normalize p)
                | None -> [])

        let replaceWipe (file: GameFile) =
            replaceDirectives
            |> List.tryFind (fun (_, loadOrder, path) ->
                loadOrderOf[file.SourceId] < loadOrder
                && (file.RelativePath.StartsWith(path + "/") || file.RelativePath = path))

        let afterReplace, replaceOverrides =
            files
            |> List.mapFold
                (fun overrides file ->
                    match replaceWipe file with
                    | Some(winnerSourceId, _, _) ->
                        let ov =
                            { Kind = FileReplacePath
                              RelativePath = file.RelativePath
                              LoserFileId = file.FileId
                              WinnerFileId = None
                              WinnerSourceId = winnerSourceId
                              LoserSourceId = file.SourceId
                              IdenticalContent = false }

                        { file with IsEffective = false }, ov :: overrides
                    | None -> file, overrides)
                []

        // 2. Shadowing among still-effective files: same relative path, the
        //    highest load order wins.
        let shadowOverrides =
            afterReplace
            |> List.filter (fun f -> f.IsEffective)
            |> List.groupBy (fun f -> f.RelativePath)
            |> List.collect (fun (_, group) ->
                match group |> List.sortByDescending (fun f -> loadOrderOf[f.SourceId]) with
                | [] | [ _ ] -> []
                | winner :: losers ->
                    losers
                    |> List.map (fun loser ->
                        { Kind = FileShadowed
                          RelativePath = loser.RelativePath
                          LoserFileId = loser.FileId
                          WinnerFileId = Some winner.FileId
                          WinnerSourceId = winner.SourceId
                          LoserSourceId = loser.SourceId
                          IdenticalContent = winner.ContentHash = loser.ContentHash }))

        let shadowedIds = shadowOverrides |> List.map (fun o -> o.LoserFileId) |> Set.ofList

        let finalFiles =
            afterReplace
            |> List.map (fun f ->
                if Set.contains f.FileId shadowedIds then
                    { f with IsEffective = false }
                else
                    f)

        { Files = finalFiles
          Overrides = List.rev replaceOverrides @ shadowOverrides }

namespace Eu4Indexer.Core

/// Parses Paradox .mod descriptor files (same Clausewitz syntax as game script).
module ModDescriptor =

    open System.IO
    open CWTools.Parser
    open CWTools.Process
    open CWTools.Utilities.Utils
    open FParsec

    let parseText (fileName: string) (text: string) : Result<ModDescriptorInfo, string> =
        match CKParser.parseString text fileName with
        | Failure(err, _, _) -> Result.Error err
        | Success(statements, _, _) ->
            let root = ProcessCore.processNodeBasic "root" (mkZeroFile fileName) statements

            let tag key =
                if root.Has key then
                    let v = root.TagText key
                    if v = "" then None else Some v
                else
                    None

            let listValues key =
                root.Child key
                |> Option.map (fun c -> c.LeafValues |> Seq.map (fun lv -> lv.ValueText) |> List.ofSeq)
                |> Option.defaultValue []

            // replace_path is a repeatable leaf, not a list block
            let replacePaths =
                root.Leaves
                |> Seq.filter (fun l -> l.Key = "replace_path")
                |> Seq.map (fun l -> l.ValueText.Replace('\\', '/').TrimEnd('/'))
                |> List.ofSeq

            Result.Ok
                { Name = tag "name" |> Option.defaultValue (Path.GetFileNameWithoutExtension fileName)
                  Version = tag "version"
                  SupportedVersion = tag "supported_version"
                  RemoteFileId = tag "remote_file_id"
                  Picture = tag "picture"
                  Path = tag "path"
                  Archive = tag "archive"
                  Tags = listValues "tags"
                  Dependencies = listValues "dependencies"
                  ReplacePaths = replacePaths }

    let parseFile (path: string) : Result<ModDescriptorInfo, string> =
        try
            parseText (Path.GetFileName path) (File.ReadAllText path)
        with ex ->
            Result.Error ex.Message

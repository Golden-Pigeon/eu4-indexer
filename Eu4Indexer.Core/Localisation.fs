namespace Eu4Indexer.Core

/// Parses localisation YAML files into LocRow values. Rows keep their source
/// file's effectiveness; key-level override resolution happens in
/// OverrideResolution.
module Localisation =

    open CWTools.Localisation
    open FParsec

    /// Is this loc file under a replace/ directory (guaranteed-override semantics)?
    let isReplacePath (relativePath: string) = relativePath.Contains "/replace/"

    let private stripQuotes (s: string) =
        let s = s.Trim()

        if s.Length >= 2 && s.StartsWith "\"" && s.EndsWith "\"" then
            s.Substring(1, s.Length - 2)
        else
            s

    /// Parse one loc file; returns rows (without ids) or a parse error.
    /// `language` must already be resolved from the file name.
    let parseFile
        (nextLocId: unit -> int64)
        (file: GameFile)
        (language: string)
        : Result<LocRow list, Parsing.ParseErrorInfo> =

        match YAMLLocalisationParser.parseLocFile file.AbsolutePath with
        | Failure(msg, err, _) ->
            Result.Error
                { Message = msg
                  Line = Some(int err.Position.Line)
                  Col = Some(int err.Position.Column) }
        | Success(locFile, _, _) ->
            let isReplace = isReplacePath file.RelativePath

            locFile.entries
            |> Seq.map (fun entry ->
                { LocId = nextLocId ()
                  LocKey = entry.key
                  Language = language
                  Value = stripQuotes entry.desc
                  VersionNum =
                    entry.value
                    |> Option.map (fun c -> int c - int '0')
                  FileId = file.FileId
                  SourceId = file.SourceId
                  IsReplace = isReplace
                  IsEffective = file.IsEffective }) // key-level resolution may flip more later
            |> List.ofSeq
            |> Result.Ok

namespace Eu4Indexer.Core

/// Wraps CWTools parsing. Parsing and source slicing use the same decoded text.
module Parsing =

    open System.IO
    open System.Text
    open CWTools.Parser
    open CWTools.Process
    open CWTools.Utilities.Utils
    open FParsec

    do Encoding.RegisterProvider CodePagesEncodingProvider.Instance

    type ParseErrorInfo =
        { Message: string
          Line: int option
          Col: int option }

    type ParsedFile =
        { Root: Node
          /// Source split into lines, for raw-text slicing by position range
          Lines: string[] }

    let private hasUtf8Bom (bytes: byte[]) =
        bytes.Length >= 3 && bytes[0] = 0xEFuy && bytes[1] = 0xBBuy && bytes[2] = 0xBFuy

    /// Honor a Unicode BOM, then prefer well-formed UTF-8 over legacy Windows-1252.
    let decodeBytes (bytes: byte[]) =
        if hasUtf8Bom bytes then
            Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
        elif bytes.Length >= 2 && bytes[0] = 0xFFuy && bytes[1] = 0xFEuy then
            Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2)
        elif bytes.Length >= 2 && bytes[0] = 0xFEuy && bytes[1] = 0xFFuy then
            Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2)
        elif Unicode.Utf8.IsValid(bytes) then
            Encoding.UTF8.GetString bytes
        else
            Encoding.GetEncoding(1252).GetString bytes

    let private splitLines (text: string) = text.Replace("\r\n", "\n").Split '\n'

    let parseText (fileName: string) (text: string) : Result<ParsedFile, ParseErrorInfo> =
        match CKParser.parseString text fileName with
        | Failure(msg, err, _) ->
            Result.Error
                { Message = msg
                  Line = Some(int err.Position.Line)
                  Col = Some(int err.Position.Column) }
        | Success(statements, _, _) ->
            let root = ProcessCore.processNodeBasic "root" (mkZeroFile fileName) statements

            Result.Ok
                { Root = root
                  Lines = splitLines text }

    let parseFile (path: string) : Result<ParsedFile, ParseErrorInfo> =
        try
            let bytes = File.ReadAllBytes path
            let text = decodeBytes bytes
            match parseText path text with
            | Result.Error _ as original when
                not (hasUtf8Bom bytes) && not (Ascii.IsValid(bytes)) && Unicode.Utf8.IsValid(bytes) ->
                // A legacy file can coincidentally be valid UTF-8. Recover it if only the legacy parse works.
                match parseText path (Encoding.GetEncoding(1252).GetString bytes) with
                | Result.Ok _ as parsed -> parsed
                | Result.Error _ -> original
            | parsed -> parsed
        with ex ->
            Result.Error
                { Message = ex.Message
                  Line = None
                  Col = None }

    /// Raw text of an inclusive 1-based line range.
    let sliceLines (lines: string[]) (startLine: int) (endLine: int) =
        let lo = max 1 startLine
        let hi = min lines.Length endLine

        if lo > hi then
            ""
        else
            lines[lo - 1 .. hi - 1] |> String.concat "\n"

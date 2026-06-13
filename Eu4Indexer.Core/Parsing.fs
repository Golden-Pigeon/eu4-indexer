namespace Eu4Indexer.Core

/// Wraps CWTools parsing. Files are decoded once (BOM-aware, falling back to
/// Windows-1252 like the game does) and the same string is used for both
/// parsing and raw-text slicing, so source positions stay consistent.
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

    /// Decode file bytes: honor a Unicode BOM if present, else Windows-1252
    /// (the encoding EU4 script uses and CWTools assumes).
    let decodeBytes (bytes: byte[]) =
        if bytes.Length >= 3 && bytes[0] = 0xEFuy && bytes[1] = 0xBBuy && bytes[2] = 0xBFuy then
            Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
        elif bytes.Length >= 2 && bytes[0] = 0xFFuy && bytes[1] = 0xFEuy then
            Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2)
        elif bytes.Length >= 2 && bytes[0] = 0xFEuy && bytes[1] = 0xFFuy then
            Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2)
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
            parseText path (decodeBytes (File.ReadAllBytes path))
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

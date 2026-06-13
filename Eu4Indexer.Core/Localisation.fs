namespace Eu4Indexer.Core

/// Parses localisation YAML files into LocRow values. Rows keep their source
/// file's effectiveness; key-level override resolution happens in
/// OverrideResolution.
module Localisation =

    open System
    open System.IO
    open System.Text
    open CWTools.Localisation
    open FParsec

    /// Is this loc file under a replace/ directory (guaranteed-override semantics)?
    let isReplacePath (relativePath: string) = relativePath.Contains "/replace/"

    // -----------------------------------------------------------------------
    // Special-escape decoding.
    //
    // Non-Latin localisation mods (e.g. Chinese) store their text in the
    // l_english slot but pre-encode every code point >= 256 with the EU4
    // "special escape" scheme (ported from matanki-saito/EU4SpecialEscape and
    // mirrored by https://gist.github.com/bruceCzK/96ad6e054111f929ed67291552d36334).
    // Each such character becomes three code points: an escape marker in
    // 0x10..0x13 followed by an offset/cp1252-mangled low and high byte. The
    // functions below invert that transform so the stored value is real UTF-8.
    // ASCII/Latin text contains no markers and passes through untouched.
    // -----------------------------------------------------------------------

    /// EU4 uses the >= 1.26 ("new version") rule and UTF-8 output.
    let private lowByteOffset = 14
    let private highByteOffset = 9 // encoder added -9; decoder undoes with +9

    let private escapeMarkers = [| '\u0010'; '\u0011'; '\u0012'; '\u0013' |]

    /// Inverse of the encoder's cp1252 -> UTF-8 control-range remap. Keys are
    /// the Unicode code points CP1252 assigns to bytes 0x80..0x9F; values are
    /// those raw bytes.
    let private utf8ToCp1252 =
        dict
            [ 0x20AC, 0x80; 0x201A, 0x82; 0x0192, 0x83; 0x201E, 0x84
              0x2026, 0x85; 0x2020, 0x86; 0x2021, 0x87; 0x02C6, 0x88
              0x2030, 0x89; 0x0160, 0x8A; 0x2039, 0x8B; 0x0152, 0x8C
              0x017D, 0x8E; 0x2018, 0x91; 0x2019, 0x92; 0x201C, 0x93
              0x201D, 0x94; 0x2022, 0x95; 0x2013, 0x96; 0x2014, 0x97
              0x02DC, 0x98; 0x2122, 0x99; 0x0161, 0x9A; 0x203A, 0x9B
              0x0153, 0x9C; 0x017E, 0x9E; 0x0178, 0x9F ]

    let private unmapCp1252 cp =
        match utf8ToCp1252.TryGetValue cp with
        | true, b -> b
        | _ -> cp

    /// Reconstruct one escaped character from its marker and the two following
    /// code points. Returns None if the bytes do not form a valid scalar.
    let private decodeOne (marker: int) (lowCp: int) (highCp: int) : string option =
        let mutable low = unmapCp1252 lowCp
        let mutable high = unmapCp1252 highCp

        if marker = 0x11 || marker = 0x13 then
            low <- low - lowByteOffset

        if marker = 0x12 || marker = 0x13 then
            high <- high + highByteOffset

        let cp = high * 256 + low

        if low >= 0 && low <= 0xFF && cp >= 0 && cp <= 0x10FFFF
           && not (cp >= 0xD800 && cp <= 0xDFFF) then
            Some(Char.ConvertFromUtf32 cp)
        else
            None

    /// Decode EU4 special-escape sequences back to UTF-8. No-op (returns the
    /// same string) when the value contains no escape markers.
    let decodeSpecialEscape (s: string) : string =
        if isNull s || s.IndexOfAny escapeMarkers < 0 then
            s
        else
            // Work over Unicode scalar values so surrounding surrogate pairs
            // (e.g. emoji) are never split.
            let cps =
                s.EnumerateRunes()
                |> Seq.map (fun r -> r.Value)
                |> Array.ofSeq

            let sb = StringBuilder(s.Length)
            let mutable i = 0

            while i < cps.Length do
                let c = cps.[i]

                if c >= 0x10 && c <= 0x13 && i + 2 < cps.Length then
                    match decodeOne c cps.[i + 1] cps.[i + 2] with
                    | Some text ->
                        sb.Append text |> ignore
                        i <- i + 3
                    | None ->
                        sb.Append(Char.ConvertFromUtf32 c) |> ignore
                        i <- i + 1
                else
                    sb.Append(Char.ConvertFromUtf32 c) |> ignore
                    i <- i + 1

            sb.ToString()

    let private stripQuotes (s: string) =
        let s = s.Trim()

        if s.Length >= 2 && s.StartsWith "\"" && s.EndsWith "\"" then
            s.Substring(1, s.Length - 2)
        else
            s

    /// The verbatim text between the first and last double quote on a line.
    /// Used to recover values CWTools truncated (see `valueFromRawLine`).
    let private extractQuoted (line: string) : string option =
        let first = line.IndexOf '"'
        let last = line.LastIndexOf '"'

        if first >= 0 && last > first then
            Some(line.Substring(first + 1, last - first - 1))
        else
            None

    /// CWTools' value parser stops at control bytes 0x10..0x13 (used by the
    /// special-escape encoding), leaving `desc` truncated to just the opening
    /// quote and setting `errorRange`. For those entries we re-read the raw
    /// line so the escaped bytes survive; clean entries keep CWTools' desc.
    let private resolveValue (rawLines: Lazy<string[]>) (entry: Entry) : string =
        match entry.errorRange with
        | Some _ ->
            let lines = rawLines.Value
            let idx = entry.position.StartLine - 1

            if idx >= 0 && idx < lines.Length then
                match extractQuoted lines.[idx] with
                | Some v -> v
                | None -> stripQuotes entry.desc
            else
                stripQuotes entry.desc
        | None -> stripQuotes entry.desc

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
            // Read lazily: only files with control-byte-escaped values need it.
            let rawLines =
                lazy
                    (try
                        File.ReadAllLines(file.AbsolutePath, Encoding.UTF8)
                     with _ ->
                        [||])

            locFile.entries
            |> Seq.map (fun entry ->
                { LocId = nextLocId ()
                  LocKey = entry.key
                  Language = language
                  Value = resolveValue rawLines entry |> decodeSpecialEscape
                  VersionNum =
                    entry.value
                    |> Option.map (fun c -> int c - int '0')
                  FileId = file.FileId
                  SourceId = file.SourceId
                  IsReplace = isReplace
                  IsEffective = file.IsEffective }) // key-level resolution may flip more later
            |> List.ofSeq
            |> Result.Ok

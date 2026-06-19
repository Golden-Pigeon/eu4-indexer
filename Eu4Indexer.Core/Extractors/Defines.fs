namespace Eu4Indexer.Core.Extractors

open System
open System.IO
open System.Text

/// Extracts key-value pairs from HOI4 define files (LUA format:
/// NDefines = { NCategory = { KEY = value, ... }, ... }).
/// Outputs flat "NCategory.KEY" → value pairs.
module Defines =

    let private splitLines (text: string) =
        text.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)

    /// Read and parse one defines .lua file into (key, value) pairs
    /// where key is e.g. "NFocus.FOCUS_POINT_DAYS".
    let parse (filePath: string) : (string * string) list =
        let lines =
            try
                splitLines (File.ReadAllText(filePath, Encoding.UTF8))
            with _ ->
                [||]

        let catStack = ResizeArray<string>()
        let results = ResizeArray<string * string>()

        for rawLine in lines do
            let t = rawLine.Trim()

            if t.Length > 0 && not (t.StartsWith("--")) then

                // Strip trailing inline comment.
                let mutable content = t
                let dashIdx = content.IndexOf("--")
                if dashIdx > 0 && content.[dashIdx - 1] = ' ' then
                    content <- content.Substring(0, dashIdx).TrimEnd()

                let eqIdx = content.IndexOf('=')

                // Single-line table value: KEY = { val, val } — has both { and }.
                // Treat as a key=value pair, don't touch the category stack.
                if eqIdx > 0
                   && content.IndexOf('{', eqIdx) > eqIdx
                   && content.IndexOf('}', eqIdx) > eqIdx then
                    let name = content.Substring(0, eqIdx).Trim()
                    let value =
                        content.Substring(eqIdx + 1).Trim().TrimEnd(',').TrimEnd()
                    if catStack.Count > 0 then
                        let fullKey =
                            String.concat "." (List.ofSeq catStack @ [name])
                        results.Add(fullKey, value)
                else
                    // Closing brace(s) — pop category stack.
                    let mutable clean = content.TrimEnd()
                    while clean.EndsWith("}") || clean.EndsWith("},") do
                        if catStack.Count > 0 then
                            catStack.RemoveAt(catStack.Count - 1)

                        if clean.EndsWith("},") then
                            clean <- clean.Substring(0, clean.Length - 2).TrimEnd()
                        else
                            clean <- clean.Substring(0, clean.Length - 1).TrimEnd()

                    // What remains after popping closing braces?
                    let remainingEqIdx = clean.IndexOf('=')
                    if remainingEqIdx > 0 then
                        let name = clean.Substring(0, remainingEqIdx).Trim()
                        let rhs = clean.Substring(remainingEqIdx + 1).Trim()

                        if rhs.StartsWith("{") then
                            // Block opener: Name = { (no } on this line)
                            if name <> "NDefines" then
                                catStack.Add(name)
                        else
                            // Key = value.
                            if catStack.Count > 0 then
                                let mutable value = rhs.TrimEnd(',').TrimEnd()
                                if value.StartsWith("\"") && value.EndsWith("\"") then
                                    value <- value.Substring(1, value.Length - 2)
                                if value.Length > 0 then
                                    let fullKey =
                                        String.concat "." (List.ofSeq catStack @ [name])
                                    results.Add(fullKey, value)

        results |> List.ofSeq

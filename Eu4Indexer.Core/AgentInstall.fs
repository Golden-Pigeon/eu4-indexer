namespace Eu4Indexer.Core

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open System.Runtime.InteropServices

/// Registers the bundled MCP server (and skill) with local AI coding agents so
/// eu4-indexer works from any directory: user-scoped config + a user-level
/// skill, both pointing at the installed binary by absolute path. No plugin and
/// no per-repo setup. Edits are backed up and idempotent.
module AgentInstall =

    type AgentResult = { Agent: string; Ok: bool; Message: string }

    [<Literal>]
    let McpName = "eu4"

    let private home () = Environment.GetFolderPath Environment.SpecialFolder.UserProfile

    /// Absolute path to the eu4indexer executable to register. Prefer the
    /// installed binary; fall back to the currently running process.
    let exePath () =
        let exeName = if RuntimeInformation.IsOSPlatform OSPlatform.Windows then "eu4indexer.exe" else "eu4indexer"
        let installed = Path.Combine(AppPaths.binDir (), exeName)
        if File.Exists installed then installed
        else
            match Environment.ProcessPath with
            | null | "" -> installed
            | p -> p

    let private backup (path: string) =
        if File.Exists path then File.Copy(path, path + ".bak", true)

    /// Recursively copy a directory tree, overwriting the destination.
    let rec private copyDir (src: string) (dest: string) =
        AppPaths.ensureDir dest |> ignore
        for file in Directory.GetFiles src do
            File.Copy(file, Path.Combine(dest, Path.GetFileName file), true)
        for dir in Directory.GetDirectories src do
            copyDir dir (Path.Combine(dest, Path.GetFileName dir))

    // -- Claude Code -------------------------------------------------------

    /// Write the MCP server into ~/.claude.json (user scope) and copy the skill
    /// into ~/.claude/skills/eu4-indexer.
    let installClaude (skillSrc: string) : AgentResult =
        try
            let exe = exePath ()
            let claudeJson = Path.Combine(home (), ".claude.json")

            let root: JsonNode =
                if File.Exists claudeJson then
                    match JsonNode.Parse(File.ReadAllText claudeJson) with
                    | null -> JsonObject() :> JsonNode
                    | n -> n
                else
                    JsonObject() :> JsonNode

            let rootObj = root.AsObject()

            if not (rootObj.ContainsKey "mcpServers") then
                rootObj["mcpServers"] <- JsonObject()

            let server = JsonObject()
            server["command"] <- JsonValue.Create exe
            server["args"] <- JsonArray(JsonValue.Create "serve")
            rootObj["mcpServers"].AsObject()[McpName] <- server

            backup claudeJson
            File.WriteAllText(claudeJson, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))

            // User-level skill so it is available in any directory.
            let mutable skillMsg = ""
            if Directory.Exists skillSrc then
                let skillDest = Path.Combine(home (), ".claude", "skills", "eu4-indexer")
                copyDir skillSrc skillDest
                skillMsg <- sprintf "; skill -> %s" (AppPaths.normalize skillDest)
            else
                skillMsg <- sprintf "; skill source not found (%s), skipped" skillSrc

            { Agent = "claude"; Ok = true; Message = sprintf "MCP '%s' -> %s%s" McpName (AppPaths.normalize claudeJson) skillMsg }
        with ex ->
            { Agent = "claude"; Ok = false; Message = ex.Message }

    // -- Codex -------------------------------------------------------------

    /// Strip an existing [mcp_servers.eu4] block (header to the next top-level
    /// header or EOF) so the rewrite stays idempotent without a TOML parser.
    let private removeCodexBlock (lines: string list) =
        let isOurHeader (l: string) = l.Trim() = sprintf "[mcp_servers.%s]" McpName
        let isAnyHeader (l: string) =
            let t = l.Trim()
            t.StartsWith "[" && t.EndsWith "]"

        let rec loop acc skipping =
            function
            | [] -> List.rev acc
            | (l: string) :: rest ->
                if skipping then
                    if isAnyHeader l && not (isOurHeader l) then loop (l :: acc) false rest
                    else loop acc true rest
                elif isOurHeader l then loop acc true rest
                else loop (l :: acc) false rest

        loop [] false lines

    let installCodex () : AgentResult =
        try
            let exe = exePath ()
            let codexToml = Path.Combine(home (), ".codex", "config.toml")
            AppPaths.ensureDir (Path.GetDirectoryName codexToml) |> ignore

            let existing =
                if File.Exists codexToml then File.ReadAllLines codexToml |> List.ofArray else []

            let kept = removeCodexBlock existing |> List.rev |> List.skipWhile String.IsNullOrWhiteSpace |> List.rev

            // TOML strings: escape backslashes (Windows paths) and quotes.
            let esc (s: string) = s.Replace("\\", "\\\\").Replace("\"", "\\\"")

            let block =
                [ sprintf "[mcp_servers.%s]" McpName
                  sprintf "command = \"%s\"" (esc exe)
                  "args = [\"serve\"]" ]

            let content =
                (if kept.IsEmpty then block else kept @ [ "" ] @ block)
                |> String.concat Environment.NewLine

            backup codexToml
            File.WriteAllText(codexToml, content + Environment.NewLine)

            { Agent = "codex"; Ok = true; Message = sprintf "MCP '%s' -> %s" McpName (AppPaths.normalize codexToml) }
        with ex ->
            { Agent = "codex"; Ok = false; Message = ex.Message }

    /// Install the requested agents (by name). Unknown names yield a failed result.
    let run (agents: string list) (skillSrc: string) : AgentResult list =
        agents
        |> List.map (fun a ->
            match a.Trim().ToLowerInvariant() with
            | "claude" -> installClaude skillSrc
            | "codex" -> installCodex ()
            | other -> { Agent = other; Ok = false; Message = "unknown agent (expected: claude, codex)" })

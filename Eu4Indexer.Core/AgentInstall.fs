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

    /// Absolute path to the eu4indexer executable to register. Use the running
    /// binary's own path so `install` always registers whatever you actually
    /// ran (a release in place, or a `dotnet build` apphost during development) —
    /// no copy/publish step needed. Falls back to the canonical install location
    /// only if the process path is unavailable.
    let exePath () =
        let exeName = if RuntimeInformation.IsOSPlatform OSPlatform.Windows then "eu4indexer.exe" else "eu4indexer"
        match Environment.ProcessPath with
        | null | "" -> Path.Combine(AppPaths.binDir (), exeName)
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

    /// Copy each game's selected language into a flat user-level skills directory.
    /// Source: {skillSrc}/{game}/{language}; destination: {skillsHome}/{game}.
    let private installSkills (skillSrc: string) (language: string) (skillsHome: string) =
        if Directory.Exists skillSrc then
            let mutable copied = false
            for gameDir in Directory.GetDirectories skillSrc do
                let langDir = Path.Combine(gameDir, language)
                if Directory.Exists langDir then
                    let gameName = Path.GetFileName gameDir
                    copyDir langDir (Path.Combine(skillsHome, gameName))
                    copied <- true

            if copied then
                sprintf "; skill (%s) -> %s" language (AppPaths.normalize skillsHome)
            else
                sprintf "; skill source for '%s' not found under %s, skipped" language skillSrc
        else
            sprintf "; skill source not found (%s), skipped" skillSrc

    // -- Claude Code -------------------------------------------------------

    /// Write the MCP server into ~/.claude.json (user scope) and copy each
    /// game's skill as a flat user-level skill into ~/.claude/skills/{game}/SKILL.md
    /// so Claude Code finds them as /eu4 and /hoi4. Source layout is
    /// skills/eu4-indexer/{game}/{language}/SKILL.md; install flattens language.
    let installClaude (skillSrc: string) (language: string) : AgentResult =
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

            // Flat user-level skills: each game -> ~/.claude/skills/{game}/SKILL.md.
            let skillsHome = Path.Combine(home (), ".claude", "skills")
            let skillMsg = installSkills skillSrc language skillsHome

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

    /// Write the MCP server into ~/.codex/config.toml and copy each game's skill
    /// into the Codex-specific user path ~/.codex/skills/{game}/SKILL.md.
    /// The explicit home keeps the filesystem work independently testable.
    let internal installCodexAt (userHome: string) (skillSrc: string) (language: string) : AgentResult =
        try
            let exe = exePath ()
            let codexToml = Path.Combine(userHome, ".codex", "config.toml")
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

            let skillsHome = Path.Combine(userHome, ".codex", "skills")
            let skillMsg = installSkills skillSrc language skillsHome

            { Agent = "codex"; Ok = true; Message = sprintf "MCP '%s' -> %s%s" McpName (AppPaths.normalize codexToml) skillMsg }
        with ex ->
            { Agent = "codex"; Ok = false; Message = ex.Message }

    let installCodex (skillSrc: string) (language: string) : AgentResult =
        installCodexAt (home ()) skillSrc language

    /// Install the requested agents (by name). Unknown names yield a failed result.
    let run (agents: string list) (skillSrc: string) (language: string) : AgentResult list =
        agents
        |> List.map (fun a ->
            match a.Trim().ToLowerInvariant() with
            | "claude" -> installClaude skillSrc language
            | "codex" -> installCodex skillSrc language
            | other -> { Agent = other; Ok = false; Message = "unknown agent (expected: claude, codex)" })

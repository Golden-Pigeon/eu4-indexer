namespace Eu4Indexer.Core

/// Which core extractor handles a folder (everything else goes through the
/// config-driven generic extractor).
type CoreEntityKind =
    | EventsFolder
    | MissionsFolder
    | DecisionsFolder
    | EventModifiersFolder
    | StaticModifiersFolder
    | TriggeredModifiersFolder
    | FocusTreesFolder
    | IdeasFolder

/// Static, per-game knowledge. One record instance per supported game.
/// Folder *contents* (which common/ subfolders exist, type definitions, symbol
/// catalogs) come from the game's cwtools config repo at run time, not from
/// this record.
type GameAdapter =
    { GameId: string
      /// File extensions treated as script inside indexable folders
      ScriptExtensions: Set<string>
      LocalisationFolder: string
      Languages: string list
      /// Steam app id used for workshop content discovery
      SteamAppId: string
      /// Candidate Steam *client* install dirs to probe (the parent of
      /// `steamapps`). Discovery reads each one's libraryfolders.vdf to find
      /// every library wherever it lives.
      SteamClientDirs: unit -> string list
      /// The game's folder name under `steamapps/common`
      SteamGameDir: string
      /// Candidate "user data" dirs holding mod/*.mod descriptors
      ModDirCandidates: unit -> string list
      /// Subdirectories of the game dir whose presence validates a detection hit
      ValidationSubdirs: string list
      /// Folders handled by dedicated core extractors
      CoreFolders: Map<string, CoreEntityKind>
      /// Detect the language from a localisation file's game-relative path.
      /// EU4 uses a `*_l_<language>.yml` name suffix; HOI4 uses the parent
      /// directory name (`localisation/english/foo.yml` → `english`).
      LocFileLanguage: string -> string option
      /// (ref_kind, target_type, child field) keyed by lowercased script key
      /// that produces the reference. Scoped per game because trigger/effect
      /// keywords differ.
      RefKeyRules: Map<string, string * string * string> }

module GameAdapter =

    open System
    open System.IO

    let private home () =
        Environment.GetFolderPath Environment.SpecialFolder.UserProfile

    /// Steam client dir(s) read from the Windows registry. This is the only
    /// reliable source when the *client itself* was installed off the default
    /// drive (the registry records exactly where Steam lives). Empty on
    /// non-Windows, where the registry doesn't exist.
    let private steamRegistryDirs () =
        let isWindows =
            Runtime.InteropServices.RuntimeInformation.IsOSPlatform Runtime.InteropServices.OSPlatform.Windows

        if not isWindows then
            []
        else
            let read (root: Microsoft.Win32.RegistryKey) (subkey: string) (name: string) =
                try
                    use key = root.OpenSubKey subkey

                    if isNull key then
                        None
                    else
                        match key.GetValue name with
                        | :? string as s when not (String.IsNullOrWhiteSpace s) -> Some s
                        | _ -> None
                with _ ->
                    None

            [ read Microsoft.Win32.Registry.CurrentUser @"Software\Valve\Steam" "SteamPath"
              read Microsoft.Win32.Registry.LocalMachine @"SOFTWARE\WOW6432Node\Valve\Steam" "InstallPath"
              read Microsoft.Win32.Registry.LocalMachine @"SOFTWARE\Valve\Steam" "InstallPath" ]
            |> List.choose id

    /// Steam *client* install dirs per platform (the parent of `steamapps`).
    /// The client itself sits at its default location on virtually every
    /// machine even when game *libraries* are moved to other drives; the
    /// libraryfolders.vdf under here enumerates every library wherever it lives.
    /// The indexer runs on any OS against dirs that may have been copied from
    /// another machine, so we probe all platform conventions unconditionally
    /// and let existence checks decide.
    let private steamClientDirs () =
        let h = home ()

        let defaults =
            [ // Windows
              @"C:\Program Files (x86)\Steam"
              @"C:\Program Files\Steam"
              // macOS
              Path.Combine(h, "Library/Application Support/Steam")
              // Linux
              Path.Combine(h, ".steam/steam")
              Path.Combine(h, ".local/share/Steam")
              // Linux (Flatpak)
              Path.Combine(h, ".var/app/com.valvesoftware.Steam/.local/share/Steam") ]

        // Registry first: it's authoritative when the client lives off the
        // default drive (defaults would never find it otherwise).
        steamRegistryDirs () @ defaults |> List.distinct

    let eu4: GameAdapter =
        let languages = [ "english"; "french"; "german"; "spanish" ]
        { GameId = "eu4"
          ScriptExtensions = Set.ofList [ ".txt" ]
          LocalisationFolder = "localisation"
          Languages = languages
          SteamAppId = "236850"
          SteamClientDirs = steamClientDirs
          SteamGameDir = "Europa Universalis IV"
          ModDirCandidates =
            fun () ->
                let h = home ()
                [ Path.Combine(h, "Documents", "Paradox Interactive", "Europa Universalis IV")
                  Path.Combine(h, "Library/Application Support/Paradox Interactive/Europa Universalis IV")
                  Path.Combine(h, ".local/share/Paradox Interactive/Europa Universalis IV") ]
          ValidationSubdirs = [ "common"; "events"; "localisation" ]
          CoreFolders =
            Map.ofList
                [ "events", EventsFolder
                  "missions", MissionsFolder
                  "decisions", DecisionsFolder
                  "common/event_modifiers", EventModifiersFolder
                  "common/static_modifiers", StaticModifiersFolder
                  "common/triggered_modifiers", TriggeredModifiersFolder ]
          LocFileLanguage =
            fun relPath ->
                let fileName = Path.GetFileName relPath
                languages
                |> List.tryFind (fun lang ->
                    fileName.EndsWith(sprintf "_l_%s.yml" lang, StringComparison.OrdinalIgnoreCase))
          RefKeyRules =
            Map.ofList
                [ "country_event", ("fires_event", "event", "id")
                  "province_event", ("fires_event", "event", "id")
                  "ruler_event", ("fires_event", "event", "id")
                  "trade_node_event", ("fires_event", "event", "id")
                  "siege_event", ("fires_event", "event", "id")
                  "set_country_flag", ("sets_flag", "country_flag", "flag")
                  "clr_country_flag", ("sets_flag", "country_flag", "flag")
                  "set_global_flag", ("sets_flag", "global_flag", "flag")
                  "clr_global_flag", ("sets_flag", "global_flag", "flag")
                  "set_province_flag", ("sets_flag", "province_flag", "flag")
                  "clr_province_flag", ("sets_flag", "province_flag", "flag")
                  "set_ruler_flag", ("sets_flag", "ruler_flag", "flag")
                  "clr_ruler_flag", ("sets_flag", "ruler_flag", "flag")
                  "has_country_flag", ("checks_flag", "country_flag", "flag")
                  "has_global_flag", ("checks_flag", "global_flag", "flag")
                  "has_province_flag", ("checks_flag", "province_flag", "flag")
                  "has_ruler_flag", ("checks_flag", "ruler_flag", "flag")
                  "set_variable", ("sets_variable", "variable", "which")
                  "change_variable", ("sets_variable", "variable", "which")
                  "add_to_variable", ("sets_variable", "variable", "which")
                  "subtract_variable", ("sets_variable", "variable", "which")
                  "multiply_variable", ("sets_variable", "variable", "which")
                  "divide_variable", ("sets_variable", "variable", "which")
                  "check_variable", ("checks_variable", "variable", "which")
                  "has_variable", ("checks_variable", "variable", "which")
                  "is_variable_equal", ("checks_variable", "variable", "which")
                  "add_country_modifier", ("applies_modifier", "modifier", "name")
                  "add_permanent_province_modifier", ("applies_modifier", "modifier", "name")
                  "add_province_modifier", ("applies_modifier", "modifier", "name")
                  "add_ruler_modifier", ("applies_modifier", "modifier", "name")
                  "add_province_triggered_modifier", ("applies_modifier", "modifier", "name")
                  "add_country_triggered_modifier", ("applies_modifier", "modifier", "name")
                  "has_country_modifier", ("checks_modifier", "modifier", "name")
                  "has_province_modifier", ("checks_modifier", "modifier", "name")
                  "has_ruler_modifier", ("checks_modifier", "modifier", "name") ] }

    let hoi4: GameAdapter =
        let languages =
            [ "english"; "braz_por"; "french"; "german"; "japanese"; "korean"; "polish"
              "russian"; "simp_chinese"; "spanish" ]
        { GameId = "hoi4"
          ScriptExtensions = Set.ofList [ ".txt" ]
          LocalisationFolder = "localisation"
          Languages = languages
          SteamAppId = "394360"
          SteamClientDirs = steamClientDirs
          SteamGameDir = "Hearts of Iron IV"
          ModDirCandidates =
            fun () ->
                let h = home ()
                [ Path.Combine(h, "Documents", "Paradox Interactive", "Hearts of Iron IV")
                  Path.Combine(h, "Library/Application Support/Paradox Interactive/Hearts of Iron IV")
                  Path.Combine(h, ".local/share/Paradox Interactive/Hearts of Iron IV") ]
          ValidationSubdirs = [ "common"; "events"; "localisation" ]
          CoreFolders =
            Map.ofList
                [ "events", EventsFolder
                  "common/national_focus", FocusTreesFolder
                  "common/decisions", DecisionsFolder
                  "common/ideas", IdeasFolder ]
          LocFileLanguage =
            fun relPath ->
                let path = relPath.Replace('/', Path.DirectorySeparatorChar)
                let dir = Path.GetDirectoryName(path)
                if String.IsNullOrEmpty dir then None
                else
                    // Walk up the directory tree so files in subdirectories
                    // (e.g. localisation/simp_chinese/kr_country_specific/...yml)
                    // still resolve to the language directory above them.
                    dir.Split(Path.DirectorySeparatorChar)
                    |> Array.rev
                    |> Array.tryFind (fun seg ->
                        languages
                        |> List.exists (fun lang ->
                            String.Equals(lang, seg, StringComparison.OrdinalIgnoreCase)))
          RefKeyRules =
            Map.ofList
                [ // fires_event
                  "country_event", ("fires_event", "event", "id")
                  "news_event", ("fires_event", "event", "id")
                  "unit_leader_event", ("fires_event", "event", "id")
                  "state_event", ("fires_event", "event", "id")
                  // set/clear flags (HOI4 uses country_flag; other scopes may exist)
                  "set_country_flag", ("sets_flag", "country_flag", "flag")
                  "clr_country_flag", ("sets_flag", "country_flag", "flag")
                  "set_global_flag", ("sets_flag", "global_flag", "flag")
                  "clr_global_flag", ("sets_flag", "global_flag", "flag")
                  // check flags
                  "has_country_flag", ("checks_flag", "country_flag", "flag")
                  "has_global_flag", ("checks_flag", "global_flag", "flag")
                  // set/change variables
                  "set_variable", ("sets_variable", "variable", "which")
                  "change_variable", ("sets_variable", "variable", "which")
                  "add_to_variable", ("sets_variable", "variable", "which")
                  "subtract_variable", ("sets_variable", "variable", "which")
                  "multiply_variable", ("sets_variable", "variable", "which")
                  "divide_variable", ("sets_variable", "variable", "which")
                  // check variables
                  "check_variable", ("checks_variable", "variable", "which")
                  "has_variable", ("checks_variable", "variable", "which")
                  // apply/remove ideas (HOI4's modifier-like concept)
                  "add_ideas", ("applies_modifier", "modifier", "name")
                  "swap_ideas", ("applies_modifier", "modifier", "name")
                  // check ideas
                  "has_idea", ("checks_modifier", "modifier", "name") ] }

    let allAdapters: GameAdapter list = [ eu4; hoi4 ]

    let byId (gameId: string) : GameAdapter option =
        allAdapters |> List.tryFind (fun a -> String.Equals(a.GameId, gameId, StringComparison.OrdinalIgnoreCase))

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

/// Static, per-game knowledge. One record instance per supported game;
/// EU4 is the only implementation for now. Folder *contents* (which common/
/// subfolders exist, type definitions, symbol catalogs) come from the game's
/// cwtools config repo at run time, not from this record.
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
      CoreFolders: Map<string, CoreEntityKind> }

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

    /// Returns the language of a localisation file, if its name matches the
    /// EU4 convention `*_l_<language>.yml`.
    let locFileLanguage (languages: string list) (fileName: string) =
        languages
        |> List.tryFind (fun lang ->
            fileName.EndsWith(sprintf "_l_%s.yml" lang, StringComparison.OrdinalIgnoreCase))

    let eu4: GameAdapter =
        { GameId = "eu4"
          ScriptExtensions = Set.ofList [ ".txt" ]
          LocalisationFolder = "localisation"
          Languages = [ "english"; "french"; "german"; "spanish" ]
          SteamAppId = "236850"
          SteamClientDirs = steamClientDirs
          SteamGameDir = "Europa Universalis IV"
          ModDirCandidates =
            fun () ->
                let h = home ()
                [ Path.Combine(h, "Documents", "Paradox Interactive", "Europa Universalis IV")
                  // macOS user dir convention
                  Path.Combine(h, "Library/Application Support/Paradox Interactive/Europa Universalis IV")
                  // Linux
                  Path.Combine(h, ".local/share/Paradox Interactive/Europa Universalis IV") ]
          ValidationSubdirs = [ "common"; "events"; "localisation" ]
          CoreFolders =
            Map.ofList
                [ "events", EventsFolder
                  "missions", MissionsFolder
                  "decisions", DecisionsFolder
                  "common/event_modifiers", EventModifiersFolder
                  "common/static_modifiers", StaticModifiersFolder
                  "common/triggered_modifiers", TriggeredModifiersFolder ] }

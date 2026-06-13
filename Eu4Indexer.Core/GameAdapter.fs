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
      /// Candidate base-game install dirs to probe, most likely first
      GameDirCandidates: unit -> string list
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

    /// Steam library roots to probe per platform. The indexer runs on any OS
    /// against game dirs that may have been copied from another machine, so we
    /// probe all platform conventions unconditionally and let existence checks
    /// decide.
    let private steamLibraryRoots () =
        let h = home ()
        [ // Windows
          @"C:\Program Files (x86)\Steam\steamapps"
          @"C:\Program Files\Steam\steamapps"
          @"D:\Steam\steamapps"
          @"D:\SteamLibrary\steamapps"
          @"E:\SteamLibrary\steamapps"
          // macOS
          Path.Combine(h, "Library/Application Support/Steam/steamapps")
          // Linux
          Path.Combine(h, ".steam/steam/steamapps")
          Path.Combine(h, ".local/share/Steam/steamapps") ]

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
          GameDirCandidates =
            fun () ->
                steamLibraryRoots ()
                |> List.map (fun root -> Path.Combine(root, "common", "Europa Universalis IV"))
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

namespace Eu4Indexer.Core

open System.IO
open Eu4Indexer.Core.Extractors
open Eu4Indexer.Core.Database

/// Orchestrates the full indexing run: discover sources -> resolve file
/// overrides -> parse scripts -> extract entities -> resolve entity/loc
/// overrides -> write SQLite.
module Pipeline =

    /// Index request parameters.
    type IndexRequest =
        { Adapter: GameAdapter
          GameDir: string
          /// Mods already in load order (earlier loads first, later overrides earlier).
          Mods: Discovery.DiscoveredMod list
          ConfigDir: string
          DbPath: string
          /// Index only the core four, skip the generic extractor (faster dev loop).
          SkipGeneric: bool
          WithFts: bool
          /// Restrict localisation languages; empty means the adapter's full set.
          Languages: string list
          Log: string -> unit }

    type IndexReport =
        { SourceCount: int
          FileCount: int
          EntityCount: int
          EffectiveEntityCount: int
          LocCount: int
          ParseErrorCount: int
          OverrideCount: int
          ForeignKeyViolations: int }

    /// Assembles the discovered game + mods into an ordered source list
    /// (load_order: 0 = base game).
    let buildSources (request: IndexRequest) : Source list =
        let baseSource =
            { SourceId = 1
              Kind = BaseGame
              LoadOrder = 0
              Name = request.Adapter.GameId
              RootPath = request.GameDir
              DescriptorPath = None
              Descriptor = None }

        let modSources =
            request.Mods
            |> List.mapi (fun i m ->
                { SourceId = i + 2
                  Kind = Mod
                  LoadOrder = i + 1
                  Name = m.Info.Name
                  RootPath = m.ContentPath
                  DescriptorPath = m.DescriptorPath
                  Descriptor = Some m.Info })

        baseSource :: modSources

    /// Picks the core extractor for a folder; None defers to the generic extractor.
    let private coreExtractorFor (adapter: GameAdapter) (folder: string) =
        Map.tryFind folder adapter.CoreFolders

    let run (request: IndexRequest) : Result<IndexReport, string> =
        let log = request.Log
        let adapter = request.Adapter

        log (sprintf "Loading config repo: %s" request.ConfigDir)

        match ConfigCatalog.load request.ConfigDir with
        | Result.Error e -> Result.Error e
        | Result.Ok catalog ->

        let lookups = ScriptTree.lookupsFromCatalog catalog
        let languages = if request.Languages.IsEmpty then adapter.Languages else request.Languages

        let sources = buildSources request
        log (sprintf "Sources: %d (base game + %d mods)" sources.Length request.Mods.Length)

        // File enumeration + file-level override resolution.
        let indexFolders =
            (adapter.CoreFolders |> Map.toList |> List.map fst) @ catalog.Folders
            |> List.distinct

        log "Enumerating files and resolving file-level overrides..."

        let resolution =
            FileResolution.resolve adapter.ScriptExtensions adapter.LocalisationFolder indexFolders sources

        let fileById = resolution.Files |> List.map (fun f -> f.FileId, f) |> Map.ofList
        let loadOrderOf = sources |> List.map (fun s -> s.SourceId, s.LoadOrder) |> Map.ofList

        let fileOverrideKindByLoser =
            resolution.Overrides
            |> List.map (fun o -> o.LoserFileId, o.Kind)
            |> Map.ofList

        log (sprintf "Files: %d, file-level overrides: %d" resolution.Files.Length resolution.Overrides.Length)

        let idGen = Support.makeIdGen ()

        let nextLocId =
            let mutable n = 0L
            fun () ->
                n <- n + 1L
                n

        let isLocFile (file: GameFile) =
            file.Folder = adapter.LocalisationFolder.ToLowerInvariant()
            || file.Folder.StartsWith(adapter.LocalisationFolder.ToLowerInvariant() + "/")

        let scriptFiles =
            resolution.Files
            |> List.filter (fun f -> not (isLocFile f) && Path.GetExtension(f.FileName).ToLowerInvariant() <> ".yml")

        let locFiles = resolution.Files |> List.filter isLocFile

        // Parse + extract sequentially: CWTools' stringManager is shared mutable state.
        log "Parsing scripts and extracting entities..."

        let mutable parseErrors: ParseErrorRow list = []
        let mutable failedFileIds: Set<int> = Set.empty

        let payloads =
            scriptFiles
            |> List.collect (fun file ->
                match Parsing.parseFile file.AbsolutePath with
                | Result.Error err ->
                    parseErrors <-
                        { FileId = file.FileId
                          Message = err.Message
                          Line = err.Line
                          Col = err.Col }
                        :: parseErrors

                    failedFileIds <- Set.add file.FileId failedFileIds
                    []
                | Result.Ok parsed ->
                    match coreExtractorFor adapter file.Folder with
                    | Some EventsFolder -> Events.extract lookups idGen file parsed
                    | Some MissionsFolder -> Missions.extract lookups idGen file parsed
                    | Some DecisionsFolder -> Decisions.extract lookups idGen file parsed
                    | Some EventModifiersFolder -> Modifiers.extract "event_modifier" lookups idGen file parsed
                    | Some StaticModifiersFolder -> Modifiers.extract "static_modifier" lookups idGen file parsed
                    | Some TriggeredModifiersFolder ->
                        Modifiers.extract "triggered_modifier" lookups idGen file parsed
                    | None ->
                        if request.SkipGeneric then
                            []
                        else
                            Generic.extract catalog.TypeDefs lookups idGen file parsed)

        let entities = payloads |> List.map (fun p -> p.Entity)
        log (sprintf "Entities: %d, failed files: %d" entities.Length failedFileIds.Count)

        // Entity-level override resolution.
        let entityRes =
            OverrideResolution.resolveEntities fileById loadOrderOf fileOverrideKindByLoser entities

        // Reference / causal graph (events fired, flags/variables set & checked,
        // modifiers applied, scripted trigger/effect calls, on_action firings).
        let scriptedNames (typeName: string) =
            payloads
            |> List.choose (fun p ->
                if p.Entity.EntityType = typeName then Some p.Entity.EntityKey else None)
            |> Set.ofList

        let references =
            ReferenceExtractor.extract (scriptedNames "scripted_trigger") (scriptedNames "scripted_effect") payloads

        log (sprintf "References: %d" references.Length)

        // Localisation parsing + key-level override resolution.
        log "Parsing localisation..."

        let locRows =
            locFiles
            |> List.collect (fun file ->
                match GameAdapter.locFileLanguage languages file.FileName with
                | None -> []
                | Some lang ->
                    match Localisation.parseFile nextLocId file lang with
                    | Result.Ok rows -> rows
                    | Result.Error err ->
                        parseErrors <-
                            { FileId = file.FileId
                              Message = err.Message
                              Line = err.Line
                              Col = err.Col }
                            :: parseErrors

                        failedFileIds <- Set.add file.FileId failedFileIds
                        [])

        let locRes =
            OverrideResolution.resolveLocalisation fileById loadOrderOf fileOverrideKindByLoser locRows

        log (sprintf "Localisation entries: %d" locRows.Length)

        // Write to the database.
        log (sprintf "Writing database: %s" request.DbPath)
        use writer = Writer.create request.DbPath

        writer.InTransaction(fun () ->
            for s in sources do
                writer.InsertSource s

            for f in resolution.Files do
                writer.InsertFile f

            for fileId in failedFileIds do
                writer.UpdateFileParseStatus(fileId, ParseFailed)

            for e in parseErrors do
                writer.InsertParseError e

            for o in resolution.Overrides do
                writer.InsertFileOverride o

            for sym in catalog.Symbols do
                writer.InsertSymbol sym

            for t in catalog.TypeDefs do
                writer.InsertConfigType t)

        writer.InTransaction(fun () ->
            for p in payloads do
                let isEffective =
                    Map.tryFind p.Entity.EntityId entityRes.Effectiveness |> Option.defaultValue true

                writer.InsertPayload(p, isEffective)

            for o in entityRes.Overrides do
                writer.InsertEntityOverride o

            for r in references do
                writer.InsertReference r)

        writer.InTransaction(fun () ->
            for row in locRows do
                let isEffective =
                    Map.tryFind row.LocId locRes.Effectiveness |> Option.defaultValue true

                writer.InsertLocRow(row, isEffective)

            for o in locRes.Overrides do
                writer.InsertLocOverride o)

        let meta =
            [ "indexer_version", "0.1.0"
              "game_id", adapter.GameId
              "config_repo_path", request.ConfigDir
              "languages", String.concat "," languages
              "created_utc", System.DateTime.UtcNow.ToString("o") ]

        log "Building indexes and FTS..."
        let violations = writer.Finalize(meta, request.WithFts)

        let effectiveCount =
            entityRes.Effectiveness |> Map.toSeq |> Seq.filter snd |> Seq.length

        Result.Ok
            { SourceCount = sources.Length
              FileCount = resolution.Files.Length
              EntityCount = entities.Length
              EffectiveEntityCount = effectiveCount
              LocCount = locRows.Length
              ParseErrorCount = parseErrors.Length
              OverrideCount =
                resolution.Overrides.Length + entityRes.Overrides.Length + locRes.Overrides.Length
              ForeignKeyViolations = violations }

    /// Convenience entry: resolve explicit mod paths, then run. Used by the CLI
    /// and by tests. Mod paths are in load order.
    let runWithPaths
        (adapter: GameAdapter)
        (gameDir: string)
        (modPaths: string list)
        (configDir: string)
        (dbPath: string)
        (skipGeneric: bool)
        (withFts: bool)
        (languages: string list)
        (log: string -> unit)
        : Result<IndexReport, string> =

        let probe = Discovery.realProbe

        let mods =
            modPaths
            |> List.choose (fun p ->
                match Discovery.resolveExplicitMod probe p with
                | Result.Ok m -> Some m
                | Result.Error e ->
                    log (sprintf "warning: %s" e)
                    None)

        run
            { Adapter = adapter
              GameDir = gameDir
              Mods = mods
              ConfigDir = configDir
              DbPath = dbPath
              SkipGeneric = skipGeneric
              WithFts = withFts
              Languages = languages
              Log = log }

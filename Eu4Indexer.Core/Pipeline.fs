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

        let mutable parseErrors: ParseErrorRow list = []
        let mutable failedFileIds: Set<int> = Set.empty

        // Open the writer up front and run the index in phases, each writing and
        // releasing its data before the next allocates — so the heavy script
        // nodes are streamed out per file and never coexist with localisation.
        // Avoid logging a Postgres connection string (it may carry a password).
        if Writer.isPostgresTarget request.DbPath then
            log "Writing database: postgres"
        else
            log (sprintf "Writing database: %s" request.DbPath)

        use writer = Writer.create adapter.GameId request.DbPath

        // Phase A: static rows (sources/files/file overrides/symbols/config
        // types). Available immediately and the FK targets for everything later.
        writer.InTransaction(fun () ->
            for s in sources do
                writer.InsertSource s

            for f in resolution.Files do
                writer.InsertFile f

            for o in resolution.Overrides do
                writer.InsertFileOverride o

            for sym in catalog.Symbols do
                writer.InsertSymbol sym

            for t in catalog.TypeDefs do
                writer.InsertConfigType t)

        // Phase B: parse scripts and write each entity's payload immediately,
        // freeing its (heavy) script nodes per file. Retain only the lightweight
        // EntityRecord for override resolution and the scripted trigger/effect
        // names. Parse + extract sequentially: CWTools' stringManager is shared
        // mutable state. is_effective is written true here, corrected in Phase B2.
        log "Parsing scripts and extracting entities..."

        let mutable entitiesRev: EntityRecord list = []
        let scriptedTriggers = System.Collections.Generic.HashSet<string>()
        let scriptedEffects = System.Collections.Generic.HashSet<string>()

        writer.InTransaction(fun () ->
            for file in scriptFiles do
                match Parsing.parseFile file.AbsolutePath with
                | Result.Error err ->
                    parseErrors <-
                        { FileId = file.FileId
                          Message = err.Message
                          Line = err.Line
                          Col = err.Col }
                        :: parseErrors

                    failedFileIds <- Set.add file.FileId failedFileIds
                | Result.Ok parsed ->
                    let payloads =
                        match coreExtractorFor adapter file.Folder with
                        | Some EventsFolder -> Events.extract lookups idGen file parsed
                        | Some MissionsFolder -> Missions.extract lookups idGen file parsed
                        | Some DecisionsFolder -> Decisions.extract lookups idGen file parsed
                        | Some EventModifiersFolder -> Modifiers.extract "event_modifier" lookups idGen file parsed
                        | Some StaticModifiersFolder -> Modifiers.extract "static_modifier" lookups idGen file parsed
                        | Some TriggeredModifiersFolder ->
                            Modifiers.extract "triggered_modifier" lookups idGen file parsed
                        | Some FocusTreesFolder -> FocusTrees.extract lookups idGen file parsed
                        | Some IdeasFolder -> Ideas.extract lookups idGen file parsed
                        | None ->
                            if request.SkipGeneric then
                                []
                            else
                                Generic.extract catalog.TypeDefs lookups idGen file parsed

                    for p in payloads do
                        writer.InsertPayload(p, true)
                        entitiesRev <- p.Entity :: entitiesRev

                        match p.Entity.EntityType with
                        | "scripted_trigger" -> scriptedTriggers.Add p.Entity.EntityKey |> ignore
                        | "scripted_effect" -> scriptedEffects.Add p.Entity.EntityKey |> ignore
                        | _ -> ())

        let entities = List.rev entitiesRev
        let entityCount = entities.Length
        log (sprintf "Entities: %d, failed files: %d" entityCount failedFileIds.Count)

        // Phase B2: entity-level override resolution, then correct is_effective
        // for the (minority) override losers.
        let entityRes =
            OverrideResolution.resolveEntities fileById loadOrderOf fileOverrideKindByLoser entities

        writer.InTransaction(fun () ->
            for o in entityRes.Overrides do
                writer.InsertEntityOverride o

            for KeyValue(entityId, effective) in entityRes.Effectiveness do
                if not effective then
                    writer.UpdateEntityEffective(entityId, false))

        let effectiveCount =
            entityRes.Effectiveness |> Map.toSeq |> Seq.filter snd |> Seq.length

        // Phase B3: Game defines (.lua files). These use LUA syntax, not
        // Paradox script, so they are parsed separately. Each key/value pair
        // becomes an entity (entity_type="define") so the agent can query
        // constants like NFocus.FOCUS_POINT_DAYS or NCountry.CORE_TIME_SIZE.
        // Two layout conventions exist:
        //   EU4:  common/defines.lua (single file, flat categories)
        //   HOI4: common/defines/*.lua (one or more files in a directory)
        let definesEntities =
            let candidates =
                sources
                |> List.collect (fun source ->
                    [ // EU4 convention: common/defines.lua
                      let p = Path.Combine(source.RootPath, "common/defines.lua")
                      if File.Exists p then
                          let relPath =
                              Path.GetRelativePath(source.RootPath, p)
                                  .Replace('\\', '/')
                                  .ToLowerInvariant()

                          [ source, p, relPath ]
                      else
                          []
                      // HOI4 convention: common/defines/*.lua
                      let dir = Path.Combine(source.RootPath, "common/defines")

                      if Directory.Exists dir then
                          Directory.EnumerateFiles(dir, "*.lua", SearchOption.AllDirectories)
                          |> Seq.map (fun absPath ->
                              let relPath =
                                  Path.GetRelativePath(source.RootPath, absPath)
                                      .Replace('\\', '/')
                                      .ToLowerInvariant()

                              source, absPath, relPath)
                          |> List.ofSeq
                      else
                          [] ]
                    |> List.concat)

            // File-level shadowing: same relative path → highest load order wins.
            candidates
            |> List.groupBy (fun (_, _, relPath) -> relPath)
            |> List.collect (fun (_, group) ->
                let winnerSource, winnerPath, _ =
                    group |> List.maxBy (fun (s, _, _) -> s.LoadOrder)

                Defines.parse winnerPath
                |> List.map (fun (key, value) -> winnerSource, key, value))
            // Key-level shadowing: same key → highest load order wins.
            |> List.groupBy (fun (_, key, _) -> key)
            |> List.map (fun (_, group) ->
                group |> List.maxBy (fun (s, _, _) -> s.LoadOrder))

        let mutable definesCount = 0

        writer.InTransaction(fun () ->
            for source, key, value in definesEntities do
                writer.InsertDefine(key, value, source.SourceId)
                definesCount <- definesCount + 1)

        if definesCount > 0 then
            log (sprintf "Defines: %d" definesCount)

        // Phase C: derive the reference / causal graph by reading the just-written
        // script nodes back from the DB one entity at a time, instead of holding
        // every entity's nodes in memory. The references themselves are small
        // (same footprint as before); only the script nodes were the cost.
        let scriptedTriggerSet = Set.ofSeq scriptedTriggers
        let scriptedEffectSet = Set.ofSeq scriptedEffects
        let optionNodeIdsByEntity = writer.ReadOptionNodeIds()
        let references = ResizeArray<ReferenceRow>()

        writer.IterEntityNodesForRefs(fun entityId entityType nodes ->
            let optionNodeIds =
                Map.tryFind entityId optionNodeIdsByEntity |> Option.defaultValue Set.empty

            for r in
                ReferenceExtractor.fromEntity
                    adapter.RefKeyRules
                    scriptedTriggerSet
                    scriptedEffectSet
                    optionNodeIds
                    entityId
                    entityType
                    nodes do
                references.Add r)

        log (sprintf "References: %d" references.Count)

        writer.InTransaction(fun () ->
            for r in references do
                writer.InsertReference r)

        // The script payloads, entity records and references are no longer
        // reachable; reclaim before localisation allocates its (large) row set.
        System.GC.Collect()

        // Phase D: localisation (independent of the script graph), parsed and
        // written only now so it never coexists with the script payloads.
        log "Parsing localisation..."

        let locRows =
            locFiles
            |> List.collect (fun file ->
                match adapter.LocFileLanguage file.RelativePath with
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

        writer.InTransaction(fun () ->
            for row in locRows do
                let isEffective =
                    Map.tryFind row.LocId locRes.Effectiveness |> Option.defaultValue true

                writer.InsertLocRow(row, isEffective)

            for o in locRes.Overrides do
                writer.InsertLocOverride o)

        let locCount = locRows.Length

        // Parse failures (script + loc) recorded together at the end, preserving
        // the original insertion order (loc-then-script, from the prepends above).
        writer.InTransaction(fun () ->
            for fileId in failedFileIds do
                writer.UpdateFileParseStatus(fileId, ParseFailed)

            for e in parseErrors do
                writer.InsertParseError e)

        let meta =
            [ "indexer_version", AppInfo.Version
              "game_id", adapter.GameId
              "config_repo_path", request.ConfigDir
              "languages", String.concat "," languages
              "created_utc", System.DateTime.UtcNow.ToString("o") ]

        log "Building indexes and FTS..."
        let violations = writer.Finalize(meta, request.WithFts)

        Result.Ok
            { SourceCount = sources.Length
              FileCount = resolution.Files.Length
              EntityCount = entityCount
              EffectiveEntityCount = effectiveCount
              LocCount = locCount
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

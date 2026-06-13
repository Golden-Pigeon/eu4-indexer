namespace Eu4Indexer.Core.Extractors

open Eu4Indexer.Core
open CWTools.Process
open Support

/// Config-driven extractor for everything outside the core four: matches
/// files to cwtools type definitions (paths, skip_root_key, type_per_file,
/// name_field, type_key_filter) and emits one entity per matched block.
module Generic =

    let private matchesSpec (spec: SkipRootSpec) (key: string) =
        let lower = key.ToLowerInvariant()

        match spec with
        | SkipAny -> true
        | SkipSpecific k -> k.ToLowerInvariant() = lower
        | SkipMultiple(ks, shouldMatch) ->
            let contains = ks |> List.exists (fun k -> k.ToLowerInvariant() = lower)
            contains = shouldMatch

    /// Does this type definition apply to the given file?
    let private typeMatchesFile (typeDef: ConfigTypeInfo) (file: GameFile) =
        let pathMatch =
            typeDef.Paths
            |> List.exists (fun p ->
                let p = p.Replace('\\', '/').ToLowerInvariant().TrimEnd('/')

                if typeDef.PathStrict then
                    file.RelativePath.StartsWith(p + "/")
                    && not (file.RelativePath.Substring(p.Length + 1).Contains '/')
                else
                    file.RelativePath.StartsWith(p + "/"))

        let fileMatch =
            match typeDef.PathFile with
            | Some f -> System.String.Equals(file.FileName, f, System.StringComparison.OrdinalIgnoreCase)
            | None -> true

        let extMatch =
            match typeDef.PathExtension with
            | Some ext ->
                let ext = if ext.StartsWith "." then ext else "." + ext
                file.FileName.ToLowerInvariant().EndsWith(ext.ToLowerInvariant())
            | None -> true

        pathMatch && fileMatch && extMatch

    /// Candidate entity nodes after descending skip_root_key levels.
    let private candidateNodes (typeDef: ConfigTypeInfo) (root: Node) =
        let rec descend (specs: SkipRootSpec list) (nodes: (int * Node) list) =
            match specs with
            | [] -> nodes
            | spec :: rest ->
                nodes
                |> List.collect (fun (stmtIndex, n) ->
                    if matchesSpec spec n.Key then
                        n.Children |> List.mapi (fun i c -> stmtIndex + i, c)
                    else
                        [])
                |> descend rest

        descend typeDef.SkipRootKeys (topLevelNodes root)

    let private keyFilterAccepts (typeDef: ConfigTypeInfo) (key: string) =
        let lower = key.ToLowerInvariant()

        let filterOk =
            match typeDef.TypeKeyFilter with
            | Some(keys, negate) ->
                let contains = keys |> List.exists (fun k -> k.ToLowerInvariant() = lower)
                contains <> negate
            | None -> true

        let startsOk =
            match typeDef.StartsWith with
            | Some prefix -> lower.StartsWith(prefix.ToLowerInvariant())
            | None -> true

        filterOk && startsOk

    let private locsFor (typeDef: ConfigTypeInfo) (entityId: int64) (entityKey: string) =
        typeDef.LocMappings
        |> List.map (fun m ->
            { EntityId = entityId
              Role = m.Role
              LocKey = m.Prefix + entityKey + m.Suffix })
        |> List.distinctBy (fun l -> l.Role)

    let extract
        (typeDefs: ConfigTypeInfo list)
        (lookups: ScriptTree.TagLookups)
        (idGen: IdGen)
        (file: GameFile)
        (parsed: Parsing.ParsedFile)
        : EntityPayload list =

        typeDefs
        |> List.filter (fun t -> typeMatchesFile t file)
        |> List.collect (fun typeDef ->
            if typeDef.TypePerFile then
                let key = System.IO.Path.GetFileNameWithoutExtension file.FileName

                let entity =
                    { EntityId = idGen.NextEntityId()
                      EntityType = typeDef.TypeName
                      EntityKey = key
                      FileId = file.FileId
                      SourceId = file.SourceId
                      StartLine = 1
                      EndLine = parsed.Lines.Length
                      StmtIndex = 0
                      Subtypes = []
                      RawText = Parsing.sliceLines parsed.Lines 1 parsed.Lines.Length
                      IsEffective = true }

                let nodes = ScriptTree.flatten lookups idGen.NextNodeId entity.EntityId parsed.Root

                [ { EntityPayload.create entity nodes with
                      EntityLocs = locsFor typeDef entity.EntityId key } ]
            else
                candidateNodes typeDef parsed.Root
                |> List.filter (fun (_, n) -> keyFilterAccepts typeDef n.Key)
                |> List.map (fun (stmtIndex, node) ->
                    let key =
                        match typeDef.NameField with
                        | Some field -> leafText node field |> Option.defaultValue node.Key
                        | None -> node.Key

                    let entity =
                        makeEntity idGen file parsed.Lines typeDef.TypeName key [] stmtIndex node

                    let nodes = ScriptTree.flatten lookups idGen.NextNodeId entity.EntityId node

                    { EntityPayload.create entity nodes with
                        EntityLocs = locsFor typeDef entity.EntityId key }))

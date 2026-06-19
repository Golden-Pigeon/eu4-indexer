namespace Eu4Indexer.Core

/// Entity-level and localisation-key-level override resolution. Pure
/// functions: take everything extracted (winners and losers), decide what is
/// effective, and emit explicit override records.
module OverrideResolution =

    /// Game load order of an entity within its folder: files load in ASCII
    /// filename order (merged across sources), statements in file order. A
    /// mod's zz_x.txt therefore beats vanilla's aa_x.txt regardless of mod
    /// load order; load order only breaks exact ties.
    let private entityOrderKey (files: Map<int, GameFile>) (loadOrderOf: Map<int, int>) (e: EntityRecord) =
        let file = files[e.FileId]
        file.Folder, file.FileName.ToLowerInvariant(), file.RelativePath, loadOrderOf[e.SourceId], e.StmtIndex

    type EntityResolution =
        { /// entity id -> effective?
          Effectiveness: Map<int64, bool>
          Overrides: EntityOverride list }

    let resolveEntities
        (files: Map<int, GameFile>)
        (loadOrderOf: Map<int, int>)
        (fileOverrideKind: Map<int, FileOverrideKind>)
        (entities: EntityRecord list)
        : EntityResolution =

        let fromEffectiveFiles, fromIneffectiveFiles =
            entities |> List.partition (fun e -> files[e.FileId].IsEffective)

        // among effective files: same (type, key) -> last definition wins
        let groups =
            fromEffectiveFiles
            |> List.groupBy (fun e -> e.EntityType, e.EntityKey.ToLowerInvariant())

        let redefinitionOverrides, effectiveLosers =
            groups
            |> List.collect (fun (_, group) ->
                match group |> List.sortByDescending (entityOrderKey files loadOrderOf) with
                | []
                | [ _ ] -> []
                | winner :: losers ->
                    losers
                    |> List.map (fun loser ->
                        { Kind = Redefinition
                          EntityType = loser.EntityType
                          EntityKey = loser.EntityKey
                          LoserEntityId = loser.EntityId
                          WinnerEntityId = Some winner.EntityId
                          WinnerSourceId = Some winner.SourceId
                          LoserSourceId = loser.SourceId
                          IdenticalContent = winner.RawText = loser.RawText },
                        loser.EntityId))
            |> List.unzip

        // winner per (type, key) among effective files, for linking shadowed losers
        let winnerByKey =
            let loserIds = Set.ofList effectiveLosers

            fromEffectiveFiles
            |> List.filter (fun e -> not (Set.contains e.EntityId loserIds))
            |> List.map (fun e -> (e.EntityType, e.EntityKey.ToLowerInvariant()), e)
            |> Map.ofList

        // entities in shadowed/wiped files lose to the same-key entity that
        // remains effective, when one exists
        let fileLevelOverrides =
            fromIneffectiveFiles
            |> List.map (fun loser ->
                let winner =
                    Map.tryFind (loser.EntityType, loser.EntityKey.ToLowerInvariant()) winnerByKey

                let kind =
                    match Map.tryFind loser.FileId fileOverrideKind with
                    | Some FileReplacePath -> EntityReplacePath
                    | _ -> EntityFileShadow

                { Kind = kind
                  EntityType = loser.EntityType
                  EntityKey = loser.EntityKey
                  LoserEntityId = loser.EntityId
                  WinnerEntityId = winner |> Option.map (fun w -> w.EntityId)
                  WinnerSourceId = winner |> Option.map (fun w -> w.SourceId)
                  LoserSourceId = loser.SourceId
                  IdenticalContent =
                    winner
                    |> Option.map (fun w -> w.RawText = loser.RawText)
                    |> Option.defaultValue false })

        let ineffectiveIds =
            Set.ofList (effectiveLosers @ (fromIneffectiveFiles |> List.map (fun e -> e.EntityId)))

        { Effectiveness =
            entities
            |> List.map (fun e -> e.EntityId, not (Set.contains e.EntityId ineffectiveIds))
            |> Map.ofList
          Overrides = redefinitionOverrides @ fileLevelOverrides }

    type LocResolution =
        { Effectiveness: Map<int64, bool>
          Overrides: LocOverride list }

    /// Localisation key resolution: among rows from effective files, ordering
    /// is (replace/ last and winning, then mod load order, then read order);
    /// the last row wins. Rows from shadowed files lose at file level.
    let resolveLocalisation
        (files: Map<int, GameFile>)
        (loadOrderOf: Map<int, int>)
        (fileOverrideKind: Map<int, FileOverrideKind>)
        (rows: LocRow list)
        : LocResolution =

        let fromEffectiveFiles, fromIneffectiveFiles =
            rows |> List.partition (fun r -> files[r.FileId].IsEffective)

        let keyLevel =
            fromEffectiveFiles
            |> List.groupBy (fun r -> r.LocKey, r.Language)
            |> List.collect (fun (_, group) ->
                match
                    group
                    |> List.sortByDescending (fun r -> r.IsReplace, loadOrderOf[r.SourceId], r.LocId)
                with
                | []
                | [ _ ] -> []
                | winner :: losers ->
                    losers
                    |> List.map (fun loser ->
                        let kind =
                            if loser.SourceId = winner.SourceId then SameSourceDuplicate
                            elif winner.IsReplace && not loser.IsReplace then ReplaceDir
                            else LaterSource

                        { LocKey = loser.LocKey
                          Language = loser.Language
                          Kind = kind
                          LoserLocId = loser.LocId
                          WinnerLocId = Some winner.LocId
                          WinnerSourceId = Some winner.SourceId
                          LoserSourceId = loser.SourceId
                          IdenticalContent = winner.Value = loser.Value },
                        loser.LocId))

        let keyLevelOverrides = keyLevel |> List.map fst
        let keyLevelLoserIds = keyLevel |> List.map snd |> Set.ofList

        let winnerByKey =
            fromEffectiveFiles
            |> List.filter (fun r -> not (Set.contains r.LocId keyLevelLoserIds))
            |> List.map (fun r -> (r.LocKey, r.Language), r)
            |> Map.ofList

        // Entries from non-effective (shadowed/replaced) files: unlike script
        // files, localisation is additive — the game engine loads ALL .yml
        // files from all sources.  A later file shadows an earlier one only
        // for the keys it actually defines; keys present only in the earlier
        // file remain visible.  We therefore only retire a row from a
        // non-effective file when an effective winner for the same (key,
        // language) exists; otherwise the row stays effective.
        let fileLevelOverrides, fileLevelLoserIds =
            fromIneffectiveFiles
            |> List.map (fun loser ->
                match Map.tryFind (loser.LocKey, loser.Language) winnerByKey with
                | None -> None, None
                | Some winner ->
                    let kind =
                        match Map.tryFind loser.FileId fileOverrideKind with
                        | Some FileReplacePath -> LocReplacePath
                        | _ -> LocFileShadow

                    let ov =
                        { LocKey = loser.LocKey
                          Language = loser.Language
                          Kind = kind
                          LoserLocId = loser.LocId
                          WinnerLocId = Some winner.LocId
                          WinnerSourceId = Some winner.SourceId
                          LoserSourceId = loser.SourceId
                          IdenticalContent = winner.Value = loser.Value }

                    Some ov, Some loser.LocId)
            |> List.unzip

        let fileLevelOverrides = fileLevelOverrides |> List.choose id
        let fileLevelLoserIds = fileLevelLoserIds |> List.choose id |> Set.ofList

        let ineffectiveIds =
            Set.union keyLevelLoserIds fileLevelLoserIds

        { Effectiveness =
            rows
            |> List.map (fun r -> r.LocId, not (Set.contains r.LocId ineffectiveIds))
            |> Map.ofList
          Overrides = keyLevelOverrides @ fileLevelOverrides }

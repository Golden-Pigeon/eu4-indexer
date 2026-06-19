namespace Eu4Indexer.Core.Extractors

open Eu4Indexer.Core
open Support

/// Extracts decisions from decisions/ files (country_decisions wrapper block).
module Decisions =

    let extract
        (lookups: ScriptTree.TagLookups)
        (idGen: IdGen)
        (file: GameFile)
        (parsed: Parsing.ParsedFile)
        : EntityPayload list =

        topLevelNodes parsed.Root
        |> List.filter (fun (_, wrapper) -> wrapper.Key.ToLowerInvariant() = "country_decisions")
        |> List.collect (fun (stmtIndex, wrapper) ->
            wrapper.Children
            |> List.mapi (fun i decisionNode ->
                let key = decisionNode.Key

                let entity =
                    makeEntity idGen file parsed.Lines "decision" key [] (stmtIndex + i) decisionNode

                let nodes =
                    ScriptTree.flatten lookups idGen.NextNodeId entity.EntityId decisionNode

                let details =
                    { EntityId = entity.EntityId
                      Major = leafBool decisionNode "major"
                      AiImportance = leafFloat decisionNode "ai_importance" }

                let locs =
                    [ { EntityId = entity.EntityId
                        Role = "title"
                        LocKey = key + "_title" }
                      { EntityId = entity.EntityId
                        Role = "desc"
                        LocKey = key + "_desc" } ]

                let p = EntityPayload.eu4 entity nodes
                { p with
                    GameDetails =
                        Eu4Game
                            { Event = None; EventOptions = []
                              Mission = None; MissionReqs = []
                              Decision = Some details }
                    EntityLocs = locs }))

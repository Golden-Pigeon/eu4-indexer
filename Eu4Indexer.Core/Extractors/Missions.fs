namespace Eu4Indexer.Core.Extractors

open Eu4Indexer.Core
open CWTools.Process
open Support

/// Extracts mission series and their missions from missions/ files.
/// Each top-level block is a series; its non-reserved child nodes are missions.
module Missions =

    /// Series-level keys that are not missions.
    let private seriesReservedKeys =
        Set.ofList [ "slot"; "generic"; "ai"; "potential"; "potential_on_load"; "has_country_shield" ]

    let extract
        (lookups: ScriptTree.TagLookups)
        (idGen: IdGen)
        (file: GameFile)
        (parsed: Parsing.ParsedFile)
        : EntityPayload list =

        topLevelNodes parsed.Root
        |> List.collect (fun (stmtIndex, seriesNode) ->
            let seriesKey = seriesNode.Key
            let slot = leafInt seriesNode "slot"
            let isGeneric = leafBool seriesNode "generic"
            let ai = leafText seriesNode "ai" |> Option.map ((=) "yes") |> Option.defaultValue true

            // the series itself: queryable via its potential tree
            let seriesEntity =
                makeEntity idGen file parsed.Lines "mission_series" seriesKey [] stmtIndex seriesNode

            let seriesNodes =
                ScriptTree.flatten lookups idGen.NextNodeId seriesEntity.EntityId seriesNode

            let seriesPayload = EntityPayload.create seriesEntity seriesNodes

            let missionPayloads =
                seriesNode.Children
                |> List.filter (fun c -> not (Set.contains (c.Key.ToLowerInvariant()) seriesReservedKeys))
                |> List.mapi (fun i missionNode ->
                    let missionKey = missionNode.Key

                    let entity =
                        makeEntity idGen file parsed.Lines "mission" missionKey [] (stmtIndex + i) missionNode

                    let nodes =
                        ScriptTree.flatten lookups idGen.NextNodeId entity.EntityId missionNode

                    let requirements =
                        missionNode.Child "required_missions"
                        |> Option.map (fun rm ->
                            rm.LeafValues
                            |> Seq.map (fun lv ->
                                { EntityId = entity.EntityId
                                  RequiredMission = lv.ValueText })
                            |> List.ofSeq
                            |> List.distinctBy (fun r -> r.RequiredMission))
                        |> Option.defaultValue []

                    let details =
                        { EntityId = entity.EntityId
                          SeriesKey = seriesKey
                          Slot = slot
                          IsGeneric = isGeneric
                          Ai = ai
                          Icon = leafText missionNode "icon"
                          Position = leafInt missionNode "position"
                          HasHighlight = missionNode.Has "provinces_to_highlight" }

                    let locs =
                        [ { EntityId = entity.EntityId
                            Role = "title"
                            LocKey = missionKey + "_title" }
                          { EntityId = entity.EntityId
                            Role = "desc"
                            LocKey = missionKey + "_desc" } ]

                    { EntityPayload.create entity nodes with
                        MissionDetails = Some details
                        MissionRequirements = requirements
                        EntityLocs = locs })

            seriesPayload :: missionPayloads)

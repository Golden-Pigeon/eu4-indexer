namespace Eu4Indexer.Core.Extractors

open Eu4Indexer.Core
open CWTools.Process
open Support

/// Extracts focus_tree and focus entities from common/national_focus/ files.
/// Each top-level block is a focus tree; its child `focus` blocks are individual
/// foci with prerequisites, mutually exclusive groups, and effects.
module FocusTrees =

    /// Keys inside a focus tree that are structural (not an individual focus).
    let private treeReservedKeys =
        Set.ofList [ "id"; "country"; "default"; "initial_show_position"; "shortcut"; "shared_focus" ]

    let extract
        (lookups: ScriptTree.TagLookups)
        (idGen: IdGen)
        (file: GameFile)
        (parsed: Parsing.ParsedFile)
        : EntityPayload list =

        topLevelNodes parsed.Root
        |> List.collect (fun (stmtIndex, treeNode) ->
            let treeId =
                leafText treeNode "id"
                |> Option.defaultValue (sprintf "<anonymous_focus_tree:%s:%d>" file.RelativePath stmtIndex)

            // Focus tree entity: queryable via its script tree
            let treeEntity =
                makeEntity idGen file parsed.Lines "focus_tree" treeId [] stmtIndex treeNode

            let treeNodes =
                ScriptTree.flatten lookups idGen.NextNodeId treeEntity.EntityId treeNode

            let treePayload =
                { EntityPayload.generic treeEntity treeNodes with
                    EntityLocs =
                        [ { EntityId = treeEntity.EntityId; Role = "name"; LocKey = treeId } ] }

            let focusPayloads =
                treeNode.Children
                |> List.filter (fun c ->
                    (c.Key.ToLowerInvariant()) = "focus"
                    && not (Set.contains (c.Key.ToLowerInvariant()) treeReservedKeys))
                |> List.mapi (fun i focusNode ->
                    let focusId =
                        leafText focusNode "id"
                        |> Option.defaultValue (sprintf "<anonymous_focus:%s:%d>" file.RelativePath (stmtIndex + i))

                    let entity =
                        makeEntity idGen file parsed.Lines "focus" focusId [] (stmtIndex + i) focusNode

                    let nodes =
                        ScriptTree.flatten lookups idGen.NextNodeId entity.EntityId focusNode

                    let prerequisites =
                        focusNode.Child "prerequisite"
                        |> Option.map (fun r ->
                            r.Leaves
                            |> Seq.choose (fun l ->
                                if l.Key.ToLowerInvariant() = "focus" then
                                    Some { EntityId = entity.EntityId; RequiredFocus = l.ValueText }
                                else
                                    None)
                            |> List.ofSeq
                            |> List.distinctBy (fun r -> r.RequiredFocus))
                        |> Option.defaultValue []

                    let details =
                        { EntityId = entity.EntityId
                          TreeId = treeId
                          Icon = leafText focusNode "icon"
                          X = leafInt focusNode "x"
                          Y = leafInt focusNode "y"
                          RelativePositionId = leafText focusNode "relative_position_id" }

                    let locs =
                        [ { EntityId = entity.EntityId; Role = "title"; LocKey = focusId }
                          { EntityId = entity.EntityId; Role = "desc"; LocKey = focusId + "_desc" } ]

                    let p = EntityPayload.hoi4 entity nodes
                    { p with
                        GameDetails =
                            Hoi4Game
                                { Event = None; EventOptions = []
                                  Focus = Some details
                                  FocusReqs = prerequisites
                                  Decision = None }
                        EntityLocs = locs })

            treePayload :: focusPayloads)

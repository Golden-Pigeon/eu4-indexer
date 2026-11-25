namespace Eu4Indexer.Core.Extractors

open Eu4Indexer.Core
open CWTools.Process
open Support

/// Extracts idea entities from common/ideas/ files.
/// The top-level `ideas` block contains categories (e.g. `economy`, `country`);
/// each category's children are individual ideas with modifiers and conditions.
module Ideas =

    /// Keys inside an idea category that are structural (not idea definitions).
    let private categoryKeys =
        Set.ofList [ "law"; "designer"; "use_list_view"; "slot"; "default"; "research_bonus" ]

    let extract
        (lookups: ScriptTree.TagLookups)
        (idGen: IdGen)
        (file: GameFile)
        (parsed: Parsing.ParsedFile)
        : EntityPayload list =

        let categoryChildren (ideasRoot: Node) =
            ideasRoot.Children |> List.collect (fun catNode -> catNode.Children)

        let ideaNodes =
            parsed.Root.AllArray
            |> Seq.choose (fun child ->
                match child with
                | NodeC n -> Some n
                | _ -> None)
            |> List.ofSeq
            |> List.collect categoryChildren
            |> List.filter (fun ideaNode ->
                not (Set.contains (ideaNode.Key.ToLowerInvariant()) categoryKeys))

        ideaNodes
        |> List.mapi (fun i ideaNode ->
            let key = ideaNode.Key

            let entity =
                makeEntity idGen file parsed.Lines "idea" key [] i ideaNode

            let nodes =
                ScriptTree.flatten lookups idGen.NextNodeId entity.EntityId ideaNode

            let locs =
                [ { EntityId = entity.EntityId; Role = "name"; LocKey = key } ]

            { EntityPayload.generic entity nodes with
                ModifierValues = inlineModifierValues lookups entity.EntityId ideaNode
                EntityLocs = locs })

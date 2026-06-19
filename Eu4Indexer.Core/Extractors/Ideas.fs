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

            let modifierValues =
                match ideaNode.Child "modifier" with
                | Some modNode ->
                    modNode.Leaves
                    |> Seq.map (fun l ->
                        { EntityId = entity.EntityId
                          ModifierKey = l.Key
                          Value = l.ValueText
                          SymbolId = Map.tryFind (l.Key.ToLowerInvariant()) lookups.Modifier })
                    // repeated keys: last definition wins, like the game
                    |> Seq.fold (fun acc mv -> Map.add mv.ModifierKey mv acc) Map.empty
                    |> Map.toList
                    |> List.map snd
                | None -> []

            let locs =
                [ { EntityId = entity.EntityId; Role = "name"; LocKey = key } ]

            { EntityPayload.generic entity nodes with
                ModifierValues = modifierValues
                EntityLocs = locs })

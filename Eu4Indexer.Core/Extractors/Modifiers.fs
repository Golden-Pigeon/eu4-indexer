namespace Eu4Indexer.Core.Extractors

open Eu4Indexer.Core
open Support

/// Extracts the three modifier entity families. Every top-level block is a
/// modifier; plain leaves are modifier key/value pairs, while triggered
/// modifiers additionally carry potential/trigger condition blocks (those land
/// in the script tree with trigger context via the normal flattening rules).
module Modifiers =

    /// Leaf keys of triggered/static modifiers that are not modifier effects.
    let private nonModifierLeaves =
        Set.ofList [ "picture"; "religion"; "secondary_religion" ]

    let extract
        (entityType: string)
        (lookups: ScriptTree.TagLookups)
        (idGen: IdGen)
        (file: GameFile)
        (parsed: Parsing.ParsedFile)
        : EntityPayload list =

        topLevelNodes parsed.Root
        |> List.map (fun (stmtIndex, node) ->
            let key = node.Key

            let entity =
                makeEntity idGen file parsed.Lines entityType key [] stmtIndex node

            let nodes = ScriptTree.flatten lookups idGen.NextNodeId entity.EntityId node

            let modifierValues =
                node.Leaves
                |> Seq.filter (fun l ->
                    let lower = l.Key.ToLowerInvariant()
                    not (Set.contains lower nonModifierLeaves))
                |> Seq.map (fun l ->
                    { EntityId = entity.EntityId
                      ModifierKey = l.Key
                      Value = l.ValueText
                      SymbolId = Map.tryFind (l.Key.ToLowerInvariant()) lookups.Modifier })
                // repeated keys: last definition wins, like the game
                |> Seq.fold (fun acc mv -> Map.add mv.ModifierKey mv acc) Map.empty
                |> Map.toList
                |> List.map snd

            let locs =
                [ { EntityId = entity.EntityId
                    Role = "name"
                    LocKey = key } ]

            let p = EntityPayload.eu4 entity nodes
            { p with ModifierValues = modifierValues; EntityLocs = locs })

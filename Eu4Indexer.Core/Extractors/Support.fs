namespace Eu4Indexer.Core.Extractors

open Eu4Indexer.Core
open CWTools.Process

/// Shared plumbing for the entity extractors.
module Support =

    /// Globally-unique id allocators, shared by all extractors in one run.
    type IdGen =
        { NextEntityId: unit -> int64
          NextNodeId: unit -> int64 }

    let makeIdGen () =
        let mutable entityId = 0L
        let mutable nodeId = 0L

        { NextEntityId =
            fun () ->
                entityId <- entityId + 1L
                entityId
          NextNodeId =
            fun () ->
                nodeId <- nodeId + 1L
                nodeId }

    /// First leaf with the given key, if any (TagText returns "" for missing,
    /// which loses the present-but-empty distinction).
    let leafText (node: Node) (key: string) =
        node.Leaves
        |> Seq.tryFind (fun l -> l.Key = key)
        |> Option.map (fun l -> l.ValueText)

    let leafBool (node: Node) (key: string) =
        leafText node key |> Option.map (fun v -> v = "yes") |> Option.defaultValue false

    let leafInt (node: Node) (key: string) =
        leafText node key |> Option.bind (fun v ->
            match System.Int32.TryParse v with
            | true, i -> Some i
            | _ -> None)

    let leafFloat (node: Node) (key: string) =
        leafText node key |> Option.bind (fun v ->
            match System.Double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture) with
            | true, f -> Some f
            | _ -> None)

    /// Builds the EntityRecord for a top-level entity node.
    let makeEntity
        (idGen: IdGen)
        (file: GameFile)
        (lines: string[])
        (entityType: string)
        (entityKey: string)
        (subtypes: string list)
        (stmtIndex: int)
        (node: Node)
        =
        let startLine = node.Position.StartLine
        let endLine = node.Position.EndLine

        { EntityId = idGen.NextEntityId()
          EntityType = entityType
          EntityKey = entityKey
          FileId = file.FileId
          SourceId = file.SourceId
          StartLine = startLine
          EndLine = endLine
          StmtIndex = stmtIndex
          Subtypes = subtypes
          RawText = Parsing.sliceLines lines startLine endLine
          IsEffective = true }

    /// Iterate the top-level entity nodes of a parsed file with their
    /// statement ordinals (comments don't consume ordinals).
    let topLevelNodes (root: Node) =
        root.AllArray
        |> Seq.fold
            (fun (idx, acc) child ->
                match child with
                | NodeC n -> idx + 1, (idx, n) :: acc
                | LeafC _
                | LeafValueC _
                | ValueClauseC _ -> idx + 1, acc
                | CommentC _ -> idx, acc)
            (0, [])
        |> snd
        |> List.rev

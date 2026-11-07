namespace Eu4Indexer.Core

/// Derives the cross-reference / causal graph (the `refs` table) from the
/// already-extracted script nodes: which entity fires which event, sets or
/// checks which flag/variable, applies or checks which modifier, calls which
/// scripted trigger/effect, and which on_action fires which event.
///
/// Pure function over the in-memory payloads, mirroring OverrideResolution.
module ReferenceExtractor =

    open System

    /// (ref_kind, target_type, child field carrying the target name) keyed by
    /// the lowercased script key that produces the reference. Flags are
    /// scope-qualified by target_type so a country-flag check is never linked
    /// to a global-flag set. Passed in via GameAdapter.RefKeyRules so each game
    /// supplies its own trigger/effect vocabulary.
    let private unquote (s: string) =
        let t = s.Trim()

        if t.Length >= 2 && t.StartsWith "\"" && t.EndsWith "\"" then
            t.Substring(1, t.Length - 2)
        else
            t

    /// A usable target name: non-empty, not a bare boolean.
    let private validTarget (s: string option) : string option =
        match s with
        | Some raw ->
            let v = unquote raw

            if String.IsNullOrWhiteSpace v || v = "yes" || v = "no" then
                None
            else
                Some v
        | None -> None

    let private keyEquals (a: string option) (b: string) =
        match a with
        | Some k -> String.Equals(k, b, StringComparison.OrdinalIgnoreCase)
        | None -> false

    /// Edges for one entity from its flattened script nodes, read back from the
    /// DB (`nodes`) so the full node lists need not be held in memory.
    /// `optionNodeIds` are the entity's event-option clause node ids; `entityId`
    /// and `entityType` come from the `entities` row. `NodeKind`/`Context` on a
    /// RefNode are the raw DB strings.
    let fromEntity
        (keyRules: Map<string, string * string * string>)
        (scriptedTriggers: Set<string>)
        (scriptedEffects: Set<string>)
        (optionNodeIds: Set<int64>)
        (entityId: int64)
        (entityType: string)
        (nodes: RefNode list)
        : ReferenceRow list =

        let byId = nodes |> List.map (fun n -> n.NodeId, n) |> Map.ofList

        let childrenOf =
            nodes
            |> List.choose (fun n -> n.ParentId |> Option.map (fun p -> p, n))
            |> List.groupBy fst
            |> List.map (fun (p, xs) -> p, List.map snd xs)
            |> Map.ofList

        let rec ancestors (parentId: int64 option) acc =
            match parentId with
            | None -> acc
            | Some pid ->
                match Map.tryFind pid byId with
                | Some p -> ancestors p.ParentId (p :: acc)
                | None -> acc

        // (from_context, enclosing option clause node, negated) for a node.
        let nodeMeta (node: RefNode) =
            let anc = ancestors node.ParentId []

            let negCount =
                anc
                |> List.filter (fun a ->
                    match a.Key with
                    | Some k -> k.ToLowerInvariant() = "not"
                    | None -> false)
                |> List.length

            let optionNode = anc |> List.tryFind (fun a -> Set.contains a.NodeId optionNodeIds)

            let fromContext =
                match optionNode with
                | Some _ -> if node.Context = "trigger" then "option_trigger" else "option_effect"
                | None -> node.Context

            fromContext, (optionNode |> Option.map (fun a -> a.NodeId)), (negCount % 2 = 1)

        // Direct leaf value, or the named child of a clause (e.g. { id = X }).
        let targetsOf (node: RefNode) (childField: string) : string list =
            match node.NodeKind with
            | "leaf" -> validTarget node.Value |> Option.toList
            | "clause" ->
                let children = Map.tryFind node.NodeId childrenOf |> Option.defaultValue []
                let fields = childField.Split('|')
                let named =
                    fields
                    |> Array.filter ((<>) "$assignment")
                    |> Array.tryPick (fun field ->
                        children |> List.tryPick (fun c -> if keyEquals c.Key field then validTarget c.Value else None))
                match named with
                | Some value -> [ value ]
                | None when Array.contains "$assignment" fields ->
                    children
                    |> List.choose (fun c ->
                        match c.Key with
                        | Some key when not (Set.contains (key.ToLowerInvariant()) (set [ "value"; "tooltip"; "if_zero" ])) ->
                            validTarget (Some key)
                        | _ -> None)
                | None -> []
            | _ -> [] // "value" (bare value node)

        let makeRow node refKind targetType targetKey =
            let fromContext, optionNodeId, negated = nodeMeta node

            { FromEntityId = entityId
              FromContext = fromContext
              RefKind = refKind
              TargetType = targetType
              TargetKey = targetKey
              NodeId = node.NodeId
              OptionNodeId = optionNodeId
              Negated = negated }

        // 1) keyword-driven references (events, flags, variables, modifiers)
        let keywordRefs =
            nodes
            |> List.filter (fun node ->
                match node.ParentId |> Option.bind (fun id -> Map.tryFind id byId), node.Key with
                | Some parent, Some key when keyEquals parent.Key key ->
                    match Map.tryFind (key.ToLowerInvariant()) keyRules with
                    | Some(_, _, childField) -> not (childField.Split('|') |> Array.contains (key.ToLowerInvariant()))
                    | None -> true
                | _ -> true)
            |> List.collect (fun node ->
                match node.Key with
                | Some key ->
                    let rule =
                        let scoped =
                            node.ParentId
                            |> Option.bind (fun id -> Map.tryFind id byId)
                            |> Option.bind (fun parent -> parent.Key)
                            |> Option.bind (fun parentKey -> Map.tryFind (parentKey.ToLowerInvariant() + "/" + key.ToLowerInvariant()) keyRules)
                        scoped |> Option.orElseWith (fun () -> Map.tryFind (key.ToLowerInvariant()) keyRules)
                    match rule with
                    | Some(refKind, targetType, childField) ->
                        targetsOf node childField
                        |> List.map (fun t -> makeRow node refKind targetType t)
                    | None -> []
                | None -> [])

        // 2) scripted trigger / effect calls (key names defined by content)
        let isTriggerSide ctx =
            ctx = "trigger" || ctx = "mtth" || ctx = "ai_chance"

        let scriptedRefs =
            nodes
            |> List.choose (fun node ->
                match node.Key with
                | Some key when isTriggerSide node.Context && Set.contains key scriptedTriggers ->
                    Some(makeRow node "calls_scripted_trigger" "scripted_trigger" key)
                | Some key when node.Context = "effect" && Set.contains key scriptedEffects ->
                    Some(makeRow node "calls_scripted_effect" "scripted_effect" key)
                | _ -> None)

        // 3) on_action -> fired events (events = { id... }, random_events = { w = id })
        let onActionRefs =
            if not (entityType.ToLowerInvariant().Contains "on_action") then
                []
            else
                nodes
                |> List.filter (fun n ->
                    n.Depth = 0 && (keyEquals n.Key "events" || keyEquals n.Key "random_events"))
                |> List.collect (fun listNode ->
                    Map.tryFind listNode.NodeId childrenOf
                    |> Option.defaultValue []
                    |> List.choose (fun child ->
                        // bare value (events) or weighted leaf (random_events)
                        validTarget child.Value
                        |> Option.map (fun evt ->
                            { FromEntityId = entityId
                              FromContext = "effect"
                              RefKind = "on_action_fires"
                              TargetType = "event"
                              TargetKey = evt
                              NodeId = child.NodeId
                              OptionNodeId = None
                              Negated = false })))

        // Root metadata relations are scoped by the adapter, never by a common
        // field name alone (e.g. an event's folder is not a doctrine folder).
        let structuralRefs =
            nodes
            |> List.filter (fun n -> n.ParentId.IsNone && n.Context = "metadata")
            |> List.collect (fun node ->
                match node.Key with
                | None -> []
                | Some key ->
                    let ruleKey = sprintf "@%s/%s" (entityType.ToLowerInvariant()) (key.ToLowerInvariant())
                    match Map.tryFind ruleKey keyRules with
                    | None -> []
                    | Some(refKind, targetType, childField) ->
                        if node.NodeKind = "clause" && childField = "" then
                            Map.tryFind node.NodeId childrenOf
                            |> Option.defaultValue []
                            |> List.choose (fun child ->
                                if child.NodeKind = "value" then
                                    validTarget child.Value |> Option.map (makeRow child refKind targetType)
                                else None)
                        else
                            targetsOf node childField |> List.map (makeRow node refKind targetType))

        keywordRefs @ scriptedRefs @ onActionRefs @ structuralRefs
        |> List.distinctBy (fun r -> r.NodeId, r.RefKind, r.TargetType, r.TargetKey)

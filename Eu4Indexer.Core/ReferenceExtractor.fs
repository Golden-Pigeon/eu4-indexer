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
    /// to a global-flag set.
    let private keyRules: Map<string, string * string * string> =
        Map.ofList
            [ // fires_event: direct value or { id = X }
              "country_event", ("fires_event", "event", "id")
              "province_event", ("fires_event", "event", "id")
              "ruler_event", ("fires_event", "event", "id")
              "trade_node_event", ("fires_event", "event", "id")
              "siege_event", ("fires_event", "event", "id")
              // set/clear flags
              "set_country_flag", ("sets_flag", "country_flag", "flag")
              "clr_country_flag", ("sets_flag", "country_flag", "flag")
              "set_global_flag", ("sets_flag", "global_flag", "flag")
              "clr_global_flag", ("sets_flag", "global_flag", "flag")
              "set_province_flag", ("sets_flag", "province_flag", "flag")
              "clr_province_flag", ("sets_flag", "province_flag", "flag")
              "set_ruler_flag", ("sets_flag", "ruler_flag", "flag")
              "clr_ruler_flag", ("sets_flag", "ruler_flag", "flag")
              // check flags
              "has_country_flag", ("checks_flag", "country_flag", "flag")
              "has_global_flag", ("checks_flag", "global_flag", "flag")
              "has_province_flag", ("checks_flag", "province_flag", "flag")
              "has_ruler_flag", ("checks_flag", "ruler_flag", "flag")
              // set/change variables
              "set_variable", ("sets_variable", "variable", "which")
              "change_variable", ("sets_variable", "variable", "which")
              "add_to_variable", ("sets_variable", "variable", "which")
              "subtract_variable", ("sets_variable", "variable", "which")
              "multiply_variable", ("sets_variable", "variable", "which")
              "divide_variable", ("sets_variable", "variable", "which")
              // check variables
              "check_variable", ("checks_variable", "variable", "which")
              "has_variable", ("checks_variable", "variable", "which")
              "is_variable_equal", ("checks_variable", "variable", "which")
              // apply modifiers
              "add_country_modifier", ("applies_modifier", "modifier", "name")
              "add_permanent_province_modifier", ("applies_modifier", "modifier", "name")
              "add_province_modifier", ("applies_modifier", "modifier", "name")
              "add_ruler_modifier", ("applies_modifier", "modifier", "name")
              "add_province_triggered_modifier", ("applies_modifier", "modifier", "name")
              "add_country_triggered_modifier", ("applies_modifier", "modifier", "name")
              // check modifiers
              "has_country_modifier", ("checks_modifier", "modifier", "name")
              "has_province_modifier", ("checks_modifier", "modifier", "name")
              "has_ruler_modifier", ("checks_modifier", "modifier", "name") ]

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
        let targetOf (node: RefNode) (childField: string) : string option =
            match node.NodeKind with
            | "leaf" -> validTarget node.Value
            | "clause" ->
                Map.tryFind node.NodeId childrenOf
                |> Option.defaultValue []
                |> List.tryPick (fun c -> if keyEquals c.Key childField then validTarget c.Value else None)
            | _ -> None // "value" (bare value node)

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
            |> List.choose (fun node ->
                match node.Key with
                | Some key ->
                    match Map.tryFind (key.ToLowerInvariant()) keyRules with
                    | Some(refKind, targetType, childField) ->
                        targetOf node childField
                        |> Option.map (fun t -> makeRow node refKind targetType t)
                    | None -> None
                | None -> None)

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

        keywordRefs @ scriptedRefs @ onActionRefs

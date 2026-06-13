namespace Eu4Indexer.Core

/// Flattens a CWTools node tree into script_nodes rows: depth-first, with
/// per-node context assignment (trigger/effect/mtth/ai_chance/metadata) and
/// symbol tagging against the config dictionaries.
module ScriptTree =

    open System
    open System.Text.RegularExpressions
    open CWTools.Parser.Types
    open CWTools.Process

    type TagLookups =
        { Trigger: Map<string, int>
          Effect: Map<string, int>
          Modifier: Map<string, int> }

    let makeLookups (triggers: (string * int) seq) (effects: (string * int) seq) (modifiers: (string * int) seq) =
        { Trigger = Map.ofSeq triggers
          Effect = Map.ofSeq effects
          Modifier = Map.ofSeq modifiers }

    let lookupsFromCatalog (catalog: ConfigCatalog.Catalog) =
        { Trigger = catalog.TriggerLookup
          Effect = catalog.EffectLookup
          Modifier = catalog.ModifierLookup }

    /// Keys that switch the context of their block regardless of parent context.
    /// Covers events, missions, decisions, modifiers and the common patterns of
    /// generic common/ types.
    let private contextSwitches =
        Map.ofList
            [ "trigger", TriggerCtx
              "potential", TriggerCtx
              "allow", TriggerCtx
              "limit", TriggerCtx
              "provinces_to_highlight", TriggerCtx
              "major_trigger", TriggerCtx
              "can_start", TriggerCtx
              "can_stop", TriggerCtx
              "can_end", TriggerCtx
              "immediate", EffectCtx
              "effect", EffectCtx
              "option", EffectCtx
              "after", EffectCtx
              "on_start", EffectCtx
              "on_end", EffectCtx
              "mean_time_to_happen", MtthCtx
              "ai_chance", AiChanceCtx
              "ai_will_do", AiChanceCtx
              "ai_weight", AiChanceCtx ]

    /// Keys that stay metadata even inside a switched context (e.g. option
    /// name / ai_chance handled by their own switch).
    let private metadataLeavesInsideOption = Set.ofList [ "name"; "goto" ]

    let private childContext (parentCtx: ScriptContext) (parentKey: string option) (key: string) =
        let lower = key.ToLowerInvariant()

        match Map.tryFind lower contextSwitches with
        | Some ctx -> ctx
        | None ->
            match parentKey with
            | Some pk when pk.ToLowerInvariant() = "option" && Set.contains lower metadataLeavesInsideOption ->
                MetadataCtx
            | _ -> parentCtx

    let private datePattern = Regex(@"^-?\d{1,4}\.\d{1,2}\.\d{1,2}$", RegexOptions.Compiled)

    let private valueKindOf (value: Value) =
        match value with
        | Value.Int _ -> IntValue
        | Value.Float _ -> FloatValue
        | Value.Bool _ -> BoolValue
        | Value.String _
        | Value.QString _ ->
            // dates parse as plain strings; sniff the d.m.y shape
            if datePattern.IsMatch(value.ToRawString()) then DateValue else StringValue
        | Value.Clause _ -> StringValue

    /// Symbol dictionary used per context: conditions live in trigger contexts
    /// (incl. mtth/ai weighting modifiers), commands in effect contexts, and
    /// raw modifier keys appear in metadata position (modifier-type entities).
    let private tagSymbol (lookups: TagLookups) (ctx: ScriptContext) (key: string) =
        let lower = key.ToLowerInvariant()

        match ctx with
        | TriggerCtx
        | MtthCtx
        | AiChanceCtx -> Map.tryFind lower lookups.Trigger
        | EffectCtx -> Map.tryFind lower lookups.Effect
        | MetadataCtx -> Map.tryFind lower lookups.Modifier

    /// Flatten all descendants of `entityNode` (the node itself is represented
    /// by the entities row). `nextId` allocates globally unique node ids.
    let flatten (lookups: TagLookups) (nextId: unit -> int64) (entityId: int64) (entityNode: Node) : ScriptNodeRow list =

        let acc = ResizeArray<ScriptNodeRow>()

        let rec walkChildren (parent: Node) (parentRowId: int64 option) (parentCtx: ScriptContext) (depth: int) =
            let parentKey = if depth = 0 then None else Some parent.Key
            let mutable sortOrder = 0

            for child in parent.AllArray do
                match child with
                | NodeC node ->
                    let ctx = childContext parentCtx parentKey node.Key
                    let id = nextId ()

                    acc.Add
                        { NodeId = id
                          EntityId = entityId
                          ParentId = parentRowId
                          Depth = depth
                          SortOrder = sortOrder
                          NodeKind = ClauseNode
                          Context = ctx
                          Key = Some node.Key
                          Operator = Some "="
                          Value = None
                          ValueKind = None
                          SymbolId = tagSymbol lookups ctx node.Key
                          Line = node.Position.StartLine }

                    sortOrder <- sortOrder + 1
                    walkChildren node (Some id) ctx (depth + 1)
                | LeafC leaf ->
                    let ctx = childContext parentCtx parentKey leaf.Key

                    acc.Add
                        { NodeId = nextId ()
                          EntityId = entityId
                          ParentId = parentRowId
                          Depth = depth
                          SortOrder = sortOrder
                          NodeKind = LeafNode
                          Context = ctx
                          Key = Some leaf.Key
                          Operator = Some(operatorToString leaf.Operator)
                          Value = Some leaf.ValueText
                          ValueKind = Some(valueKindOf leaf.Value)
                          SymbolId = tagSymbol lookups ctx leaf.Key
                          Line = leaf.Position.StartLine }

                    sortOrder <- sortOrder + 1
                | LeafValueC lv ->
                    acc.Add
                        { NodeId = nextId ()
                          EntityId = entityId
                          ParentId = parentRowId
                          Depth = depth
                          SortOrder = sortOrder
                          NodeKind = BareValueNode
                          Context = parentCtx
                          Key = None
                          Operator = None
                          Value = Some lv.ValueText
                          ValueKind = Some(valueKindOf lv.Value)
                          SymbolId = None
                          Line = lv.Position.StartLine }

                    sortOrder <- sortOrder + 1
                | ValueClauseC vc ->
                    // anonymous clauses (rare); keep structure with a NULL key
                    let id = nextId ()

                    acc.Add
                        { NodeId = id
                          EntityId = entityId
                          ParentId = parentRowId
                          Depth = depth
                          SortOrder = sortOrder
                          NodeKind = ClauseNode
                          Context = parentCtx
                          Key = None
                          Operator = None
                          Value = None
                          ValueKind = None
                          SymbolId = None
                          Line = vc.Position.StartLine }

                    sortOrder <- sortOrder + 1
                    walkValueClause vc (Some id) parentCtx (depth + 1)
                | CommentC _ -> ()

        and walkValueClause (vc: ValueClause) (parentRowId: int64 option) (ctx: ScriptContext) (depth: int) =
            let mutable sortOrder = 0

            for child in vc.AllArray do
                match child with
                | LeafValueC lv ->
                    acc.Add
                        { NodeId = nextId ()
                          EntityId = entityId
                          ParentId = parentRowId
                          Depth = depth
                          SortOrder = sortOrder
                          NodeKind = BareValueNode
                          Context = ctx
                          Key = None
                          Operator = None
                          Value = Some lv.ValueText
                          ValueKind = Some(valueKindOf lv.Value)
                          SymbolId = None
                          Line = lv.Position.StartLine }

                    sortOrder <- sortOrder + 1
                | _ -> ()

        walkChildren entityNode None MetadataCtx 0
        List.ofSeq acc

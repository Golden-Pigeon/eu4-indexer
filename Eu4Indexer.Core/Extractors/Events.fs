namespace Eu4Indexer.Core.Extractors

open Eu4Indexer.Core
open CWTools.Process
open Support

/// Extracts country_event / province_event entities from events/ files.
module Events =

    let private eventKinds =
        Map.ofList [ "country_event", CountryEvent; "province_event", ProvinceEvent ]

    let extract
        (lookups: ScriptTree.TagLookups)
        (idGen: IdGen)
        (file: GameFile)
        (parsed: Parsing.ParsedFile)
        : EntityPayload list =

        // 'namespace = x' statements apply to subsequent events in file order;
        // resolve per event by tracking the last namespace seen before it.
        let namespaceAt =
            let decls =
                parsed.Root.Leaves
                |> Seq.filter (fun l -> l.Key.ToLowerInvariant() = "namespace")
                |> Seq.map (fun l -> l.Position.StartLine, l.ValueText)
                |> List.ofSeq

            fun (line: int) ->
                decls
                |> List.filter (fun (declLine, _) -> declLine <= line)
                |> List.tryLast
                |> Option.map snd
                |> Option.defaultValue ""

        topLevelNodes parsed.Root
        |> List.choose (fun (stmtIndex, node) ->
            Map.tryFind (node.Key.ToLowerInvariant()) eventKinds
            |> Option.map (fun kind -> stmtIndex, node, kind))
        |> List.map (fun (stmtIndex, node, kind) ->
            let eventId =
                leafText node "id"
                |> Option.defaultValue (sprintf "<anonymous:%s:%d>" file.RelativePath stmtIndex)

            let hidden = leafBool node "hidden"
            let triggeredOnly = leafBool node "is_triggered_only"
            let hasMtth = node.Has "mean_time_to_happen"

            let subtypes =
                [ kind.DbValue
                  if hidden then "hidden"
                  if triggeredOnly then "triggered" ]

            let entity =
                makeEntity idGen file parsed.Lines "event" eventId subtypes stmtIndex node

            let nodes = ScriptTree.flatten lookups idGen.NextNodeId entity.EntityId node

            // option clauses in flattened rows, in declaration order
            let optionRowIds =
                nodes
                |> List.filter (fun r -> r.Depth = 0 && r.Key = Some "option")
                |> List.map (fun r -> r.NodeId)

            let options =
                node.Children
                |> List.filter (fun c -> c.Key.ToLowerInvariant() = "option")
                |> List.mapi (fun i optNode ->
                    { EntityId = entity.EntityId
                      OptionIdx = i
                      NameKey = leafText optNode "name"
                      NodeId = List.item i optionRowIds })

            let titleKey = leafText node "title"
            let descKey = leafText node "desc"

            let details =
                { EntityId = entity.EntityId
                  Namespace = namespaceAt node.Position.StartLine
                  EventKind = kind
                  TitleKey = titleKey
                  DescKey = descKey
                  Picture = leafText node "picture"
                  IsTriggeredOnly = triggeredOnly
                  Hidden = hidden
                  FireOnlyOnce = leafBool node "fire_only_once"
                  Major = leafBool node "major"
                  HasMtth = hasMtth
                  OptionCount = options.Length }

            let locs =
                [ match titleKey with
                  | Some k -> { EntityId = entity.EntityId; Role = "title"; LocKey = k }
                  | None -> ()
                  match descKey with
                  | Some k -> { EntityId = entity.EntityId; Role = "desc"; LocKey = k }
                  | None -> ()
                  for opt in options do
                      match opt.NameKey with
                      | Some k ->
                          { EntityId = entity.EntityId
                            Role = sprintf "option_%d_name" opt.OptionIdx
                            LocKey = k }
                      | None -> () ]

            { EntityPayload.create entity nodes with
                EventDetails = Some details
                EventOptions = options
                EntityLocs = locs })

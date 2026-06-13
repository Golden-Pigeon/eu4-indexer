namespace Eu4Indexer.Core

/// Loads a cwtools game config repo (e.g. cwtools-eu4-config) and distils it
/// into the pieces the indexer needs: type definitions for the generic
/// extractor, trigger/effect/modifier symbol dictionaries for script-node
/// tagging, and the indexable folder list.
module ConfigCatalog =

    open System.IO
    open CWTools.Common
    open CWTools.Parser
    open CWTools.Process
    open CWTools.Rules
    open CWTools.Utilities
    open CWTools.Utilities.Utils
    open FParsec

    type Catalog =
        { TypeDefs: ConfigTypeInfo list
          Symbols: Symbol list
          /// lowercase trigger name -> symbol id
          TriggerLookup: Map<string, int>
          /// lowercase effect name -> symbol id
          EffectLookup: Map<string, int>
          /// lowercase modifier key -> symbol id
          ModifierLookup: Map<string, int>
          /// Game-relative folders listed in folders.cwt
          Folders: string list }

    let private leftFieldName (field: NewField) =
        match field with
        | SpecificField(SpecificValue v) -> Some(StringResource.stringManager.GetStringForIDs v)
        | _ -> None

    let private ruleKeyName (rule: RuleType) =
        match rule with
        | NodeRule(left, _) -> leftFieldName left
        | LeafRule(left, _) -> leftFieldName left
        | LeafValueRule _
        | ValueClauseRule _
        | SubtypeRule _ -> None

    let private toSkipRootSpec (s: SkipRootKey) =
        match s with
        | SpecificKey k -> SkipSpecific k
        | AnyKey -> SkipAny
        | MultipleKeys(ks, shouldMatch) -> SkipMultiple(ks, shouldMatch)

    let private toLocMapping (l: TypeLocalisation) =
        { Role = l.name
          Prefix = l.prefix
          Suffix = l.suffix
          IsPrimary = l.primary }

    let private toConfigTypeInfo (t: TypeDefinition) =
        { TypeName = t.name
          NameField = t.nameField
          Paths = t.pathOptions.paths |> List.ofArray
          PathStrict = t.pathOptions.pathStrict
          PathFile = t.pathOptions.pathFile
          PathExtension = t.pathOptions.pathExtension
          TypePerFile = t.type_per_file
          SkipRootKeys = t.skipRootKey |> List.map toSkipRootSpec
          LocMappings = t.localisation |> List.map toLocMapping
          TypeKeyFilter = t.typeKeyFilter
          StartsWith = t.startsWith }

    /// Parse the `modifiers = { name = scope }` block of modifiers.cwt without
    /// requiring the modifier-category manager.
    let private parseModifierCatalog (fileName: string) (text: string) =
        match CKParser.parseString text fileName with
        | Failure _ -> []
        | Success(statements, _, _) ->
            let root = ProcessCore.processNodeBasic "root" (mkZeroFile fileName) statements

            root.Child "modifiers"
            |> Option.map (fun ms ->
                ms.Leaves
                |> Seq.map (fun l -> l.Key, l.ValueText)
                |> List.ofSeq)
            |> Option.defaultValue []

    let private readFoldersList (text: string) =
        text.Split('\n')
        |> Array.map (fun l -> l.Trim().TrimEnd('/'))
        |> Array.filter (fun l -> l <> "" && not (l.StartsWith "#"))
        |> Array.map (fun l -> l.Replace('\\', '/'))
        |> List.ofArray

    /// Loads and distils the config repo. Returns Error for unusable repos
    /// (missing directory or scopes.cwt); individual file parse failures are
    /// tolerated (CWTools logs and yields empty results for them).
    let load (configDir: string) : Result<Catalog, string> =
        if not (Directory.Exists configDir) then
            Result.Error(sprintf "config directory not found: %s" configDir)
        else

        let cwtFiles =
            Directory.EnumerateFiles(configDir, "*.cwt", SearchOption.AllDirectories)
            |> Seq.map (fun p -> p, File.ReadAllText p)
            |> List.ofSeq

        let findByName name =
            cwtFiles
            |> List.tryFind (fun (p, _) -> Path.GetFileName(p: string) = name)

        match findByName "scopes.cwt" with
        | None -> Result.Error(sprintf "scopes.cwt not found under %s" configDir)
        | Some(scopesPath, scopesText) ->

        UtilityParser.initializeScopes (Some(scopesPath, scopesText)) None
        UtilityParser.initializeModifierCategories None None

        let parseScope = scopeManager.ParseScope()
        let allScopes = scopeManager.AllScopes
        let anyScope = scopeManager.AnyScope
        let scopeGroups = scopeManager.ScopeGroups

        // Per-file parse keeps cwt-file attribution for the symbols table.
        let perFile =
            cwtFiles
            |> List.map (fun (path, text) ->
                let relName =
                    Path.GetRelativePath(configDir, path).Replace(Path.DirectorySeparatorChar, '/')

                let rules, types, _enums, _complexEnums, _values =
                    RulesParser.parseConfig parseScope allScopes anyScope scopeGroups path text

                relName, rules, types)

        let aliasSymbols =
            perFile
            |> List.collect (fun (relName, rules, _) ->
                rules
                |> List.choose (fun rootRule ->
                    match rootRule with
                    | AliasRule(category, (ruleType, _)) ->
                        let kind =
                            match category with
                            | "trigger" -> Some TriggerSymbol
                            | "effect" -> Some EffectSymbol
                            | _ -> None

                        match kind, ruleKeyName ruleType with
                        | Some kind, Some name -> Some(kind, name.Trim(), None, relName)
                        | _ -> None
                    | _ -> None))

        let modifierSymbols =
            match findByName "modifiers.cwt" with
            | Some(path, text) ->
                let relName =
                    Path.GetRelativePath(configDir, path).Replace(Path.DirectorySeparatorChar, '/')

                parseModifierCatalog path text
                |> List.map (fun (name, scope) -> ModifierSymbol, name, Some scope, relName)
            | None -> []

        let symbols =
            aliasSymbols @ modifierSymbols
            |> List.distinctBy (fun (kind, name, _, _) -> kind, (name: string).ToLowerInvariant())
            |> List.mapi (fun i (kind, name, scope, cwtFile) ->
                { SymbolId = i + 1
                  Name = name
                  Kind = kind
                  Scope = scope
                  CwtFile = cwtFile })

        let lookupFor kind =
            symbols
            |> List.filter (fun s -> s.Kind = kind)
            |> List.map (fun s -> s.Name.ToLowerInvariant(), s.SymbolId)
            |> Map.ofList

        let typeDefs =
            perFile
            |> List.collect (fun (_, _, types) -> types |> List.map toConfigTypeInfo)
            |> List.distinctBy (fun t -> t.TypeName)

        let folders =
            match findByName "folders.cwt" with
            | Some(_, text) -> readFoldersList text
            | None -> []

        Result.Ok
            { TypeDefs = typeDefs
              Symbols = symbols
              TriggerLookup = lookupFor TriggerSymbol
              EffectLookup = lookupFor EffectSymbol
              ModifierLookup = lookupFor ModifierSymbol
              Folders = folders }

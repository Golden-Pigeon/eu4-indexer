namespace Eu4Indexer.Core

/// Core domain types shared across the indexing pipeline.
/// Integer ids are assigned sequentially by the pipeline before DB write;
/// they become the SQLite primary keys verbatim.

type SourceKind =
    | BaseGame
    | Mod

    member this.DbValue =
        match this with
        | BaseGame -> "base_game"
        | Mod -> "mod"

/// Parsed contents of a .mod descriptor file.
type ModDescriptorInfo =
    { Name: string
      Version: string option
      SupportedVersion: string option
      RemoteFileId: string option
      Picture: string option
      /// 'path=' entry (launcher-managed descriptors point at the content dir)
      Path: string option
      /// 'archive=' entry (zipped mods; not supported for indexing)
      Archive: string option
      Tags: string list
      Dependencies: string list
      ReplacePaths: string list }

/// One content source: the base game or a mod, with its position in load order.
type Source =
    { SourceId: int
      Kind: SourceKind
      /// 0 = base game; mods follow in load order
      LoadOrder: int
      Name: string
      /// Absolute directory containing the content (common/, events/, ...)
      RootPath: string
      DescriptorPath: string option
      Descriptor: ModDescriptorInfo option }

type ParseStatus =
    | ParsedOk
    | ParseFailed
    | SkippedFile

    member this.DbValue =
        match this with
        | ParsedOk -> "ok"
        | ParseFailed -> "error"
        | SkippedFile -> "skipped"

/// A discovered script/localisation file (winners and shadowed losers alike).
type GameFile =
    { FileId: int
      SourceId: int
      AbsolutePath: string
      /// Normalized: lowercase, '/' separators
      RelativePath: string
      /// Top folder key, e.g. "events", "common/event_modifiers", "localisation"
      Folder: string
      FileName: string
      ContentHash: string
      ByteSize: int64
      IsEffective: bool
      ParseStatus: ParseStatus }

type FileOverrideKind =
    | FileShadowed
    | FileReplacePath

    member this.DbValue =
        match this with
        | FileShadowed -> "shadow"
        | FileReplacePath -> "replace_path"

type FileOverride =
    { Kind: FileOverrideKind
      RelativePath: string
      LoserFileId: int
      /// None for replace_path wipes without a replacement file
      WinnerFileId: int option
      WinnerSourceId: int
      LoserSourceId: int
      IdenticalContent: bool }

type ParseErrorRow =
    { FileId: int
      Message: string
      Line: int option
      Col: int option }

// ---------------------------------------------------------------------------
// Symbols and config types (from cwtools-eu4-config)
// ---------------------------------------------------------------------------

type SymbolKind =
    | TriggerSymbol
    | EffectSymbol
    | ModifierSymbol

    member this.DbValue =
        match this with
        | TriggerSymbol -> "trigger"
        | EffectSymbol -> "effect"
        | ModifierSymbol -> "modifier"

type Symbol =
    { SymbolId: int
      Name: string
      Kind: SymbolKind
      /// Modifier scope category (country/province); None for triggers/effects in v1
      Scope: string option
      CwtFile: string }

/// Localisation mapping declared by a config type definition.
type ConfigLocMapping =
    { Role: string
      Prefix: string
      Suffix: string
      IsPrimary: bool }

/// One level of root keys to skip before entity keys (mirrors CWTools SkipRootKey).
type SkipRootSpec =
    | SkipSpecific of string
    | SkipAny
    | SkipMultiple of keys: string list * shouldMatch: bool

/// Distilled view of a CWTools TypeDefinition, enough to drive the generic extractor.
type ConfigTypeInfo =
    { TypeName: string
      NameField: string option
      /// Game-relative folder paths this type loads from (no "game/" prefix)
      Paths: string list
      PathStrict: bool
      PathFile: string option
      PathExtension: string option
      TypePerFile: bool
      SkipRootKeys: SkipRootSpec list
      LocMappings: ConfigLocMapping list
      /// Root keys that identify (or exclude) this type when several types share a folder
      TypeKeyFilter: (string list * bool) option
      StartsWith: string option }

// ---------------------------------------------------------------------------
// Entities and script tree
// ---------------------------------------------------------------------------

type ScriptContext =
    | TriggerCtx
    | EffectCtx
    | MtthCtx
    | AiChanceCtx
    | MetadataCtx

    member this.DbValue =
        match this with
        | TriggerCtx -> "trigger"
        | EffectCtx -> "effect"
        | MtthCtx -> "mtth"
        | AiChanceCtx -> "ai_chance"
        | MetadataCtx -> "metadata"

type ScriptNodeKind =
    | ClauseNode
    | LeafNode
    | BareValueNode

    member this.DbValue =
        match this with
        | ClauseNode -> "clause"
        | LeafNode -> "leaf"
        | BareValueNode -> "value"

type ScriptValueKind =
    | IntValue
    | FloatValue
    | BoolValue
    | DateValue
    | StringValue

    member this.DbValue =
        match this with
        | IntValue -> "int"
        | FloatValue -> "float"
        | BoolValue -> "bool"
        | DateValue -> "date"
        | StringValue -> "string"

type ScriptNodeRow =
    { NodeId: int64
      EntityId: int64
      ParentId: int64 option
      Depth: int
      SortOrder: int
      NodeKind: ScriptNodeKind
      Context: ScriptContext
      Key: string option
      Operator: string option
      Value: string option
      ValueKind: ScriptValueKind option
      SymbolId: int option
      Line: int }

type EntityRecord =
    { EntityId: int64
      EntityType: string
      EntityKey: string
      FileId: int
      SourceId: int
      StartLine: int
      EndLine: int
      /// Ordinal of the entity's top-level statement within its file (override tie-break)
      StmtIndex: int
      Subtypes: string list
      RawText: string
      IsEffective: bool }

type EntityOverrideKind =
    | Redefinition
    | EntityFileShadow
    | EntityReplacePath

    member this.DbValue =
        match this with
        | Redefinition -> "redefinition"
        | EntityFileShadow -> "file_shadow"
        | EntityReplacePath -> "replace_path"

type EntityOverride =
    { Kind: EntityOverrideKind
      EntityType: string
      EntityKey: string
      LoserEntityId: int64
      WinnerEntityId: int64 option
      WinnerSourceId: int option
      LoserSourceId: int
      IdenticalContent: bool }

// ---------------------------------------------------------------------------
// Core-four detail rows
// ---------------------------------------------------------------------------

type EventKind =
    | CountryEvent
    | ProvinceEvent

    member this.DbValue =
        match this with
        | CountryEvent -> "country"
        | ProvinceEvent -> "province"

type EventDetails =
    { EntityId: int64
      Namespace: string
      EventKind: EventKind
      TitleKey: string option
      DescKey: string option
      Picture: string option
      IsTriggeredOnly: bool
      Hidden: bool
      FireOnlyOnce: bool
      Major: bool
      HasMtth: bool
      OptionCount: int }

type EventOption =
    { EntityId: int64
      OptionIdx: int
      NameKey: string option
      NodeId: int64 }

type MissionDetails =
    { EntityId: int64
      SeriesKey: string
      Slot: int option
      IsGeneric: bool
      Ai: bool
      Icon: string option
      Position: int option
      HasHighlight: bool }

type MissionRequirement =
    { EntityId: int64
      RequiredMission: string }

type DecisionDetails =
    { EntityId: int64
      Major: bool
      AiImportance: float option }

type ModifierValue =
    { EntityId: int64
      ModifierKey: string
      Value: string
      SymbolId: int option }

type EntityLocalisation =
    { EntityId: int64
      Role: string
      LocKey: string }

/// Everything an extractor produces for one entity, ready for the DB writer.
type EntityPayload =
    { Entity: EntityRecord
      Nodes: ScriptNodeRow list
      EventDetails: EventDetails option
      EventOptions: EventOption list
      MissionDetails: MissionDetails option
      MissionRequirements: MissionRequirement list
      DecisionDetails: DecisionDetails option
      ModifierValues: ModifierValue list
      EntityLocs: EntityLocalisation list }

module EntityPayload =
    let create entity nodes =
        { Entity = entity
          Nodes = nodes
          EventDetails = None
          EventOptions = []
          MissionDetails = None
          MissionRequirements = []
          DecisionDetails = None
          ModifierValues = []
          EntityLocs = [] }

// ---------------------------------------------------------------------------
// Localisation
// ---------------------------------------------------------------------------

type LocRow =
    { LocId: int64
      LocKey: string
      Language: string
      Value: string
      /// Value with inline formatting markup (color codes, icons) stripped, for search.
      ValuePlain: string
      VersionNum: int option
      FileId: int
      SourceId: int
      IsReplace: bool
      IsEffective: bool }

type LocOverrideKind =
    | LaterSource
    | ReplaceDir
    | SameSourceDuplicate
    | LocFileShadow
    | LocReplacePath

    member this.DbValue =
        match this with
        | LaterSource -> "later_source"
        | ReplaceDir -> "replace_dir"
        | SameSourceDuplicate -> "same_source_duplicate"
        | LocFileShadow -> "file_shadow"
        | LocReplacePath -> "replace_path"

type LocOverride =
    { LocKey: string
      Language: string
      Kind: LocOverrideKind
      LoserLocId: int64
      WinnerLocId: int64 option
      WinnerSourceId: int option
      LoserSourceId: int
      IdenticalContent: bool }

// ---------------------------------------------------------------------------
// Reference / causal graph (derived from the script tree)
// ---------------------------------------------------------------------------

/// One edge in the cross-reference graph: a script node referencing another
/// piece of content. See Schema's `refs` table for the controlled vocabularies
/// of RefKind and TargetType.
type ReferenceRow =
    { FromEntityId: int64
      /// trigger | effect | mtth | option_trigger | option_effect
      FromContext: string
      RefKind: string
      TargetType: string
      TargetKey: string
      NodeId: int64
      /// The enclosing event option's clause node, when inside one.
      OptionNodeId: int64 option
      /// True when the reference sits inside a NOT wrapper (odd nesting).
      Negated: bool }

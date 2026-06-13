namespace Eu4Indexer.Mcp;

/// <summary>A node in an entity's condition/effect tree.</summary>
public sealed record ScriptNodeDto(
    string? Key,
    string? Operator,
    string? Value,
    string Context,
    string? SymbolKind,
    string? SymbolName,
    List<ScriptNodeDto> Children);

/// <summary>Identity and provenance of one entity.</summary>
public sealed record EntityRef(
    string EntityType,
    string EntityKey,
    string SourceName,
    bool IsEffective,
    string RelativePath);

/// <summary>An entity's localised text for one role (title/desc/name).</summary>
public sealed record LocText(string Role, string LocKey, string? Text);

/// <summary>One event option: its visibility condition is in the script tree.</summary>
public sealed record OptionInfo(int Index, string? NameKey, string? NameText, long NodeId);

/// <summary>An outbound reference this entity makes.</summary>
public sealed record RefEdge(
    string RefKind,
    string TargetType,
    string TargetKey,
    bool Negated,
    string FromContext);

/// <summary>An inbound reference: another entity that fires/references this one.</summary>
public sealed record InboundEdge(
    string RefKind,
    string FromType,
    string FromKey,
    string FromContext);

/// <summary>Everything explain_entity returns for one entity.</summary>
public sealed record EntityExplanation(
    EntityRef Entity,
    List<LocText> Localisation,
    List<ScriptNodeDto> Script,
    bool ScriptTruncated,
    List<OptionInfo> Options,
    List<RefEdge> References,
    List<InboundEdge> TriggeredBy);

/// <summary>A cross-type search hit (entity script or localisation text).</summary>
public sealed record SearchHit(string Kind, string Type, string Key, string Snippet);

/// <summary>A localisation search hit.</summary>
public sealed record LocHit(string LocKey, string Language, string Value);

/// <summary>One dictionary match for a symbol name.</summary>
public sealed record SymbolMatch(string Kind, string? Scope, string CwtFile);

/// <summary>resolve_symbol result: dictionary matches plus any scripted definition.</summary>
public sealed record SymbolResolution(
    string Name,
    List<SymbolMatch> Matches,
    string? ScriptedType,
    List<ScriptNodeDto>? Definition);

/// <summary>what_triggers result: the entity, how it fires, and its inbound edges.</summary>
public sealed record InboundResult(
    EntityRef Entity,
    string? FiringModel,
    List<InboundEdge> TriggeredBy);

/// <summary>what_does_it_do result: the entity and its outbound edges.</summary>
public sealed record OutboundResult(EntityRef Entity, List<RefEdge> References);

/// <summary>An entity whose conditions check a given flag/variable/trigger.</summary>
public sealed record ConditionUser(
    string EntityType,
    string EntityKey,
    string RefKind,
    string FromContext,
    bool Negated);

/// <summary>One step in a reachability chain: an entity that produces a state.</summary>
public sealed record TraceStep(
    string Produces,
    string Via,
    string EntityType,
    string EntityKey,
    string Role);

/// <summary>A candidate chain of actions, ordered from a base action to the goal.</summary>
public sealed record TracePath(List<TraceStep> Steps);

/// <summary>trace_to_goal result: candidate chains plus the modelling caveat.</summary>
public sealed record TraceResult(
    string Target,
    bool Reachable,
    List<TracePath> Paths,
    bool Truncated,
    string Note);

/// <summary>A reference whose target is never set/defined (likely unreachable / a bug).</summary>
public sealed record DanglingRef(
    string Kind,
    string TargetType,
    string TargetKey,
    long ReferencedBy,
    string? SampleReferencer);

/// <summary>A content source (base game or mod) in load order.</summary>
public sealed record SourceInfo(
    int LoadOrder,
    string Kind,
    string Name,
    string? ModVersion,
    string? RemoteFileId,
    List<string> ReplacePaths);

/// <summary>One override across any level, with source names resolved.</summary>
public sealed record OverrideRow(
    string Level,
    string Kind,
    string What,
    string? WinnerSource,
    string LoserSource,
    bool IdenticalContent);

/// <summary>A name with an associated count, for the data dictionary.</summary>
public sealed record NameCount(string Name, long Count);

/// <summary>describe_schema result: the DDL plus a data dictionary.</summary>
public sealed record SchemaInfo(
    string Ddl,
    List<NameCount> EntityTypes,
    List<NameCount> ReferenceKinds,
    List<NameCount> SymbolKinds,
    List<NameCount> OverrideLevels,
    List<string> Languages);

/// <summary>read_query result: column names and rows (as column-keyed maps).</summary>
public sealed record QueryResult(
    List<string> Columns,
    List<Dictionary<string, object?>> Rows,
    int RowCount,
    bool Truncated);

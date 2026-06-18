namespace Eu4Indexer.Core

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization

/// Reads and writes ~/.eu4indexer/config.json, the registry of built indexes.
/// Each entry records which game it belongs to so a single installation can
/// hold several games' databases, and an MCP session can pick the right one.
module Registry =

    /// One built index. Arrays (not F# lists) are used so System.Text.Json can
    /// (de)serialise the model without an extra converter package. CLIMutable
    /// gives the records a parameterless constructor for deserialisation.
    [<CLIMutable>]
    type DbEntry =
        { Name: string
          Game: string
          Path: string
          SchemaVersion: int
          Sources: int
          IndexedAt: string }

    [<CLIMutable>]
    type Config =
        { /// Name of the active database, or "" when none is set.
          ActiveDb: string
          Databases: DbEntry[] }

    let empty = { ActiveDb = ""; Databases = [||] }

    let private jsonOptions =
        let o = JsonSerializerOptions(WriteIndented = true)
        o.DefaultIgnoreCondition <- JsonIgnoreCondition.Never
        o

    /// Load the registry, returning an empty config when the file is absent or
    /// unreadable (a corrupt file should not block indexing).
    let load () : Config =
        let path = AppPaths.configFile ()

        if not (File.Exists path) then
            empty
        else
            try
                match JsonSerializer.Deserialize<Config>(File.ReadAllText path, jsonOptions) with
                | c when obj.ReferenceEquals(c, null) -> empty
                | c -> { c with Databases = (if obj.ReferenceEquals(c.Databases, null) then [||] else c.Databases) }
            with _ ->
                empty

    let save (config: Config) =
        AppPaths.home () |> AppPaths.ensureDir |> ignore
        File.WriteAllText(AppPaths.configFile (), JsonSerializer.Serialize(config, jsonOptions))

    let findByName (name: string) (config: Config) =
        config.Databases |> Array.tryFind (fun e -> e.Name = name)

    /// Insert or replace an entry by name, optionally marking it active, and
    /// persist. Paths are normalised to forward slashes on the way in.
    let upsert (entry: DbEntry) (setActive: bool) =
        let config = load ()
        let entry = { entry with Path = AppPaths.normalize entry.Path }
        let others = config.Databases |> Array.filter (fun e -> e.Name <> entry.Name)
        let databases = Array.append others [| entry |] |> Array.sortBy (fun e -> e.Name)
        let activeDb = if setActive then entry.Name else config.ActiveDb
        save { ActiveDb = activeDb; Databases = databases }

    /// Set the active database by name. Returns false if the name is unknown.
    let setActive (name: string) =
        let config = load ()

        match findByName name config with
        | None -> false
        | Some _ ->
            save { config with ActiveDb = name }
            true

    /// The filesystem path of the active database, if any is set and known.
    let activePath () : string option =
        let config = load ()

        if String.IsNullOrEmpty config.ActiveDb then
            // Fall back to the sole database when exactly one is registered.
            match config.Databases with
            | [| only |] -> Some only.Path
            | _ -> None
        else
            findByName config.ActiveDb config |> Option.map (fun e -> e.Path)

namespace Eu4Indexer.Core

open System
open System.IO

/// On-disk layout of an eu4indexer installation. Defaults to ~/.eu4indexer
/// (override with EU4INDEXER_HOME). The config and db directories are
/// namespaced by game id so additional games (CK3 / HOI4 / Stellaris / VIC3)
/// can coexist without colliding.
module AppPaths =

    [<Literal>]
    let HomeEnvVar = "EU4INDEXER_HOME"

    /// The installation root.
    let home () =
        match Environment.GetEnvironmentVariable HomeEnvVar with
        | null | "" ->
            Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".eu4indexer")
        | dir -> dir

    let binDir () = Path.Combine(home (), "bin")

    /// The registry file recording known databases and the active one.
    let configFile () = Path.Combine(home (), "config.json")

    /// cwtools config rules for a game, e.g. ~/.eu4indexer/config/eu4.
    let configDir (gameId: string) = Path.Combine(home (), "config", gameId)

    /// Default output directory for a game's indexes, e.g. ~/.eu4indexer/db/eu4.
    let dbDir (gameId: string) = Path.Combine(home (), "db", gameId)

    /// Create a directory (and parents) if missing, returning its path.
    let ensureDir (path: string) =
        Directory.CreateDirectory path |> ignore
        path

    /// Normalise a path to forward slashes for stable, escape-free storage in
    /// JSON. .NET file APIs accept forward slashes on Windows too.
    let normalize (path: string) = path.Replace('\\', '/')

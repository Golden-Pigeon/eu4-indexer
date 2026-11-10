namespace Eu4Indexer.Core

open System
open System.IO

/// On-disk layout of an eu4indexer installation. Defaults to ~/.eu4indexer
/// (override with EU4INDEXER_HOME). User data is separate from the immutable
/// game rules distributed beside each engine build.
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

    /// Rules are copied into build and publish output by the Core project.
    let bundledConfigDir (gameId: string) = Path.Combine(AppContext.BaseDirectory, "rules", gameId)

    /// Explicit game-specific overrides take precedence over the bundled rules.
    let configDir (gameId: string) =
        match Environment.GetEnvironmentVariable(gameId.ToUpperInvariant() + "_CONFIG_DIR") with
        | null | "" -> bundledConfigDir gameId
        | dir -> Path.GetFullPath dir

    let samePath (left: string) (right: string) =
        let comparison = if OperatingSystem.IsWindows() then StringComparison.OrdinalIgnoreCase else StringComparison.Ordinal
        String.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath left), Path.TrimEndingDirectorySeparator(Path.GetFullPath right), comparison)

    let isBundledConfig (gameId: string) (dir: string) = samePath dir (bundledConfigDir gameId)

    /// Recognize old downloaded defaults without altering user files.
    let isLegacyManagedConfig (gameId: string) (dir: string) =
        dir <> "" && samePath dir (Path.Combine(home (), "config", gameId)) && File.Exists(Path.Combine(dir, ".eu4indexer-ref"))

    /// Default output directory for a game's indexes, e.g. ~/.eu4indexer/db/eu4.
    let dbDir (gameId: string) = Path.Combine(home (), "db", gameId)

    /// Create a directory (and parents) if missing, returning its path.
    let ensureDir (path: string) =
        Directory.CreateDirectory path |> ignore
        path

    /// Normalise a path to forward slashes for stable, escape-free storage in
    /// JSON. .NET file APIs accept forward slashes on Windows too.
    let normalize (path: string) = path.Replace('\\', '/')

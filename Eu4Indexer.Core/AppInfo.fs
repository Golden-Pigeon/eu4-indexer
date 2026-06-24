namespace Eu4Indexer.Core

/// Single source of truth for the released version. Surfaced by the CLI and MCP
/// `version` commands and written into every index's `meta` table. Keep this in
/// step with the git release tag.
module AppInfo =

    [<Literal>]
    let Version = "0.3.1"

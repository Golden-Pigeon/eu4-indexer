# Management API and query sessions

`eu4indexer manage` accepts one JSON object from stdin (or `--request <json>`).
It writes newline-delimited JSON to stdout. Every event has `apiVersion: 1`,
`type`, and `data`. Types are `log`, `progress`, and exactly one terminal
`result` or `error`. Success exits 0; failure exits 1. Human CLI commands retain
their existing flags and output. Set `EU4INDEXER_HOME` to isolate the registry,
rules and managed databases from another application.

```sh
printf '%s' '{"operation":"capabilities"}' | eu4indexer manage
printf '%s' '{"operation":"index","game":"eu4","name":"vanilla","gameDir":"/games/EU4"}' | eu4indexer manage
```

| Operation | Request fields and result |
| --- | --- |
| `capabilities` | API, engine/schema versions, games, operations, session metadata key |
| `status` | Application home and each game's managed rules status |
| `detect` | `game`, optional `gameDir`, `mods`, `workshopIds`, `playset`, `autoMods`; resolved sources in load order |
| `workshop` | `game`; installed Workshop items |
| `playsets` | `game`; playsets with enabled flags and ordered mods |
| `list` | Registry (`activeDb`, `databases`) |
| `inspect` | `name` or `path`; schema/integrity check, metadata, parse errors with file path and nullable line/column. `errorOffset` (default 0), `errorLimit` (default 100, maximum 1000), `hasMoreErrors` allow complete traversal |
| `setup` | `game`, optional `ref`; download CWTools rules |
| `index` | `game`, `name`, discovery fields above, optional `configDir`, `languages`, `skipGeneric`, `noFts`, `setActive` |
| `refresh` | `name`; rebuild using the persisted original discovery/options metadata |
| `register` | `name`, absolute `path`, optional `setActive`; validate an existing database and register it |
| `remove` | `name`, optional `deleteFile`; unregister. File deletion is restricted to the managed database directory |

Names are 1–80 ASCII letters/digits/dots/underscores/hyphens, starting with a
letter or digit. Builds write a unique staging file, validate integrity and
foreign keys, then register a unique immutable database. Registration defaults
to `setActive: false`. Existing registry entries and database files survive
failed builds; superseded database files are retained for readers already using
them. The human `index` and `refresh` commands retain their original behavior.
Registry saves use atomic replacement; read-modify-write operations serialize
across human CLI and management processes. Applications should serialize builds
of the same index name. A killed process can leave an unregistered `.building`
file; remove it only after confirming no build process is using it.

## Isolated query selection

For clients multiplexing several logical sessions over one MCP stdio connection,
include the same nonempty opaque id (up to 128 characters, no control characters)
in **every** `tools/call` request for that session:

```json
{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"select_database","arguments":{"name":"vanilla"},"_meta":{"eu4indexer/sessionId":"chat-42"}}}
```

`list_databases`, `select_database` and every game query use that request's
session. A new named session starts from the connection's initial database,
regardless of later selections in other sessions. Selections do not change the
registry's active default. Requests without metadata keep the legacy connection
selection. Concurrent requests in different sessions are independent; clients
should await a selection before querying within the same session.

`serve --query-session <id>` supplies a default id for a dedicated client that
cannot attach metadata. Explicit request metadata takes precedence.
`close_query_session(sessionId)` releases a named session; its next use starts
from the initial database again. Each connection allows at most 4096 named
sessions; close unused sessions or close the connection to release all state.
Already executing tools keep their resolved immutable database. Session ids
separate query state; they are not an authentication boundary for untrusted
clients. The server remains read-only for game databases.

Validation lives in `Eu4Indexer.Tests/DatabaseSelectionTests.cs`. The DSH adapter
also exercises the JSONL protocol and real multiplexed MCP calls against both
copyright-safe game fixtures.

`scripts/management-smoke.py <executable>` runs the JSONL build/refresh/import lifecycle against both game fixtures after rules setup; the existing three-platform smoke workflow runs it alongside the original CLI/MCP smoke.

# CLAUDE.md

Auto-loaded guidance for agents working in this repo. Deeper references:
[docs/architecture.md](docs/architecture.md) (structure & pipeline),
[docs/database.md](docs/database.md) (schema), [docs/commands.md](docs/commands.md)
(CLI), [CONTRIBUTING.md](CONTRIBUTING.md) (full dev workflow).

## What this is

`eu4indexer` parses Paradox grand-strategy game scripts (EU4 + HOI4) and any
loaded mods with [CWTools](https://github.com/cwtools/cwtools) into a queryable
SQLite (or PostgreSQL) database, then serves that index to AI agents over a
read-only MCP server. The core is game-agnostic behind a `GameAdapter`.

## Project structure

```text
Eu4Indexer.Core/      # F#  — the engine: discovery, parsing, extraction,
  Extractors/         #       override + ref resolution, DB writer
  Database/           #       Schema.fs (SQLite) + PostgresSchema.fs + Writer.fs
Eu4Indexer.Cli/       # F# (Argu) — the `eu4indexer` CLI (Program.fs)
Eu4Indexer.Mcp/       # C#  — read-only MCP server (Tools/, McpServer.cs)
Eu4Indexer.Tests/     # C# (xunit) — unit + fixture + integration tests
skills/eu4-indexer/   #     per-game, per-language agent skills (<game>/<lang>/SKILL.md)
scripts/              #     build-binaries.{sh,ps1}, mcp-smoke.py
external/cwtools/     #     vendored CWTools fork (git submodule)
install.sh / .ps1     #     end-user installers
docs/                 #     architecture / database / commands references
```

| Project | Lang | Role |
|---|---|---|
| `Eu4Indexer.Core` | F# | parse → extract → resolve overrides → build `refs` graph → write DB |
| `Eu4Indexer.Cli` | F# (Argu) | CLI front-end; also hosts the MCP server via `serve` |
| `Eu4Indexer.Mcp` | C# | read-only MCP tools over a built index |
| `Eu4Indexer.Tests` | C# (xunit) | tests (see below) |

Data flow: discover sources (game + mods, load order) → resolve effective files +
record overrides → parse (CWTools) → extract entities → flatten into the
`script_nodes` tree, tag symbols from the `.cwt` config → derive the `refs` causal
graph → write SQLite/Postgres → build indexes, FTS, and views. Parse failures are
recorded in `parse_errors`; a single bad file never aborts the run.

## Build & run

Requires the **.NET 9 SDK**, and a `--recursive` clone (CWTools submodule).

```bash
git submodule update --init --recursive      # if not cloned with --recursive
dotnet build Eu4Indexer.slnx                  # build everything

# run the CLI from source (replaces `eu4indexer` with the dotnet invocation):
dotnet run --project Eu4Indexer.Cli -- index --help
dotnet run --project Eu4Indexer.Cli -- serve --db /abs/path/eu4.db

# cross-publish self-contained release archives for one or all RIDs:
./scripts/build-binaries.sh osx-arm64         # or: (no args) = all six targets
```

## Testing

`dotnet test` runs everything; tests degrade gracefully by tier so the suite
stays green without external data.

- **Unit tests** — run anywhere, no data needed (e.g. `ConfigCatalogTests`,
  `ScriptTreeTests`, `LocalisationTests`, `EffectAnalysisTests`, the `Hoi4*`
  adapter/extractor tests, `McpToolsTests`).
- **Fixture index tests** (`FixtureIndexTests`, `Hoi4FixtureIndexTests`) — index
  the synthetic fixtures under `Eu4Indexer.Tests/fixtures/<game>/` (no real game
  files), but need the CWTools config rules. They **no-op without a config dir**;
  to enable, run `eu4indexer setup` or set `EU4_CONFIG_DIR`. Run just these with:
  `dotnet test --filter "FullyQualifiedName~FixtureIndexTests"`.
- **Integration tests** (`IntegrationTests`, `PostgresExportTests`) — need real
  game data and/or a Postgres connection. Gated by `TestPaths`; they no-op when
  the resource is unset or missing. Enable by copying `.env.example` → `.env`
  (git-ignored) and filling in: `EU4_GAME_DIR`, `EU4_CONFIG_DIR`,
  `EU4_EXAMPLE_MOD_DIR`, and optionally `EU4_PG_CONN` (Postgres export test;
  the role needs `CREATE EXTENSION pg_trgm`). Process env vars take precedence
  over `.env`.

Adding integration assertions that need game data: gate on `TestPaths` and
`return` early when the resource is missing — follow the existing pattern.
`scripts/mcp-smoke.py <exe> <db-name>` drives the MCP server end to end (used by
CI). CI is `.github/workflows/smoke.yml` (build → install via the script in
offline mode → setup → index → serve), on all three platforms.

## Push, PR & release

- **Branch + PR by default.** Don't commit straight to `main` unless the user
  explicitly asks for a quick change. Use Conventional Commit messages and keep
  PRs focused.
- **Merge with rebase**, and only after the `smoke` CI workflow is green.
- **Cutting a release** (manual — there is no release workflow):
  1. **Bump the version in lockstep** — `Eu4Indexer.Core/AppInfo.fs` **and** both
     `.claude-plugin/*.json` (see [Keep these in sync](#keep-these-in-sync)).
     Commit as `chore: bump version to X.Y.Z`.
  2. Open a PR; wait for `smoke` to pass; rebase-merge; update local `main`.
  3. Build every target: `./scripts/build-binaries.sh` — emits both the versioned
     `eu4indexer-<ver>-<rid>` and the version-less `eu4indexer-<rid>` archives in `dist/`.
  4. Publish the release, uploading **all** assets:
     `gh release create vX.Y.Z --generate-notes dist/eu4indexer-*`. This tags the
     commit and attaches both archive sets. The **version-less** copies are what
     the default `releases/latest/download/…` installer resolves to — omit them and
     the one-line install 404s until the next release.

## Keep these in sync

These are the non-obvious "change X → also change Y" couplings CI or users break on.

- **Release version lives in `Eu4Indexer.Core/AppInfo.fs`** (`let Version`) — the
  single source of truth that `build-binaries.*` and `smoke.yml` read. When you
  bump it, also update the two plugin manifests, which are **not** auto-derived:
  `.claude-plugin/plugin.json` and `.claude-plugin/marketplace.json` (two
  `version` fields). Then tag the release `vX.Y.Z`.

- **`.github/workflows/smoke.yml` is coupled to moving parts** — when you change
  any of these, update the workflow (and `scripts/mcp-smoke.py`) to match:
  - **Fixture layout** under `Eu4Indexer.Tests/fixtures/<game>/` (`example-game`,
    `example-mod`) — the smoke job indexes these by path.
  - **CLI command/flag surface** — the job exercises `version`/`setup`/`index`/
    `list`/`serve`; renaming or changing required flags breaks it.
  - **Release archive names** from `scripts/build-binaries.*`
    (`eu4indexer-<version>-<rid>.{tar.gz,zip}` plus the version-less
    `eu4indexer-<rid>.…` copy) — the job and the installers reference them.

- **Installer ↔ build naming**: download URLs in `install.sh` / `install.ps1`
  must match the asset names emitted by `scripts/build-binaries.{sh,ps1}`. The
  default install uses the **version-less** name via GitHub's
  `releases/latest/download/…` redirect; `--version`/`EU4INDEXER_VERSION` pins via
  the **versioned** name. A new latest release must carry the version-less assets
  or the default install 404s.

- **Database schema is dual-dialect**: `Database/Schema.fs` (SQLite) and
  `Database/PostgresSchema.fs` (Postgres) mirror each other table-for-table and
  column-for-column. Change one → change both. Any schema change must bump
  `Schema.UserVersion`; the MCP server rejects indexes whose `PRAGMA user_version`
  differs, and the Postgres `DROP TABLE` list must include any new table.

## Conventions worth not relearning

- **`Eu4Indexer.Core` compiles in dependency order.** The `<Compile>` list in
  `Eu4Indexer.Core.fsproj` is significant — add a new file *after* everything it
  depends on, or the build fails.
- **CWTools calls stay isolated** to `Parsing.fs`, `ConfigCatalog.fs`, and
  `Localisation.fs`. Don't reach into CWTools elsewhere.
- **The core is game-agnostic (`GameAdapter`).** Adding a game = a new adapter +
  any game-specific extractors (`Extractors/`) + per-game detail tables in *both*
  schema dialects. Don't hardcode game assumptions in shared modules.
- **No non-English text in code.** Comments and string literals are English even
  when the data processed (localisation values, mod names) is not.
- **F# is immutable by default; pure where it matters.** Override and
  escape-decoding logic stays in pure functions so it stays unit-testable.
- **Conventional Commits** (`feat:`, `fix:`, `docs:`, `chore:`, `ci:`, …),
  imperative and lowercase. Changes to CWTools itself go in the fork; bump the
  submodule pointer in a separate commit.

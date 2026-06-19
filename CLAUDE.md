# CLAUDE.md

Project-specific notes for agents working in this repo. For structure see
[docs/architecture.md](docs/architecture.md), for the schema see
[docs/database.md](docs/database.md), for the CLI see [docs/commands.md](docs/commands.md),
and for the dev workflow see [CONTRIBUTING.md](CONTRIBUTING.md). This file only
captures the **non-obvious cross-file invariants** — the "if you change X, also
change Y" couplings that are easy to miss and that CI or users will break on.

## Keep these in sync

- **Release version lives in `Eu4Indexer.Core/AppInfo.fs`** (`let Version`). It is
  the single source of truth — `build-binaries.*` and `smoke.yml` read it. When
  you bump it, also update the two plugin manifests, which are **not** auto-derived:
  `.claude-plugin/plugin.json` and `.claude-plugin/marketplace.json` (two
  `version` fields). Then tag the release `vX.Y.Z`.

- **`.github/workflows/smoke.yml` is coupled to several moving parts** — when you
  change any of these, update the workflow (and `scripts/mcp-smoke.py`) to match:
  - **Fixture layout** under `Eu4Indexer.Tests/fixtures/<game>/` (e.g.
    `example-game`, `example-mod`) — the smoke job indexes these by path.
  - **CLI command/flag surface** — the job exercises `version`/`setup`/`index`/
    `list`/`serve`; renaming or changing required flags breaks it.
  - **Release archive names** produced by `scripts/build-binaries.*`
    (`eu4indexer-<version>-<rid>.{tar.gz,zip}` plus the version-less
    `eu4indexer-<rid>.…` copy) — the job and the installers reference them.

- **Installer ↔ build naming**: the download URLs in `install.sh` / `install.ps1`
  must match the asset names emitted by `scripts/build-binaries.{sh,ps1}`. The
  default install path uses the **version-less** name via GitHub's
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
- **Errors are recorded, not swallowed** — parse failures go into `parse_errors`;
  a single bad file never aborts the run.

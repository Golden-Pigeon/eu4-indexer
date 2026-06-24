# Changelog

All notable changes to eu4-indexer are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres
to [Semantic Versioning](https://semver.org/).

Each entry links its source, preferring (high → low): **pull request**, then
**issue**, then **commit hash**.

## [Unreleased]

### Fixed

- Updated the `SUBCOMMANDS` help block at the top of `docs/commands.md` to include
  the `refresh` and `update` commands, which were missing.

### Changed

- Expanded `docs/database.md` into a full schema reference: added a database
  design-philosophy section, three Mermaid ER diagrams (sources/files, entities,
  localisation), and a per-table field reference documenting every column's type,
  meaning, and constraints.
  ([#7](https://github.com/Golden-Pigeon/eu4-indexer/pull/7))

## [0.3.1] - 2026-06-24

### Added
- `refresh` command: re-indexes a registered database in place after a game or
  mod update. `refresh --name <n>` refreshes one index; with no `--name` it
  refreshes every registered (SQLite) index. The original source selection is
  read back from the index's `meta`, so workshop mods are re-located by id, a
  playset is re-expanded (new mod list / order / enabled state), and
  auto-discovery re-runs — a relocated game or mod install is picked up
  automatically. A failed index is reported and skipped, not fatal; the active
  selection is left untouched. `--verbose` / `--progress` mirror `index`
  ([#5](https://github.com/Golden-Pigeon/eu4-indexer/pull/5)).
- `update` command: self-updates the installed binary to the latest GitHub
  release (atomic `bin/` + `skills/` swap; Unix in place, Windows via a deferred
  helper since a running `.exe` can't be overwritten), then refreshes any config
  whose pinned ref drifted. `--check` reports availability without downloading,
  `--force` reinstalls the latest. Refuses to run from a source/dev build
  ([#4](https://github.com/Golden-Pigeon/eu4-indexer/pull/4)).
- `index --progress` flag: shows a live counter of processed files / entities /
  loc entries while parsing, then the current finalize sub-step (indexes / FTS /
  views / integrity / optimize). Refreshes in place on a terminal and falls back
  to periodic lines when stderr is redirected. Orthogonal to `--verbose`.
- HOI4 real-game integration test (`Hoi4IntegrationTests`), the gated analogue of
  the EU4 `IntegrationTests`; enabled via `HOI4_GAME_DIR` and no-ops without it.

### Changed
- Default install now follows GitHub's latest-release redirect instead of a
  hardcoded version tag, so `curl … | sh` always fetches the newest release;
  pinning is still available via `--version` / `EU4INDEXER_VERSION`
  ([018f299](https://github.com/Golden-Pigeon/eu4-indexer/commit/018f299)).
- Release archives now use a single **version-less** name (`eu4indexer-<rid>`);
  the release tag in the download URL selects the version, dropping the redundant
  versioned copy
  ([552418e](https://github.com/Golden-Pigeon/eu4-indexer/commit/552418e)).

### Fixed
- `smoke` CI fixture paths after the reorganisation to `fixtures/<game>/`
  ([2d098d9](https://github.com/Golden-Pigeon/eu4-indexer/commit/2d098d9)).

### Docs
- Slimmed the README into a scannable landing page and moved the detail into
  `docs/architecture.md`, `docs/database.md`, and `docs/commands.md`
  ([f3dafef](https://github.com/Golden-Pigeon/eu4-indexer/commit/f3dafef)).
- Added a repo `CLAUDE.md` covering structure, build/test, cross-file sync
  invariants, the push/PR/release flow, and a docs-with-code rule
  ([0adb6aa](https://github.com/Golden-Pigeon/eu4-indexer/commit/0adb6aa),
  [0cbd166](https://github.com/Golden-Pigeon/eu4-indexer/commit/0cbd166),
  [f9b5a51](https://github.com/Golden-Pigeon/eu4-indexer/commit/f9b5a51),
  [0f1322d](https://github.com/Golden-Pigeon/eu4-indexer/commit/0f1322d)).
- Added this `CHANGELOG.md`.
- Updated `CONTRIBUTING.md`, the Claude Code plugin manifests, and the installer
  hints to reflect HOI4 support (they still read EU4-only).

### Chore
- Bumped the Claude Code plugin manifests to 0.3.0 to match the release
  ([79714bc](https://github.com/Golden-Pigeon/eu4-indexer/commit/79714bc)).

## [0.3.0] - 2026-06-19

### Added
- **HOI4 game support**: a split per-game schema, focus-tree extractor, and MCP
  routing behind the game-agnostic `GameAdapter`
  ([6cc7932](https://github.com/Golden-Pigeon/eu4-indexer/commit/6cc7932)).
- LUA `defines` extractor for game constants (focus duration, AE, etc.)
  ([1723532](https://github.com/Golden-Pigeon/eu4-indexer/commit/1723532)).
- Per-game skill files with `zh` / `en` language variants
  ([3cde0f9](https://github.com/Golden-Pigeon/eu4-indexer/commit/3cde0f9)).
- HOI4 tests: adapter, extractor, localisation, and fixture index
  ([8f2e14b](https://github.com/Golden-Pigeon/eu4-indexer/commit/8f2e14b)).

### Fixed
- Resolve localisation at the key level and detect the HOI4 language from the
  ancestor directory
  ([201c57d](https://github.com/Golden-Pigeon/eu4-indexer/commit/201c57d)).

## [0.2.0] - 2026-06-18

### Added / Changed
- Reshaped into a **single merged binary** (the CLI also hosts the MCP server via
  `serve`), with script installers, multi-agent registration (Claude Code /
  Codex), and a multi-database MCP backed by an index registry
  ([#2](https://github.com/Golden-Pigeon/eu4-indexer/pull/2)).

### Fixed
- Copy config instead of `Directory.Move` so setup is cross-volume safe on Windows
  ([f365428](https://github.com/Golden-Pigeon/eu4-indexer/commit/f365428)).

## [0.1.1] - 2026-06-17

### Changed
- Name release archives by version, defaulting from `AppInfo.fs`
  ([44ddd27](https://github.com/Golden-Pigeon/eu4-indexer/commit/44ddd27)).

### Docs
- Skill: mandatory answering conventions for id format and localisation
  ([83c4660](https://github.com/Golden-Pigeon/eu4-indexer/commit/83c4660)).

## [0.1.0] - 2026-06-15

Initial release — an EU4 script indexer with a query layer for agents.

### Added
- EU4 script indexer: CWTools parsing into a SQLite database
  ([76afee6](https://github.com/Golden-Pigeon/eu4-indexer/commit/76afee6)).
- Decode EU4 special-escape localisation back to UTF-8
  ([17ad2f7](https://github.com/Golden-Pigeon/eu4-indexer/commit/17ad2f7)).
- Derived reference (causal) graph and searchable markup-stripped localisation
  ([ec24545](https://github.com/Golden-Pigeon/eu4-indexer/commit/ec24545)).
- Read-only MCP server with entity and search tools
  ([27f5149](https://github.com/Golden-Pigeon/eu4-indexer/commit/27f5149)),
  graph traversal (`what_triggers`, `what_does_it_do`, `find_by_condition`)
  ([9e5875b](https://github.com/Golden-Pigeon/eu4-indexer/commit/9e5875b)),
  planning tools (`trace_to_goal`, `find_dangling`)
  ([5201d7a](https://github.com/Golden-Pigeon/eu4-indexer/commit/5201d7a)),
  sources/overrides/schema/read_query tools
  ([9b014c2](https://github.com/Golden-Pigeon/eu4-indexer/commit/9b014c2)) with
  `read_query` guarded by an EXPLAIN write-opcode check
  ([f822617](https://github.com/Golden-Pigeon/eu4-indexer/commit/f822617)), and
  proactive effect analysis
  ([#1](https://github.com/Golden-Pigeon/eu4-indexer/pull/1)).
- PostgreSQL export backend behind an `IIndexWriter` + `Dialect` abstraction
  ([d6b9f01](https://github.com/Golden-Pigeon/eu4-indexer/commit/d6b9f01),
  [7202001](https://github.com/Golden-Pigeon/eu4-indexer/commit/7202001)).
- Steam library auto-detection (registry + `libraryfolders.vdf`)
  ([07fae4a](https://github.com/Golden-Pigeon/eu4-indexer/commit/07fae4a)) and
  Steam Workshop / launcher playset discovery
  ([12af2a9](https://github.com/Golden-Pigeon/eu4-indexer/commit/12af2a9)).
- `version` command on the CLI and MCP server
  ([41e2e8c](https://github.com/Golden-Pigeon/eu4-indexer/commit/41e2e8c)).
- Packaged as a Claude Code plugin with a bundled skill + MCP config
  ([400550f](https://github.com/Golden-Pigeon/eu4-indexer/commit/400550f)) and an
  agent `SKILL.md` teaching the EU4 causal model and workflows
  ([c8df726](https://github.com/Golden-Pigeon/eu4-indexer/commit/c8df726)).
- Cross-platform release scripts
  ([8392ce4](https://github.com/Golden-Pigeon/eu4-indexer/commit/8392ce4)).

### Changed
- Stream script nodes to the DB to cut indexing peak memory
  ([18e997b](https://github.com/Golden-Pigeon/eu4-indexer/commit/18e997b)).

### Fixed
- Collapse duplicate files by normalised relative path
  ([a07556a](https://github.com/Golden-Pigeon/eu4-indexer/commit/a07556a)).
- Clear error when FTS is missing; allow semicolons in string literals
  ([766a9d0](https://github.com/Golden-Pigeon/eu4-indexer/commit/766a9d0)).

### Build / Docs
- Reference CWTools via a git submodule
  ([500eb51](https://github.com/Golden-Pigeon/eu4-indexer/commit/500eb51)).
- MIT license, usage disclaimer, contribution guide, and acknowledgements
  ([7f94130](https://github.com/Golden-Pigeon/eu4-indexer/commit/7f94130),
  [c23cdfd](https://github.com/Golden-Pigeon/eu4-indexer/commit/c23cdfd),
  [6f69b1d](https://github.com/Golden-Pigeon/eu4-indexer/commit/6f69b1d)).

[Unreleased]: https://github.com/Golden-Pigeon/eu4-indexer/compare/v0.3.1...HEAD
[0.3.1]: https://github.com/Golden-Pigeon/eu4-indexer/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/Golden-Pigeon/eu4-indexer/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/Golden-Pigeon/eu4-indexer/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/Golden-Pigeon/eu4-indexer/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Golden-Pigeon/eu4-indexer/releases/tag/v0.1.0

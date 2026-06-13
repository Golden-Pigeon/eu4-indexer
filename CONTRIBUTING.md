# Contributing to eu4-indexer

Thanks for your interest in improving eu4-indexer. This guide covers how to get
set up, the conventions the codebase follows, and how to land a change.

## Getting set up

1. **Clone with submodules.** CWTools is vendored as a git submodule at
   `external/cwtools` (a fork pinned to a build that compiles against the
   current .NET SDK).

   ```bash
   git clone --recursive https://github.com/Golden-Pigeon/eu4-indexer.git
   # already cloned without --recursive?
   git submodule update --init --recursive
   ```

2. **Install the .NET 9 SDK.**

3. **Build and run the unit tests** (these need no game data):

   ```bash
   dotnet build Eu4Indexer.slnx
   dotnet test
   ```

4. **Enable the integration tests** (optional). They index the real game plus an
   example mod and no-op when the data is absent. Copy `.env.example` to `.env`
   and fill in the paths:

   ```bash
   cp .env.example .env   # then edit
   ```

   - `EU4_GAME_DIR` — an EU4 install (must contain `common/`, `events/`,
     `localisation/`).
   - `EU4_CONFIG_DIR` — a [`cwtools-eu4-config`](https://github.com/cwtools/cwtools-eu4-config) checkout.
   - `EU4_EXAMPLE_MOD_DIR` — a mod directory (drives the override and
     special-escape localisation tests).

   `.env` is git-ignored; never commit game files, mods, or generated `*.db`
   indexes.

## Project layout

| Project | Language | Purpose |
|---|---|---|
| `Eu4Indexer.Core` | F# | Parsing, extraction, override resolution, SQLite writer |
| `Eu4Indexer.Cli` | F# (Argu) | `index` and `detect` commands |
| `Eu4Indexer.Tests` | C# (xunit) | Unit + integration tests |

`Eu4Indexer.Core` modules compile in dependency order (see the `.fsproj`); when
you add a file, place it in the `<Compile>` list after everything it depends on.
The design is game-agnostic at its core (`GameAdapter`); EU4 is the only
implementation today. Keep all CWTools calls isolated to `Parsing.fs`,
`ConfigCatalog.fs`, and `Localisation.fs`.

## Conventions

- **No non-English text in code.** Comments and string literals must be English,
  even when the data being processed (e.g. localisation values) is not.
- **Pure functions where it matters.** Override and escape-decoding logic lives
  in pure functions (`OverrideResolution.fs`, `Localisation.fs`) so it can be
  unit-tested directly — keep it that way.
- **Small, focused files.** Prefer many small modules over large ones.
- **Errors are recorded, not swallowed.** Parse failures go into the
  `parse_errors` table; the run never aborts on a single bad file.
- **F#**: idiomatic, immutable by default. **C# tests**: nullable reference
  types on, Arrange-Act-Assert structure, descriptive test names.

## Tests

- Add unit tests for any new pure logic (extractors, resolution, decoding).
- Integration assertions that need game data must guard on `TestPaths` and
  `return` early when the resource is missing, so the suite stays green for
  contributors without the data. Follow the existing pattern in
  `IntegrationTests.cs`.
- Run `dotnet test` before opening a PR.

## Commits and pull requests

- Use [Conventional Commits](https://www.conventionalcommits.org/): `feat:`,
  `fix:`, `refactor:`, `docs:`, `test:`, `chore:`, `perf:`, `ci:`. Imperative
  mood, lowercase, no trailing period, under ~72 chars.
- Keep each PR focused; describe what changed and why, and note how you tested.
- If your change touches the database schema, bump `Schema.UserVersion` and
  describe the migration impact.
- Changes to CWTools itself belong in the
  [fork](https://github.com/Golden-Pigeon/cwtools); update the submodule pointer
  here in a separate, clearly described commit.

## License

By contributing, you agree that your contributions are licensed under the
project's [MIT License](LICENSE).

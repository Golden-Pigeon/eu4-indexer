namespace Eu4Indexer.Core.Database

open Microsoft.Data.Sqlite
open System.Data.Common

/// A SQL backend variant. Carries the DDL and the handful of statement-level
/// differences (auto-id placeholder, ignore-on-conflict form, setup/finalize
/// statements, parameter naming) so a single relational writer can target both
/// SQLite and PostgreSQL without duplicating the insert logic.
type Dialect =
    { Name: string
      TablesSql: string
      IndexesSql: string
      /// Full-text/substring search DDL (SQLite FTS5, or Postgres pg_trgm GIN).
      SearchSql: string
      ViewsSql: string
      /// Run right after connecting, before the schema is created: bulk-load
      /// tuning, and (for an existing target) dropping any prior tables.
      SetupSql: string list
      /// Run at the end of finalize: statistics, durability restore.
      FinalizeSql: string list
      /// Placeholder for a backend-generated primary key inside a VALUES list.
      AutoId: string
      /// Wrap a plain "INSERT INTO ..." so a duplicate key is silently ignored.
      IgnoreConflict: string -> string
      /// Name a positional parameter (1-based), or leave it unnamed (positional).
      NameParam: DbParameter -> int -> unit
      /// Prepare a command after its parameters are declared. SQLite can prepare
      /// before values are bound; Npgsql cannot infer untyped parameter types
      /// upfront, so it skips explicit prepare and lets the server infer each
      /// parameter type per execution.
      PrepareCommand: DbCommand -> unit
      /// Count referential-integrity violations after load (0 when the backend
      /// enforces foreign keys eagerly or at commit time).
      VerifyIntegrity: DbConnection -> int
      /// Record the schema version using the given exec function.
      RecordVersion: (string -> unit) -> unit
      /// Backend-specific cleanup on dispose (e.g. release SQLite file locks).
      OnDispose: unit -> unit }

/// Built-in dialects.
module Dialect =

    let private sqliteVerifyIntegrity (conn: DbConnection) =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "PRAGMA foreign_key_check"
        use reader = cmd.ExecuteReader()
        let mutable count = 0

        while reader.Read() do
            count <- count + 1

        count

    /// SQLite backend: matches the original single-file writer behaviour exactly.
    let sqliteFor (gameId: string) =
        { Name = "sqlite"
          TablesSql = Schema.tablesSql gameId
          IndexesSql = Schema.indexesSql gameId
          SearchSql = Schema.ftsSql
          ViewsSql = Schema.viewsSql
          SetupSql =
            [ "PRAGMA journal_mode=OFF"
              "PRAGMA synchronous=OFF"
              "PRAGMA temp_store=MEMORY"
              "PRAGMA cache_size=-200000"
              "PRAGMA foreign_keys=OFF" ]
          FinalizeSql =
            [ "ANALYZE"
              sprintf "PRAGMA user_version=%d" Schema.UserVersion
              "PRAGMA journal_mode=WAL"
              "PRAGMA synchronous=NORMAL" ]
          AutoId = "NULL"
          IgnoreConflict = fun sql -> sql.Replace("INSERT INTO", "INSERT OR IGNORE INTO")
          NameParam = fun p i -> p.ParameterName <- sprintf "$%d" i
          PrepareCommand = fun cmd -> cmd.Prepare()
          VerifyIntegrity = sqliteVerifyIntegrity
          RecordVersion = fun _ -> () // version is set in FinalizeSql via PRAGMA
          OnDispose = fun () -> SqliteConnection.ClearAllPools() }

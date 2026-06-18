namespace Eu4Indexer.Core

open System.IO
open System.IO.Compression
open System.Net.Http

/// Fetches the cwtools config rules a game needs for indexing, straight from
/// GitHub as a zip archive — no `git` dependency, so it behaves identically on
/// Windows / macOS / Linux. Rules are MIT-licensed open source (not game
/// content). Each game maps to its own pinned config repo under
/// ~/.eu4indexer/config/<game>.
module Setup =

    type GameConfig =
        { /// GitHub "owner/repo" of the cwtools config rules.
          Repo: string
          /// Pinned ref (commit SHA) for reproducible installs.
          Ref: string }

    /// Known cwtools config repos by game id. Pinned to a known-good commit;
    /// bump deliberately. CK3 / HOI4 / Stellaris / VIC3 can be added here.
    let configForGame (gameId: string) : GameConfig option =
        match gameId with
        | "eu4" ->
            Some
                { Repo = "cwtools/cwtools-eu4-config"
                  Ref = "a85622d368bbb7afca938ed70fdd5eda44aec769" }
        | _ -> None

    let private archiveUrl (cfg: GameConfig) =
        sprintf "https://github.com/%s/archive/%s.zip" cfg.Repo cfg.Ref

    /// Recursive copy. Used instead of Directory.Move because the temp dir and the
    /// install dir can be on different volumes (e.g. C: vs the work drive on
    /// Windows CI), where Move fails with "must have identical roots".
    let rec private copyDir (src: string) (dst: string) =
        Directory.CreateDirectory dst |> ignore
        for file in Directory.GetFiles src do
            File.Copy(file, Path.Combine(dst, Path.GetFileName file), true)
        for dir in Directory.GetDirectories src do
            copyDir dir (Path.Combine(dst, Path.GetFileName dir))

    /// Download the config zip, extract it, and place its contents at
    /// ~/.eu4indexer/config/<game> (replacing any previous copy). Returns the
    /// destination directory, or an error message.
    let fetchConfig (gameId: string) (refOverride: string option) (log: string -> unit) : Result<string, string> =
        match configForGame gameId with
        | None -> Error(sprintf "no known config repo for game '%s'" gameId)
        | Some baseCfg ->
            let cfg =
                match refOverride with
                | Some r when r <> "" -> { baseCfg with Ref = r }
                | _ -> baseCfg

            let dest = AppPaths.configDir gameId
            let tmpZip = Path.Combine(Path.GetTempPath(), sprintf "eu4indexer-config-%s.zip" gameId)
            let tmpDir = Path.Combine(Path.GetTempPath(), sprintf "eu4indexer-config-%s" gameId)

            try
                let url = archiveUrl cfg
                log (sprintf "Downloading %s config from %s" gameId url)

                use client = new HttpClient()
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eu4indexer")

                (use resp = client.GetAsync(url).GetAwaiter().GetResult()
                 resp.EnsureSuccessStatusCode() |> ignore
                 use fs = File.Create tmpZip
                 resp.Content.CopyToAsync(fs).GetAwaiter().GetResult())

                if Directory.Exists tmpDir then Directory.Delete(tmpDir, true)
                ZipFile.ExtractToDirectory(tmpZip, tmpDir)

                // GitHub archives nest everything under a single "<repo>-<ref>" folder.
                let inner =
                    match Directory.GetDirectories tmpDir with
                    | [| only |] -> only
                    | _ -> tmpDir

                AppPaths.ensureDir (Directory.GetParent(dest).FullName) |> ignore
                if Directory.Exists dest then Directory.Delete(dest, true)
                copyDir inner dest

                log (sprintf "Installed %s config to %s" gameId (AppPaths.normalize dest))
                Ok dest
            with ex ->
                Error(sprintf "config download failed: %s" ex.Message)
            |> fun result ->
                // Best-effort cleanup of the temp artifacts.
                try
                    if File.Exists tmpZip then File.Delete tmpZip
                    if Directory.Exists tmpDir then Directory.Delete(tmpDir, true)
                with _ ->
                    ()

                result

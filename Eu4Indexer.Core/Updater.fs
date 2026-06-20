namespace Eu4Indexer.Core

open System
open System.Diagnostics
open System.Formats.Tar
open System.IO
open System.IO.Compression
open System.Net.Http
open System.Runtime.InteropServices

/// Self-update: replace the installed eu4indexer binary (and bundled skills)
/// with the latest GitHub release, then let the new binary refresh any config
/// rules whose pinned ref has moved on. Mirrors install.sh / install.ps1 so the
/// on-disk layout stays identical to a fresh install.
module Updater =

    /// GitHub "owner/repo" the releases hang on. Kept in step with the REPO in
    /// install.sh / install.ps1 (a deliberate keep-in-sync coupling).
    [<Literal>]
    let Repo = "Golden-Pigeon/eu4-indexer"

    /// What happened after a binary update was applied.
    type UpdateOutcome =
        /// Binary swapped in place (Unix); the caller should run the config
        /// refresh under the new binary.
        | Applied
        /// A deferred helper was launched (Windows); the caller should just exit
        /// and let the helper swap + relaunch the config refresh.
        | Deferred

    type OsKind =
        | Windows
        | MacOS
        | Linux

    /// Pure RID mapping over (os, arch) so it is unit-testable without touching
    /// the host. Matches the six published targets in build-binaries.*.
    let ridFor (os: OsKind) (arch: Architecture) : Result<string, string> =
        let osPart =
            match os with
            | Windows -> "win"
            | MacOS -> "osx"
            | Linux -> "linux"

        match arch with
        | Architecture.X64 -> Ok(osPart + "-x64")
        | Architecture.Arm64 -> Ok(osPart + "-arm64")
        | other -> Error(sprintf "unsupported architecture: %A" other)

    let private currentOs () =
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then Windows
        elif RuntimeInformation.IsOSPlatform OSPlatform.OSX then MacOS
        else Linux

    /// The RID for the running host, e.g. "osx-arm64".
    let currentRid () : Result<string, string> =
        ridFor (currentOs ()) RuntimeInformation.OSArchitecture

    /// Parse a release tag ("v1.2.3" or "1.2.3") into a comparable Version.
    let parseTag (s: string) : Version option =
        match Version.TryParse((s.Trim()).TrimStart('v', 'V')) with
        | true, v -> Some v
        | _ -> None

    /// True when `latest` is a strictly newer version than `current`.
    let isNewer (latest: string) (current: string) : bool =
        match parseTag latest, parseTag current with
        | Some l, Some c -> l > c
        | _ -> false

    /// Resolve the latest release tag via the `releases/latest` redirect, so we
    /// never hit the GitHub API rate limit (no auth, no JSON).
    let latestVersion () : Result<string, string> =
        try
            use handler = new HttpClientHandler(AllowAutoRedirect = false)
            use client = new HttpClient(handler)
            client.DefaultRequestHeaders.UserAgent.ParseAdd("eu4indexer")
            let url = sprintf "https://github.com/%s/releases/latest" Repo
            use resp = client.GetAsync(url).GetAwaiter().GetResult()

            match resp.Headers.Location with
            | null -> Error "could not resolve the latest release (no redirect)"
            | loc ->
                let s = loc.ToString()
                let marker = "/tag/"
                let idx = s.LastIndexOf marker

                if idx < 0 then
                    Error "no releases found"
                else
                    Ok(s.Substring(idx + marker.Length))
        with ex ->
            Error(sprintf "version check failed: %s" ex.Message)

    /// Download URL for a target's release asset (version-less; the latest tag
    /// selects the version).
    let assetUrl (rid: string) =
        let ext = if rid.StartsWith "win-" then "zip" else "tar.gz"
        sprintf "https://github.com/%s/releases/latest/download/eu4indexer-%s.%s" Repo rid ext

    /// True when the running process lives in the managed install's bin dir, i.e.
    /// it was installed by the script rather than run from a source checkout
    /// (dotnet run / bin/Debug/...). Self-update only makes sense for the former.
    let isManagedInstall () : bool =
        match Environment.ProcessPath with
        | null -> false
        | p ->
            let procDir = Path.GetFullPath(Path.GetDirectoryName p)
            let binDir = Path.GetFullPath(AppPaths.binDir ())
            String.Equals(procDir, binDir, StringComparison.OrdinalIgnoreCase)

    let rec private copyDir (src: string) (dst: string) =
        Directory.CreateDirectory dst |> ignore
        for file in Directory.GetFiles src do
            File.Copy(file, Path.Combine(dst, Path.GetFileName file), true)
        for dir in Directory.GetDirectories src do
            copyDir dir (Path.Combine(dst, Path.GetFileName dir))

    /// Download the release archive and extract it, returning the staged root
    /// that holds bin/ (+ skills/), or an error.
    let private downloadAndStage (rid: string) (log: string -> unit) : Result<string, string> =
        let ext = if rid.StartsWith "win-" then "zip" else "tar.gz"
        let tmpArchive = Path.Combine(Path.GetTempPath(), sprintf "eu4indexer-update.%s" ext)
        let tmpDir = Path.Combine(Path.GetTempPath(), "eu4indexer-update")

        try
            let url = assetUrl rid
            log (sprintf "Downloading %s" url)

            use client = new HttpClient()
            client.DefaultRequestHeaders.UserAgent.ParseAdd("eu4indexer")

            (use resp = client.GetAsync(url).GetAwaiter().GetResult()
             resp.EnsureSuccessStatusCode() |> ignore
             use fs = File.Create tmpArchive
             resp.Content.CopyToAsync(fs).GetAwaiter().GetResult())

            if Directory.Exists tmpDir then Directory.Delete(tmpDir, true)
            Directory.CreateDirectory tmpDir |> ignore

            if ext = "zip" then
                ZipFile.ExtractToDirectory(tmpArchive, tmpDir)
            else
                use fs = File.OpenRead tmpArchive
                use gz = new GZipStream(fs, CompressionMode.Decompress)
                TarFile.ExtractToDirectory(gz, tmpDir, true)

            // Archive holds bin/ + skills/ at the root, or nested one level.
            let root =
                if Directory.Exists(Path.Combine(tmpDir, "bin")) then
                    Some tmpDir
                else
                    Directory.GetDirectories tmpDir
                    |> Array.filter (fun d -> Directory.Exists(Path.Combine(d, "bin")))
                    |> function
                        | [| only |] -> Some only
                        | _ -> None

            match root with
            | Some r -> Ok r
            | None -> Error "downloaded package has no bin/ directory"
        with ex ->
            Error(sprintf "download/extract failed: %s" ex.Message)
        |> fun result ->
            try
                if File.Exists tmpArchive then File.Delete tmpArchive
            with _ ->
                ()

            result

    /// Replace `target` with `staged`'s contents, keeping a backup until the new
    /// copy is in place. On Unix the running process keeps its old (now unlinked)
    /// inode, so swapping the live bin dir is safe.
    let private swapDir (staged: string) (target: string) =
        if Directory.Exists target then
            let backup = target + ".old-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss")
            Directory.Move(target, backup)

            try
                copyDir staged target
                Directory.Delete(backup, true)
            with _ ->
                if Directory.Exists target then Directory.Delete(target, true)
                Directory.Move(backup, target)
                reraise ()
        else
            copyDir staged target

    /// In-process swap for Unix: replace bin/ (+ skills/ if shipped), mark the
    /// apphost executable, and clear the macOS quarantine flag.
    let private applyUnix (stagedRoot: string) (log: string -> unit) =
        let home = AppPaths.home ()
        let binTarget = AppPaths.binDir ()
        swapDir (Path.Combine(stagedRoot, "bin")) binTarget

        let stagedSkills = Path.Combine(stagedRoot, "skills")

        if Directory.Exists stagedSkills then
            swapDir stagedSkills (Path.Combine(home, "skills"))

        let apphost = Path.Combine(binTarget, "eu4indexer")

        if File.Exists apphost then
            File.SetUnixFileMode(
                apphost,
                UnixFileMode.UserRead
                ||| UnixFileMode.UserWrite
                ||| UnixFileMode.UserExecute
                ||| UnixFileMode.GroupRead
                ||| UnixFileMode.GroupExecute
                ||| UnixFileMode.OtherRead
                ||| UnixFileMode.OtherExecute
            )

        if RuntimeInformation.IsOSPlatform OSPlatform.OSX then
            try
                let psi = ProcessStartInfo("xattr", sprintf "-dr com.apple.quarantine \"%s\"" home)
                psi.UseShellExecute <- false
                use p = Process.Start psi
                p.WaitForExit()
            with _ ->
                ()

        log (sprintf "Replaced %s" (AppPaths.normalize binTarget))

    /// Windows can't overwrite a running .exe or its loaded DLLs, so write a
    /// detached helper that waits for this process to exit, swaps the dirs, and
    /// relaunches the new binary to finish the config refresh.
    let private applyWindows (stagedRoot: string) (log: string -> unit) =
        let home = AppPaths.home ()
        let binTarget = AppPaths.binDir ()
        let pid = Process.GetCurrentProcess().Id
        let stagedBin = Path.Combine(stagedRoot, "bin")
        let stagedSkills = Path.Combine(stagedRoot, "skills")
        let exe = Path.Combine(binTarget, "eu4indexer.exe")

        let skillsBlock =
            if Directory.Exists stagedSkills then
                sprintf
                    "Remove-Item -Recurse -Force '%s' -ErrorAction SilentlyContinue\nCopy-Item -Recurse -Force '%s' '%s'\n"
                    (Path.Combine(home, "skills"))
                    stagedSkills
                    (Path.Combine(home, "skills"))
            else
                ""

        let script =
            sprintf
                "$ErrorActionPreference = 'SilentlyContinue'\n\
                 try { Wait-Process -Id %d -Timeout 120 } catch {}\n\
                 Remove-Item -Recurse -Force '%s'\n\
                 Copy-Item -Recurse -Force '%s' '%s'\n\
                 %s\
                 Remove-Item -Recurse -Force '%s'\n\
                 Start-Process -FilePath '%s' -ArgumentList 'update','--finish-config'\n"
                pid
                binTarget
                stagedBin
                binTarget
                skillsBlock
                stagedRoot
                exe

        let scriptPath = Path.Combine(Path.GetTempPath(), sprintf "eu4indexer-update-%d.ps1" pid)
        File.WriteAllText(scriptPath, script)

        let psi = ProcessStartInfo("powershell")
        psi.Arguments <- sprintf "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"%s\"" scriptPath
        psi.UseShellExecute <- true
        Process.Start psi |> ignore
        log "Update will finish after this process exits."

    /// Apply a binary update: guard against source runs, download, and swap.
    /// Returns Applied (Unix, caller runs the config refresh) or Deferred
    /// (Windows, a helper will).
    let applyBinaryUpdate (log: string -> unit) : Result<UpdateOutcome, string> =
        if not (isManagedInstall ()) then
            Error
                "this looks like a source/dev build (not a script install); update via git instead"
        else
            match currentRid () with
            | Error e -> Error e
            | Ok rid ->
                match downloadAndStage rid log with
                | Error e -> Error e
                | Ok stagedRoot ->
                    try
                        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
                            applyWindows stagedRoot log
                            Ok Deferred
                        else
                            applyUnix stagedRoot log

                            try
                                Directory.Delete(stagedRoot, true)
                            with _ ->
                                ()

                            Ok Applied
                    with ex ->
                        Error(sprintf "update failed: %s" ex.Message)

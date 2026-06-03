# o-down

A high-performance download manager for Windows, built on **WinUI 3** (Windows App SDK 1.5) and **.NET 8**. o-down bundles three sidecar tools to give you the most feature-rich, optimized download experience possible on Windows:

- **[aria2c](https://aria2.github.io/)** — multi-connection, multi-source, segmented HTTP/HTTPS/FTP downloads
- **[yt-dlp](https://github.com/yt-dlp/yt-dlp) + [ffmpeg](https://ffmpeg.org/)** — 1000+ media sites, format selection, transcoding
- **[MonoTorrent](https://github.com/alanmcgovern/MonoTorrent)** — BitTorrent (magnet links, .torrent files, DHT, PEX)

---

## Features

### Core Reliability
- ✅ Pause / resume (per-task and global)
- ✅ Automatic error recovery (exponential backoff, mirror failover)
- ✅ Checksum verification (MD5, SHA-1, SHA-256, SHA-384, SHA-512) — streamed during write, no re-read
- ✅ Crash-safe resume state persisted in SQLite

### Performance
- ✅ Segmented / multi-threaded downloading (default 16 connections, configurable to 64)
- ✅ Multi-source / mirror support (paste comma- or newline-separated mirrors)
- ✅ Bandwidth throttling (per-task and global, hot-tunable)
- ✅ Connection pool, disk cache (64 MB), pipelining

### Organization and Automation
- ✅ Queue with priority levels and drag-reorder
- ✅ Cron-based schedules (e.g. "start at 03:00 daily")
- ✅ Auto-sort into folders by extension or regex rule
- ✅ Bulk / batch link processing — paste any text, URLs auto-extracted and classified
- ✅ Default rules pre-seeded: Video, Audio, Images, Docs, Archives, Installers

### Utility and Integration
- ✅ Browser extension: **Chrome / Edge / Brave / Opera** (MV3) and **Firefox** (MV2 + MV3)
- ✅ Post-download actions: run a script, shutdown, hibernate, sleep, lock, logout, open folder
- ✅ Media extraction and format conversion (pick from yt-dlp's format list, ffmpeg remux)
- ✅ Clipboard monitoring (off by default; consent prompt on first run)
- ✅ Native messaging host (separate small EXE, no dependencies on the browser)

### Torrent Support
- ✅ Magnet links and `.torrent` files
- ✅ DHT, PEX, μTP, Local Peer Discovery
- ✅ Per-torrent sequential download
- ✅ Persistent piece-level resume (auto-saved by MonoTorrent)

---

## Project Layout

```
o-down.sln
├── src/
│   ├── o-down.App/                  # WinUI 3 host (unpackaged)
│   ├── o-down.Core/                 # Domain models, interfaces, pipeline
│   ├── o-down.Data/                 # EF Core SQLite context
│   ├── o-down.Infrastructure/       # Clipboard monitor, registry, paths
│   ├── o-down.Update/               # Self-updater
│   ├── o-down.Engines.Aria2/        # JSON-RPC client + host process
│   ├── o-down.Engines.Torrent/      # MonoTorrent wrapper
│   ├── o-down.Engines.Media/        # yt-dlp + ffmpeg invocation
│   └── o-down.NativeMessaging/      # Small EXE for browser host
├── tools/                           # Bundled sidecars (copied at build)
├── extensions/                      # Browser extension manifests
├── tests/                           # xUnit tests
└── build/                           # Build scripts (to be added)
```

## Building

### Prerequisites
- Windows 10 1809+ (or Windows 11)
- **Visual Studio 2022** with the **Windows App SDK** workload and **.NET 8 SDK**
  - Standalone `.NET 8 SDK` works for non-WinUI projects (Core, Data, Infrastructure, engines, tests)
  - WinUI 3 XAML compilation **requires** the `Microsoft.WindowsAppSDK` workload from VS 2022
- Sidecar binaries placed in `tools/` (see [Sidecars](#sidecars))

### Build everything
```pwsh
dotnet build o-down.sln -c Release
```

### Run the WinUI 3 app
```pwsh
dotnet run --project src/o-down.App -c Release
```

### Produce a portable release (zip + update manifest)
```pwsh
.\build.ps1 -Version 1.2.0 -DownloadUrl https://updates.example.com/o-down-1.2.0.zip
# or, with the cmd shim:
.\build.cmd -Version 1.2.0 -DownloadUrl https://updates.example.com/o-down-1.2.0.zip
```

`build.ps1` publishes the App (`win-x64`, self-contained) and the native-messaging host, bundles `aria2c.exe` / `yt-dlp.exe` / `ffmpeg.exe` / `ffprobe.exe` from `tools/`, copies the native-messaging host into the App output, zips the result, and writes `dist\latest.json` with the SHA-256 + size of the zip (via `o-down.Update.UpdateManifestBuilder`).

Output:
- `dist\o-down-<Version>.zip` — portable, drop-in install (extract anywhere)
- `dist\latest.json` — feed manifest, ready to upload next to the zip

For an ARM64 build: `.\build.ps1 -Runtime win-arm64 -Version 1.2.0 ...`. Sidecar binaries are picked from `tools\aria2c\arm64\` and `tools\ffmpeg\arm64\` automatically.

### Run tests
```pwsh
dotnet test o-down.sln -c Release
```

#### Integration tests

Most unit tests are pure xUnit and have no external dependencies. The Aria2 engine has a small set of **integration tests** that spawn a real `aria2c.exe` subprocess and an in-process HTTP file server. They are tagged `[Trait("Category", "Integration")]` and excluded from the default `dotnet test` run by default.

To run them:

1. Download a Windows build of aria2 and drop the binary at one of:
   - `tools/aria2c/x64/aria2c.exe` (preferred)
   - `tools/aria2c/aria2c.exe` (alt, flat)
   - `C:\Program Files\aria2\aria2c.exe` (system install fallback)
2. Run with the explicit filter:
   ```pwsh
   dotnet test tests/o-down.Engines.Aria2.Tests -c Release --filter "Category=Integration"
   ```

If the binary is missing, the integration tests will fail with a clear message: `aria2c.exe not available. Drop a binary at tools/aria2c/x64/aria2c.exe to run integration tests.`

**What the integration tests cover:**
- `EndToEnd_DownloadsFile_ViaRealAria2` — full download of a 4 MB seed file, verifies the engine's `ProgressChanged` and `Completed` events fire, the file lands on disk, the gid is purged from aria2's stopped list, and `QueryAsync` returns `null` afterwards.
- `ForceRemoveAsync_StopsInFlightDownload` — throttled in-progress download, calls `ForceRemoveAsync` while the gid is still active, verifies state transitions to `Removed`, the partial file (and `.aria2` control file) is cleaned up on disk, and the gid is purged from aria2's cache.
- `ChangeOptionAsync_AcceptsMidFlightChanges` — calls `changeOption` mid-flight to update `user-agent` and a custom header, verifies the RPC succeeds and the download remains in `Running` state.
- `PurgeCompletedResultsAsync_RemovesStoppedResults` — force-removes an active download, confirms the gid shows up in `tellStopped`, calls `PurgeCompletedResultsAsync`, and verifies the gid is no longer in the stopped list.

The in-process HTTP file server in the tests is intentionally throttled (16 KB chunks with a 20 ms delay between writes) so that the test can reliably observe the in-flight state before completion. The default Windows test discovery filters out the integration category so `dotnet test` remains fast and offline.

**Diagnostic environment variables:**
- `ODOWN_KEEP_TEST_DIR=1` — preserves the per-test work directory under `%TEMP%\odown-aria2-it-*` for postmortem. Otherwise it is cleaned up on `DisposeAsync`.
- `ODOWN_RPC_LOG=1` — (diagnostic, off by default) writes the raw JSON-RPC bodies to `%TEMP%\odown-rpc.log`.

## Sidecars

Place the following binaries under `tools/` (or in the directory indicated) before first run:

| Tool       | Path (preferred)                          | Path (alt, flat)          | License         |
|------------|-------------------------------------------|---------------------------|-----------------|
| `aria2c`   | `tools/aria2c/{x64|arm64}/aria2c.exe`     | `tools/aria2c/aria2c.exe` | GPLv2           |
| `yt-dlp`   | `tools/yt-dlp/yt-dlp.exe`                 | —                         | Unlicense       |
| `ffmpeg`   | `tools/ffmpeg/{x64|arm64}/ffmpeg.exe`     | `tools/ffmpeg/ffmpeg.exe` | LGPL/GPL (build) |

Downloads:
- **aria2**: https://github.com/aria2/aria2/releases (build: `aria2-*-win-64bit-build1.zip` for x64, `aria2-*-win-arm64-build1.zip` for arm64)
- **yt-dlp**: https://github.com/yt-dlp/yt-dlp/releases (download `yt-dlp.exe`)
- **ffmpeg**: https://www.gyan.dev/ffmpeg/builds/ (essentials, full shared) for x64; for arm64 use https://github.com/BtbN/FFmpeg-Builds

The `SidecarManager` falls back to whatever it finds in `PATH` if the bundled binaries are missing.

## Architecture

```
+----------------------------+        Named Pipe
|  o-down.App (WinUI 3)      | <--------------------+
|  - NavigationView shell    |                      |
|  - ViewModels              |                      |
|  - DownloadOrchestrator    |                      |
+-----------+----------------+                      |
            |                                       |
            |  JSON-RPC (HTTP 127.0.0.1:6800)       |
            v                                       |
   +----------------+    stdio     +----------------+------------+
   |   aria2c.exe   |              | o-down.NativeMessaging  |
   +----------------+              |   (browser host EXE)    |
                                   +----------------+---------+
            +----------------+                   ^
            |   yt-dlp.exe   |                   | Native Messaging
            |   ffmpeg.exe   |                   | (stdin/stdout JSON)
            +----------------+        +----------+----------+
                                       | Chrome/Edge/FF ext |
                                       +--------------------+
```

- **Process model**: aria2, yt-dlp, and ffmpeg are spawned and supervised by o-down. Communication with the browser is via a separate small host EXE that talks Native Messaging (stdio) and forwards to o-down over a named pipe (`\\.\pipe\o-down-link`).
- **State**: SQLite (WAL) at `%LOCALAPPDATA%\o-down\odown.db`. Resume data is in the same DB plus per-engine files (`.aria2` for HTTP, MonoTorrent's own cache dir for torrents).
- **Threading**: single `DownloadOrchestrator` loop with a priority queue and a configurable concurrency cap (default 5).

## Distribution

- **Unpackaged** by default (portable `.exe` + sidecars). Settings live in `%LOCALAPPDATA%\o-down\`, so the binary can be moved freely.
- Single-instance enforced at startup (re-activation focuses the running window).
- **Auto-update** via `UpdateService` (M6 milestone) — checks a JSON manifest at `https://updates.example.com/o-down/{channel}/latest.json`.

## License

Source-available, closed-source. See [LICENSE.md](LICENSE.md) (TBD) for terms. Bundled sidecars retain their own licenses (GPL, Unlicense, LGPL — see `Settings → About`).

## Project Status

**Milestone 6 (polish: update flow, settings persistence, single-instance, scheduled update checks, build/publish script) — in progress.** 210 tests pass: 118 Core unit + 11 Media engine unit + 10 Aria2 unit + 17 Torrent engine unit + 4 Infrastructure unit + 35 Update unit + 7 Infrastructure unit + 4 Aria2 integration (real `aria2c.exe` + in-process HTTP file server) + 1 Infrastructure integration (spawns the real native-messaging host EXE) + 4 Torrent integration (in-process MonoTorrent seeder + leecher). The remaining M6 work is tray icon (needs H.NotifyIcon.WinUI, blocked by the sandbox XAML compiler).

### M6: polish (update flow, settings persistence, single-instance, scheduled update checks)

- **`AppSettings` model + `JsonAppSettingsStore`** (`src/o-down.Core/Models/AppSettings.cs`, `src/o-down.Core/Pipeline/JsonAppSettingsStore.cs`): pure JSON-on-disk settings (default download dir, max concurrent downloads, clipboard-monitor toggle, update channel, minimize-to-tray, theme, etc.). Atomic writes via `.tmp` + `File.Move(overwrite: true)`. Semaphore-guarded for concurrent save. Falls back to defaults when the file is missing or corrupt. Path defaults to `%LOCALAPPDATA%\o-down\settings.json`.
- **`UpdateService` rewrite** (`src/o-down.Update/UpdateService.cs`): adds a real `IsNewer` helper (handles invalid/empty versions), `VerifySha256Async` (accepts uppercase, lowercase, and dashed hex; skips when expected is empty), and `ApplyAsync(zipPath, currentExePath)` — extracts the update zip to a staging dir next to the current app dir, renames the live app dir to `.old-{timestamp}` (rollback), moves the staging dir in, then deletes the backup. Throws `FileNotFoundException` if the current exe path is invalid. `DownloadAsync` now validates the `Content-Length` against the manifest's `SizeBytes` and detects truncated downloads. New `CurrentVersion` and `AppDirectory` properties for callers that need them.
- **`UpdateCheckScheduler`** (`src/o-down.Update/UpdateCheckScheduler.cs`): background loop that periodically calls `UpdateService.CheckAsync` using the live `AppSettings.UpdateChannel` and `AppSettings.AutoUpdateEnabled`. Re-reads settings on every check so a mid-loop user toggle is honoured. Raises `CheckCompleted` on every result and `UpdateAvailable` only when `HasUpdate`. Default interval is 6 hours; configurable via constructor. `Start()` is idempotent; `StopAsync()` cancels cleanly.
- **`SingleInstanceGuard`** (`src/o-down.Infrastructure/SingleInstanceGuard.cs`): named-mutex single-instance check + named-pipe focus-signal server. `TryAcquire()` returns true only for the first instance. The first instance calls `StartFocusServer(pipeName, onFocusRequested)` to listen for `SendFocusMessageAsync` calls. Subsequent instances send the focus message (with their command-line args) and exit, so launching the .exe again just brings the existing window to the front.
- **App startup wiring** (`App.xaml.cs`): pre-DI single-instance check at the very start of `OnLaunched`; if a previous instance exists, sends the focus signal and exits. `UpdateService`, `UpdateCheckScheduler`, `IAppSettingsStore`, and `SingleInstanceGuard` are now properly resolved through DI (the previous `AddSingleton<UpdateService>()` was a no-op because the constructor needs `IUpdateFeed`/`appDir`/`Version`). After the orchestrator starts, the update scheduler is started and its `UpdateAvailable` event is logged.
- **`UpdateManifestBuilder`** (`src/o-down.Update/UpdateManifestBuilder.cs`): pure helper that turns a zip into an `UpdateManifest` (computes SHA-256 + size, defaults channel/release-date) and writes `latest.json` atomically. Used by `build.ps1` to generate the feed manifest.
- **Build/publish script** (`build.ps1` + `build.cmd`): one-shot release pipeline. Restores, publishes the App (`win-x64`, self-contained, x64), publishes the native-messaging host, bundles `aria2c` / `yt-dlp` / `ffmpeg` / `ffprobe` from `tools/`, copies the native-messaging host into the App output, zips everything, and writes `dist\latest.json` via `UpdateManifestBuilder`. ARM64 supported via `-Runtime win-arm64` (sidecars picked from `tools\aria2c\arm64\` and `tools\ffmpeg\arm64\`).

**What the M6 tests cover** (17 UpdateService + 9 AppSettings + 9 UpdateCheckScheduler + 9 UpdateManifestBuilder + 7 SingleInstanceGuard = 51 new tests):
- `UpdateServiceTests` — version compare (newer/equal/lesser, invalid/empty/null), SHA-256 verify (match/mismatch/dashed/empty), `StageAsync` (extract + overwrite), `ApplyAsync` (replace contents, delete backup+staging, throw on missing exe), `CheckAsync` (newer/equal/missing manifest).
- `JsonAppSettingsStoreTests` — default-when-missing, round-trip, `Current` updates, atomic no-tmp, overwrite, corrupt-JSON fallback, nested-dir creation, `Reload`, concurrent saves.
- `UpdateCheckSchedulerTests` — skip when auto-update disabled, call feed with current channel, raise `UpdateAvailable` on has-update, raise `CheckCompleted` always, run at least once on `Start`, idempotent `Start`, `LastResult` is set, reads latest settings on each call, mid-loop auto-update toggle is honoured.
- `UpdateManifestBuilderTests` — SHA-256 + size from zip, channel default to stable, release-date default to now, throw on missing zip, reject blank version, create parent directory, atomic overwrite, JSON round-trip, end-to-end build-then-write-then-read.
- `SingleInstanceGuardTests` — first/second acquire, release allows re-acquire, focus message round-trip, timeout-when-no-server, throws when not first instance, `MutexName` accessor.

### M5: torrent engine (MonoTorrent)

- **`TorrentDownloadEngine : IDownloadEngine` (Kind=Torrent)** (`src/o-down.Engines.Torrent/TorrentDownloadEngine.cs`): wraps a `MonoTorrent.Client.ClientEngine` and surfaces real `ProgressChanged` / `Completed` events. `Completed` fires once on `Seeding` (deduped via `_completionFired`) and once on `Error`. State mapping covers all 10 `TorrentState` values: `Downloading → Running`, `Seeding → Completed`, `Paused/HashingPaused/Stopped/Stopping → Paused`, `Error → Failed`, `Hashing → Verifying`, `Metadata → FetchingMetadata`, `Starting → Running`.
- **O(1) manager lookup**: `_byManager` reverse map (TorrentManager → DownloadId) so the state-changed handler resolves the download id in O(1) instead of scanning `_byDownloadId`.
- **Per-torrent settings** (`BuildTorrentSettings(item)`): `MaximumConnections`, `MaximumDownloadSpeed` (Int32), `UploadSlots`, `AllowDht`, `AllowPeerExchange`, `AllowInitialSeeding`, `CreateContainingDirectory`.
- **File priorities** (`ApplyFilePrioritiesAsync`): uses `manager.SetFilePriorityAsync(file, priority)` to mark excluded files as `Priority.DoNotDownload` and included files as `Priority.Normal`. The engine's `TorrentFile` model is the canonical input; `TorrentWantedFiles` on `DownloadItem` is resolved through `TorrentFileSelector`.
- **Progress**: byte counts derived from `m.Bitfield.Length` × `PieceLength` (counting `Bitfield[i]=true`), clamped to torrent size. Speed from `m.Monitor?.DownloadSpeed`. Peer count from `m.Peers?.Available`, connection count from `m.OpenConnections`.
- **Magnet link parser** (`src/o-down.Core/Pipeline/MagnetLinkParser.cs`): pure parser for `xt=urn:btih:...`, `urn:btmh:...`, `dn`, `tr`, `ws`, `xs`, `kt`, `as`, `xl`. Returns a structured `MagnetLinkInfo`.
- **Torrent file selector** (`src/o-down.Core/Pipeline/TorrentFileSelector.cs`): pure spec parser supporting `all`, `video`, `audio`, `images`, `subs`, `regex:...`, `ext:jpg,srt`, `size>500MB`, `size<1MB`, comma-separated indices, and a single index.
- **`DownloadItem` torrent options**: `TorrentSequential` (no-op in MonoTorrent 2.0 — no public sequential picker), `TorrentFirstLastPieceFirst=true`, `TorrentMaxConnections?`, `TorrentMaxDownloadSpeed?`, `TorrentWantedFiles?`, `TorrentUploadSlots=8`.
- **DI wiring** (`App.xaml.cs`): `TorrentDownloadEngine` registered as both a concrete singleton and as `IDownloadEngine`, so `DownloadRouter` routes `Kind=Torrent` items to it.

**What the torrent tests cover** (17 unit + 4 integration):
- `TorrentDownloadEngineUnitTests` — constructor, name, kind, availability, empty torrents, query, pause/resume, remove unknown handles, bandwidth/sequential/purge no-throw, event subscription, `ProbeAsync`/`AddAsync` error paths.
- `TorrentTestBuilder` — builds BitTorrent v1 torrents (single-file and multi-file) by emitting a hand-rolled `BEncodedDictionary` since MonoTorrent 2.0 has no public `Torrent.Create` method. Uses `SHA1` for piece hashes; piece hashes are computed over the **byte stream with padding** so multi-file torrents match MonoTorrent's on-disk piece alignment.
- `TorrentRoundTripIntegrationTests`:
  - `Engine_CanDownloadFromInProcessSeeder` — runs an in-process seeder (`ClientEngine` on a random TCP port) + our leecher (`TorrentDownloadEngine`). Verifies that the leecher's `Completed` event fires, the file lands on disk, and its SHA-256 matches the original payload.
  - `Engine_RespectsWantedFiles_ExcludesExcluded` — multi-file torrent, marks one file as wanted via `TorrentWantedFiles`, verifies the wanted file is written and the unwanted file is not.
  - `TorrentTestBuilder_ProducesValidSingleFileTorrent` / `TorrentTestBuilder_ProducesValidMultiFileTorrent` — builder validation: the produced `.torrent` round-trips through `MTorrent.Load` and exposes the correct name, size, piece length, and file entries.
- The integration tests use a `FindFreeTcpPort` helper, a `TryConnectToListenerAsync` probe to confirm the seeder is actually listening before peer injection, and reflection to access the leecher's private `_engine` field for `AddPeersAsync(new[] { new Peer(new BEncodedString(20 bytes), new Uri("tcp://127.0.0.1:port")) })`. Tests honour `ODOWN_KEEP_TEST_DIR=1` to preserve the per-test work dir under `%TEMP%\odown-m5-roundtrip-*` for postmortem.

**M5 caveats / deferred**:
- `SetSequentialAsync` is a documented no-op — MonoTorrent 2.0 has no public `SequentialPicker`. Sequential download would require a custom `IPieceRequester` swap.
- `SetBandwidthLimitAsync` is a documented no-op for in-flight torrents — MonoTorrent 2.0 `EngineSettings` are read-only at runtime, so the limit only takes effect on newly-added torrents.

### M4: media engine (yt-dlp + ffmpeg) (previous)

- **`MediaDownloadEngine : IDownloadEngine`** (`src/o-down.Engines.Media/MediaDownloadEngine.cs`): probes the URL via `IMediaExtractor`, picks a format from the probe, spawns yt-dlp, and reports `ProgressChanged` / `Completed` events. Handles per-download cancellation, pause (kills process), resume (no-op for media — re-add required), and remove (with optional file delete).
- **`IMediaExtractor.DownloadAsync`** now takes an `Action<DownloadProgress>? progress` parameter. The `YtDlpMediaExtractor` reads yt-dlp's `--newline` stderr output and forwards each `YtDlpEvent` (progress, completed, destination, error) to the callback. The download respects `MediaAudioOnly` (adds `-x --audio-format`), `MediaWriteSubtitles` (adds `--write-subs` and `--sub-langs`), `MediaEmbedSubtitles` (adds `--embed-subs`), and `MediaSponsorblockRemove` (adds `--sponsorblock-remove`).
- **Pure helpers in `o-down.Core/Pipeline/`**:
  - `FormatSelector` — picks a `MediaFormat` from a `MediaProbe` per `MediaFormatPreference` (Best, Worst, BestVideoOnly, BestAudioOnly, Smallest, Largest, Custom) and generates the corresponding yt-dlp `-f` expression.
  - `YtDlpProgressParser` — parses yt-dlp's stderr lines (progress `[download] 42% of 10MiB at 1MiB/s ETA 00:04`, completed `100%`, destination, merger, error) into structured `YtDlpEvent` records. Also has unit-tested `TryParseSize` and `TryParseSpeed` for `1.23MiB`, `500KiB`, `2.0TiB`, etc.
  - `OutputTemplateResolver` — resolves yt-dlp output templates (`%(title)s.%(ext)s`, `%(uploader)s/%(title)s.%(ext)s`) against a `MediaTemplateContext` (probe data) up-front so the App can register the expected final path with the engine. Sanitises invalid filename chars.
- **`DownloadItem` media options**: `MediaFormatId`, `MediaFormatPreference`, `MediaAudioOnly`, `MediaAudioFormat`, `MediaWriteSubtitles`, `MediaEmbedSubtitles`, `MediaSubtitleLanguages`, `MediaOutputTemplate`, `MediaSponsorblockRemove`, `MediaChapterStart`, `MediaChapterEnd`.
- **`DownloadRouter`** now routes `Media` items to a media engine when one is registered (falling back to an HTTP engine if not). Engines are registered as `IDownloadEngine` via `sp.GetRequiredService<X>()` so the router sees both `Kind == Http` (aria2) and `Kind == Media` (yt-dlp) instances.
- **DI wiring** (`App.xaml.cs`): `MediaDownloadEngine` is registered as both a concrete singleton and as `IDownloadEngine`. `FfmpegTranscoder` now uses the `ILogger` from DI.

**What the media tests cover** (11 unit, 0 integration — integration tests require real `yt-dlp.exe` and `ffmpeg.exe` binaries in `tools/yt-dlp/` and `tools/ffmpeg/x64/`):
- `FormatSelectorTests` — empty list, custom ID, best/worst, video-only/audio-only, smallest/largest, null-size handling, expression generation
- `YtDlpProgressParserTests` — progress lines with/without speed/ETA/fragments, completion, destination, merger, error, info, garbage, size parsing (KiB/MiB/GiB/TiB), speed parsing
- `OutputTemplateResolverTests` — title+ext, invalid char sanitisation, uploader, empty template fallback, unknown token, trailing-dot trim, resolution+fps
- `MediaDownloadEngineTests` (uses `FakeMediaExtractor`): progress+completed events, explicit format ID, audio-only fallback, output template resolution, failure propagation, unavailable extractor rejection, cancel via `RemoveAsync`, `QueryAllAsync` snapshot

### M3: browser extension and clipboard

- **Wire protocol** (`src/o-down.Core/Protocol/NativeMessageCodec.cs`): canonical 4-byte little-endian length prefix + UTF-8 JSON, used by both halves (browser host ↔ named pipe). Requests carry `url`, `referrer`, `cookies`, `filenameHint`, `source`, `capturedAt`; responses carry `ok`, `downloadId`, `error`, `version`.
- **Native-messaging host EXE** (`src/o-down.NativeMessaging/`): small `WinExe` that reads one request per loop iteration from stdin, forwards to the named pipe `\\.\pipe\o-down-link`, writes the response to stdout, then loops. Honors `ODOWN_PIPE_NAME` (test/CI override) and `ODOWN_HOST_LOG` (diagnostic file log).
- **Named-pipe server** (`src/o-down.Infrastructure/NamedPipeLinkServer.cs`): 4-listener `ListenLoop`, bidirectional `PipeDirection.InOut`, pluggable `Func<CapturedLink, NativeResponse>` responder (so the App can return a `DownloadItem.Id` to the browser). When no responder is wired, the server still fires `LinkCaptured` for in-process consumers.
- **Native-messaging registrar** (`src/o-down.Infrastructure/NativeMessagingRegistrar.cs`): writes per-browser manifest JSON to `%LOCALAPPDATA%\o-down\native-messaging\{chrome,firefox}\o_down_native_messaging.json` and registers HKCU keys for Chrome, Edge, and Firefox. Chrome manifest uses `allowed_origins`; Firefox uses `allowed_extensions` (the two browsers expect different fields).
- **Clipboard monitor** (`src/o-down.Infrastructure/WindowsClipboardMonitor.cs`): hidden message-only window registered with `AddClipboardFormatListener`, 2 s debounce, `UrlClassifier.IsUrl` filter, raises `TextCaptured` only for URLs.
- **Consent gate** (`src/o-down.Core/Abstractions/IConsentStore.cs` + `FileConsentStore`): per-feature opt-in stored at `%LOCALAPPDATA%\o-down\consent.json`. The App only starts the clipboard monitor when the user has granted consent; the default is off.
- **Browser extensions** (`extensions/`): three manifests — Chrome/Edge MV3, Firefox MV3, Firefox MV2 — with context-menu items, toolbar action, and an "extract all links on page" helper that requires the `scripting` permission.

#### Installing the browser extension

1. Build the host: `dotnet publish src/o-down.NativeMessaging -c Release -r win-x64 --self-contained false` (the host needs to live in a stable path so the manifest can point to it).
2. Place the published `o-down.NativeMessaging.exe` somewhere stable (e.g., next to `o-down.App.exe`).
3. Run the App once; the Settings page exposes a "Register native messaging host" button that calls `NativeMessagingRegistrar.Register(hostExePath)`. This writes the manifest JSON files and the registry keys. Unregister with the matching button.
4. Chrome/Edge: load `extensions/chrome/` as an unpacked extension (`chrome://extensions` → Developer mode → "Load unpacked"). For an installed extension, the host manifest's `allowed_origins` must include the real extension ID — update `NativeMessagingRegistrar.ChromeExtensionId` before publishing to the Chrome Web Store.
5. Firefox MV3: `about:debugging#/runtime/this-firefox` → "Load Temporary Add-on" → pick `extensions/firefox-mv3/manifest.json`. For a permanent install, sign via AMO and update `NativeMessagingRegistrar.FirefoxExtensionId`.
6. Firefox MV2: same as above, pointing at `extensions/firefox-mv2/`.

If o-down is not running when the browser sends a message, the host returns `{"ok":false,"error":"o-down is not running","version":"0.1.0"}` after a 2 s connect timeout.

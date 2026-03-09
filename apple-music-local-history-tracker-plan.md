# Apple Music Local History Tracker Plan

## Summary
- Build a Windows-only C# WPF tray app on `.NET 10` that uses the same capture method as `AMWin-RP`: `FlaUI` + Windows UI Automation against the Microsoft Store Apple Music app.
- Run it as a per-user tray app at Windows logon, not as a true Windows Service. `AMWin-RP`’s approach depends on the interactive desktop session, so Session 0 service scraping is out of scope for v1.
- Store future listening history in a local SQLite database at `%LocalAppData%\AppleMusicTracker\history.sqlite`.
- V1 will collect and persist every detected listen, even very short ones, without inserting duplicate rows every second. It will store track catalog data, listening sessions, and session events, then expose CSV/JSON export from the tray menu.

## What AMWin-RP already proves
- The current repo already solves live Apple Music detection by polling `AppleMusic.exe`, finding either the Mini Player or `TransportBar`, reading song name / subtitle / progress / timestamps / pause state, and inferring playback state from UI controls.
- The current repo also enriches tracks from Apple Music Web for duration, artwork, song URL, and artist URL. That logic should be reused conceptually, but moved off the hot polling path so 1-second history capture stays cheap and reliable.
- The repo builds successfully in this environment with `.NET 10`, so no SDK change is required if implementation stays aligned with the existing stack.

## Product shape and scope
- Runtime model: tray app, starts at user logon, hidden by default, with a small settings/status window and tray actions for `Open`, `Pause Tracking`, `Export`, `Open Database Folder`, and `Exit`.
- History model: store track sessions plus state-change events, not raw per-second duplicate rows and not only scrobble-style completed plays.
- Past history: future-only. No backfill is planned unless you later provide an import source.
- Session rule: pause/resume of the same track stays in one session only if the gap is `<= 30 minutes`; a longer gap opens a new session.
- Replay rule: if the same track progresses from near-end back to near-start, or ends and restarts, close the old session and start a new one.

## Implementation architecture
- Create three projects: `AppleMusicHistory.App`, `AppleMusicHistory.Core`, and `AppleMusicHistory.Infrastructure`.
- `Core` will own the domain and state machine: `PlaybackSnapshot`, `TrackFingerprint`, `ListeningSession`, `SessionEvent`, `CaptureConfig`, and `PlaybackSessionCoordinator`.
- `Infrastructure` will own `FlaUI` scraping, SQLite access via `Microsoft.Data.Sqlite`, Apple Music Web enrichment, file logging, and startup registration.
- `App` will host the tray icon, settings UI, export commands, lifecycle hooks, and background services.

## Polling and sessionization
- Use a fast local poll loop: `1 second` while Apple Music is detected, `5 seconds` when the app is missing, and `2 seconds` while paused but still open.
- Each poll produces a `PlaybackSnapshot` with normalized track fields, pause state, current position, duration if known, and observation timestamp.
- New fingerprint: upsert track row, close prior active session with reason `track_changed`, start a new session, write `session_started`.
- Same fingerprint + advancing progress: update the active session row only, and write no duplicate event unless a `60 second` checkpoint is due.
- Same fingerprint + paused: update session state to paused and write one `paused` event.
- Same fingerprint + resume within `30 minutes`: continue the same session and write one `resumed` event.
- Same fingerprint + resume after `30 minutes`: close prior session with reason `gap_timeout`, start a new session.
- Same fingerprint + progress jumps from near-end to near-start: close prior session with reason `replayed`, start a new session.
- Apple Music disappears or the tracker exits: close any open session with the last observed position and reason `app_closed` or `tracker_stopped`.

## SQLite schema
- `app_runs`: one row per tracker launch, for crash recovery and diagnostics.
- `tracks`: stable fingerprint, raw title / artist / album / subtitle, normalized fields, duration, URLs, artwork, first seen, last seen, enrichment status.
- `listening_sessions`: session id, track id, app run id, started/ended UTC, first/last/max position, heard wallclock seconds, pause count, resume count, current state, replay index, end reason, last observed UTC.
- `session_events`: session id, event type, observed UTC, position seconds, optional JSON payload for debug details.
- SQLite versioning will use `PRAGMA user_version` plus SQL migration scripts committed in the repo.

## Public interfaces and types
- `IAppleMusicSnapshotSource`: `Task<PlaybackSnapshot?> GetCurrentAsync(CancellationToken ct)`.
- `ITrackMetadataEnricher`: `Task<TrackMetadata?> EnrichAsync(TrackFingerprint fingerprint, CancellationToken ct)`.
- `IHistoryRepository`: methods for `UpsertTrack`, `StartSession`, `UpdateSessionProgress`, `AppendEvent`, `CloseSession`, `RecoverOpenSessions`, `ExportSessions`.
- `PlaybackSessionCoordinator`: pure state machine service that takes snapshots and emits repository commands.
- `TrackerOptions`: database path, polling intervals, resume-gap threshold, checkpoint interval, startup behavior, enrichment enabled flag.

## Enrichment, export, and operations
- Metadata enrichment will be asynchronous and cached per track fingerprint; it will fetch duration, song URL, artist URL, and cover art from Apple Music Web, with retry limits similar to `AMWin-RP`.
- Lyrics, Discord RPC, Last.fm, and ListenBrainz are out of scope for v1.
- Export will support `CSV` for one-row-per-session reporting and `JSON` for full session + event detail.
- Logging will go to `%LocalAppData%\AppleMusicTracker\logs\yyyy-MM-dd.log`.

## Tests and acceptance criteria
- Unit tests must cover fingerprint normalization, pause detection fallback, gap splitting at `30 minutes`, replay detection, track change handling, and crash-recovery closure of open sessions.
- Repository tests must cover schema creation, migrations, track upsert idempotency, session updates without duplicate rows, and export correctness.
- Manual smoke tests must cover: Apple Music not running; short 1-3 second listen; pause/resume within 5 minutes; pause/resume after 31 minutes; seek within a track; replay after track end; quick skip to another song; app restart while a track is active.
- Acceptance: after a listening session, SQLite must show exactly one session row per continuous listen window, a bounded event trail, correct start/end times, and a new row when the same track is replayed later.

## What you need to set up for implementation
- Required runtime target: Windows 11 with the Microsoft Store Apple Music app installed and signed in.
- Important limitation: Apple Music and the tracker must be in the same signed-in desktop session; if you use virtual desktops, keep them on the same desktop.
- For local build/test here, the current environment already has a working `.NET 10` toolchain and the reference repo builds, so no extra SDK setup is needed if we keep that target.
- If you want real end-to-end validation on your machine, you need Apple Music installed, at least a few playable tracks, and permission for the tracker to write under `%LocalAppData%`.

## Assumptions and defaults
- App name: `AppleMusicTracker`.
- Default DB path: `%LocalAppData%\AppleMusicTracker\history.sqlite`.
- Default active poll interval: `1 second`.
- Default resume-gap split: `30 minutes`.
- Default checkpoint event interval: `60 seconds`.
- Default startup method: Startup shortcut for the current user at logon.
- No historical backfill, no cloud sync, and no non-Windows support in v1.

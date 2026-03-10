# Add Playback Quality / Format Capture to AppleMusicHistory

## Summary
`AppleMusicHistory` is currently split into a clean 4-layer shape: `Core` holds the contracts/models/coordinator, `Infrastructure` handles UI Automation + web enrichment + SQLite + export/settings, `App` is the WPF tray host/runtime, and `Tests` cover the repository/coordinator/fingerprint logic. The live playback path today is: Windows UI Automation -> `PlaybackSnapshot` -> `PlaybackSessionCoordinator` -> SQLite/export.

Feasibility is positive, but with an important distinction:
- Apple catalog metadata can expose supported audio variants for a song/album.
- I did not find a documented Apple “now playing” API for Windows that exposes the exact active stream quality/format.
- On this machine, the Apple Music Windows UI is exposing a live UIA element `AutomationId='AudioBadgeButton'` with `Name='Dolby Audio'`, so the practical way to capture the actual current playback format in this app is UI Automation against the desktop app.

## Important API / Type Changes
- Add a new enum in `Core`, `PlaybackAudioVariant`, with values:
  `Unknown`, `Lossless`, `HiResLossless`, `DolbyAudio`, `DolbyAtmos`, `Other`.
- Extend `PlaybackSnapshot` with:
  `string? ObservedAudioBadgeRaw`
  `PlaybackAudioVariant? ObservedAudioVariant`
- Extend track-level metadata models (`TrackMetadata`, `TrackUpsert`, `TrackRecord`) with:
  `string? CatalogAudioVariantsJson`
  This stores Apple catalog-supported variants, not live playback state.
- Extend session-level models (`ListeningSessionRecord`, `StartSessionRequest`, `SessionProgressUpdate`, `ExportSessionRow`) with:
  `string? LastObservedAudioBadgeRaw`
  `PlaybackAudioVariant? LastObservedAudioVariant`
- Extend `SessionEventType` with:
  `AudioVariantChanged = 6`
- Reuse `SessionEventRecord.PayloadJson` for audio-variant transition payloads instead of adding another event model.

## Implementation Plan
1. Update the UI Automation snapshot source to read the live audio badge/button from the playback controls.
   Default lookup target: `AutomationId == "AudioBadgeButton"`.
   Search both the main transport bar and mini-player path.
   Normalize the UI label case-insensitively:
   `Lossless` -> `Lossless`
   `Hi-Res Lossless` -> `HiResLossless`
   `Dolby Audio` -> `DolbyAudio`
   `Dolby Atmos` -> `DolbyAtmos`
   Anything else non-empty -> `Other`, preserving the raw label.

2. Persist live playback format as session data, not track data.
   On session start, write the initial observed badge/raw value into the session row.
   On subsequent same-track snapshots, compare the normalized/raw value to the active session state.
   If it changes, update the session row and append an `AudioVariantChanged` event with JSON payload:
   `previousRaw`, `previousVariant`, `currentRaw`, `currentVariant`, `observedAtUtc`.

3. Keep catalog capability metadata separate from live playback state.
   Add an optional Apple catalog enricher that can fetch `audioVariants` for the matched song/album and store them in `tracks.catalog_audio_variants_json`.
   Do not block capture on this enrichment.
   If no Apple developer token is configured, skip catalog audio enrichment entirely and continue using only live UI badge capture.

4. Introduce a composite enrichment strategy instead of overloading the existing HTML scraper.
   Keep the current web enricher for URL/artwork/duration.
   Add a second optional catalog enricher for `audioVariants`.
   Merge results before the repository upsert so one track write can contain both current web metadata and catalog variants.

5. Add a SQLite schema migration `002_audio_variants.sql`.
   Add `catalog_audio_variants_json` to `tracks`.
   Add `last_observed_audio_badge_raw` and `last_observed_audio_variant` to `listening_sessions`.
   Keep `session_events` unchanged; use `payload_json` for change payloads.
   Bump schema version and update repository mapping, export queries, JSON export, and CSV headers.

6. Surface the data in exports and status.
   CSV: include session-level live fields plus track-level catalog variants JSON.
   JSON export: include the new session fields and `AudioVariantChanged` event payloads.
   WPF status window: show the currently observed live badge if present; if absent, show `Standard / unknown`.
   Do not display catalog variants as the current playback format.

## Tests And Scenarios
- Unit test badge normalization:
  `Lossless`, `Hi-Res Lossless`, `Dolby Audio`, `Dolby Atmos`, unexpected text, and missing badge.
- Coordinator test:
  same track, same badge -> no new event.
- Coordinator test:
  same track, badge changes mid-session -> session updated and `AudioVariantChanged` event appended.
- Repository migration test:
  v2 schema initializes cleanly and export includes the new columns.
- Repository persistence test:
  session row round-trips `last_observed_audio_variant` and raw badge.
- Export test:
  CSV and JSON outputs include live session format plus catalog variants.
- Non-regression:
  existing pause/replay/crash-recovery tests still pass.
- Manual validation:
  play a track currently showing `Dolby Audio` in Apple Music and confirm the session/export records `DolbyAudio`.
- Manual validation:
  play a track with no visible badge and confirm capture remains stable with null/unknown fields.

## Assumptions And Defaults
- Chosen strategy: Hybrid.
  Live playback format comes from the Windows app UI badge.
  Catalog-supported variants come from Apple metadata only as enrichment.
- Chosen granularity: Session + events.
  Mid-session quality changes are preserved as events.
- Default security/ops choice: catalog enrichment is optional and disabled unless an Apple developer token is configured.
- Scope limit: this plan captures Apple’s exposed category labels, not a guaranteed exact codec/bit-depth/sample-rate readout of the active stream.
- Inference from Apple docs: `audioVariants` is the supported catalog capability surface; it is not evidence of the exact stream currently selected on Windows.

## Sources
- Apple Support: [Play lossless audio in Apple Music on Windows](https://support.apple.com/en-ie/guide/music-windows/mus90b573cbb/windows)
- Apple Support: [Play Dolby Atmos in Music on Windows](https://support.apple.com/en-al/guide/music-windows/musf7d98bef8/windows)
- Apple Support: [Change Playback settings in Apple Music on Windows](https://support.apple.com/en-afri/guide/music-windows/musdf855a1b/windows)
- Apple Developer: [MusicKit overview](https://developer.apple.com/musickit/)
- Apple Developer: [Song.audioVariants](https://developer.apple.com/documentation/musickit/song/audiovariants)
- Apple Developer: [Album.audioVariants](https://developer.apple.com/documentation/musickit/album)

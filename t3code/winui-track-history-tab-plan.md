# WinUI Track History Tab Plan

## Summary
Add a second WinUI tab, `Track History`, alongside the existing now-playing dashboard, while keeping the current glassmorph shell, ambient glow orbs, and blurred currently-playing artwork background untouched. The new tab will render a custom Apple Music-style list, backed by a dedicated repository query for raw track rows, with per-column text filters, hover-only row shortcuts, and no database migration.

## Public API and model changes
- Add `TrackHistoryRow` in [TrackHistoryRow.cs](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.Core/Models/TrackHistoryRow.cs).
- Add `Task<IReadOnlyList<TrackHistoryRow>> GetTrackHistoryAsync(CancellationToken cancellationToken);` to [IHistoryRepository.cs](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.Core/Abstractions/IHistoryRepository.cs).
- Extend [TrackerApplicationHost.cs](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.Host/TrackerApplicationHost.cs) with `GetTrackHistoryAsync(CancellationToken cancellationToken)` so WinUI stays host-driven instead of reaching into infrastructure directly.
- `TrackHistoryRow` should contain the displayed raw fields plus action/rendering fields needed by WinUI: `track_id`, `title`, `artist`, `album`, `subtitle`, `catalog_audio_variants_json`, `last_observed_audio_badge_raw`, `last_observed_audio_variant`, `song_url`, `album_url`, `artist_url`, `artwork_url`, `artwork_cache_relative_path`, `last_seen_utc`.

## Data and host implementation
- Implement `GetTrackHistoryAsync` in [SqliteHistoryRepository.cs](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.Infrastructure/Data/SqliteHistoryRepository.cs) as a single `tracks LEFT JOIN track_metadata` query ordered by `t.last_seen_utc DESC, t.track_id DESC`.
- Return raw values only for the visible data columns. Do not normalize, pretty-print, or transform `catalog_audio_variants_json`, `last_observed_audio_badge_raw`, or `last_observed_audio_variant`.
- Resolve row actions from metadata first: `album_url = tm.apple_music_album_url`, `artist_url = tm.apple_music_artist_url ?? t.artist_url`, `song_url = tm.apple_music_song_url ?? t.song_url`.
- No schema change and no migration. All required fields already exist in `tracks` and `track_metadata`.

## WinUI structure
- Refactor [MainWindowViewModel.cs](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.WinUI/ViewModels/MainWindowViewModel.cs) into a shell view model plus child tab view models.
- Introduce `MainTabControllerViewModel` with two tabs: `Now Playing` and `Track History`. Default selected tab remains `Now Playing`.
- Introduce `NowPlayingTabViewModel` to absorb the current dashboard-specific state/commands already living in `MainWindowViewModel`.
- Introduce `TrackHistoryTabViewModel` to own loading, filters, filtered rows, refresh state, and row commands.
- Keep shell-owned background/art palette behavior exactly as it works now: tab changes must not swap away from the currently playing artwork/glow state.

## WinUI UI design
- Replace the single-content body in [MainWindow.xaml](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.WinUI/MainWindow.xaml) with a glass segmented tab controller directly under the title bar.
- Keep the existing now-playing tab layout visually unchanged except for being hosted inside tab content.
- Build `Track History` as a custom list view, not a new DataGrid dependency. Use a frosted card with:
  - sticky header row,
  - filter row directly under headers,
  - virtualized `ListView` rows beneath.
- Use these visible columns in this order: `track_id`, `title`, `artist`, `album`, `subtitle`, `catalog_audio_variants_json`, `last_observed_audio_badge_raw`, `last_observed_audio_variant`.
- Render the `title` cell in Apple Music detail-list style: artwork thumbnail, title text, and a compact audio badge/logo cluster when available.
- Render `last_observed_audio_variant` as text plus mapped logo when the variant is `Lossless`, `HiResLossless`, `DolbyAudio`, or `DolbyAtmos`, reusing the existing badge assets already copied by [AppleMusicHistory.WinUI.csproj](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.WinUI/AppleMusicHistory.WinUI.csproj).
- Row hover behavior: fade in a floating shortcut tray anchored over the `track_id` area and visually adjacent to the song title. Buttons are `show album`, `queue it next`, `play now`, `show artist`.
- Shortcut behavior for v1:
  - `show album`: active, opens album URL when present.
  - `show artist`: active, opens artist URL when present.
  - `queue it next`: visible but disabled/inactive with a tooltip like `Planned`.
  - `play now`: visible but disabled/inactive with a tooltip like `Planned`.
- If a URL is missing, keep the icon visible on hover but disabled.
- Keep responsive behavior similar to the existing window: the history list stays single-column and horizontally scrollable on narrow widths rather than collapsing fields away.

## Filtering and refresh behavior
- Add one `TextBox` filter per visible column. Placeholder text should match the column name.
- Filtering is client-side inside `TrackHistoryTabViewModel`, case-insensitive, substring-based, and combined with AND semantics across all columns.
- `track_id` filtering compares against the decimal string form of the numeric id.
- Null database values are treated as empty strings for filtering.
- Debounce filter application by about 200 ms so typing stays smooth.
- Add `Clear Filters` and `Refresh` actions in the history tab header.
- Load the history rows lazily the first time the `Track History` tab is selected.
- After first load, mark the tab dirty whenever `DashboardStateChanged` fires; if the history tab is active, debounce and reload, otherwise refresh on next activation.

## Code files to add or change
- Change [IHistoryRepository.cs](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.Core/Abstractions/IHistoryRepository.cs)
- Add [TrackHistoryRow.cs](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.Core/Models/TrackHistoryRow.cs)
- Change [SqliteHistoryRepository.cs](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.Infrastructure/Data/SqliteHistoryRepository.cs)
- Change [TrackerApplicationHost.cs](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.Host/TrackerApplicationHost.cs)
- Change [MainWindow.xaml](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.WinUI/MainWindow.xaml)
- Change [MainWindow.xaml.cs](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.WinUI/MainWindow.xaml.cs)
- Change [MainWindowViewModel.cs](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.WinUI/ViewModels/MainWindowViewModel.cs)
- Add `MainTabControllerViewModel`, `NowPlayingTabViewModel`, `TrackHistoryTabViewModel`, and `TrackHistoryRowViewModel` under [ViewModels](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.WinUI/ViewModels)
- Add small WinUI helpers/converters only if needed for hover/action visibility and badge asset resolution
- Change [SqliteHistoryRepositoryTests.cs](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.Tests/SqliteHistoryRepositoryTests.cs)
- Change [TestDoubles.cs](/S:/Dev/AppleMusicTracker/AppleMusicHistory/AppleMusicHistory.Tests/TestDoubles.cs)
- Add WinUI view model tests if kept in the existing test project; otherwise cover filter logic as plain unit tests without UI dependencies

## Test cases and acceptance criteria
- Repository test: `GetTrackHistoryAsync` returns raw track fields, metadata URLs, and artwork fields in `last_seen_utc DESC` order.
- Repository test: rows with no metadata still return track data and null-safe action targets.
- Host test: `TrackerApplicationHost.GetTrackHistoryAsync` delegates correctly after initialization.
- View model test: each column filter matches case-insensitively and all active filters combine with AND.
- View model test: `track_id` filter works from text input.
- View model test: audio badge/logo mapping matches the existing current-track mapping.
- Manual WinUI check: tab switch preserves the current glass background and ambient motion.
- Manual WinUI check: hover tray appears only on the hovered row and disabled icons are visibly inactive.
- Manual WinUI check: history tab works at wide and narrow window sizes with horizontal scrolling instead of clipping.
- Verification command target after implementation: build/test the WinUI path specifically and close the running `AppleMusicHistory.App` first, because current solution-wide build is blocked by file locks from the legacy WPF shell.

## Assumptions and defaults
- `Track History` is a track catalog/history list sourced from `tracks`, not a session/event timeline.
- The eight requested columns are the only visible data columns in v1.
- The current blurred artwork background always follows the live now-playing track, not the selected history row.
- No new third-party grid package will be introduced; the list is custom-styled to stay consistent with the current WinUI design.
- `queue it next` and `play now` are intentionally non-functional placeholders in v1, per the chosen scope.

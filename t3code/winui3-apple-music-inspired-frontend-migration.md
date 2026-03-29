# WinUI3 Apple Music-Inspired Frontend Migration

## Summary
- Migrate the current single WPF status window and all tray actions from `AppleMusicHistory.App` into `AppleMusicHistory.WinUI`, making WinUI the primary desktop shell.
- Keep `AppleMusicHistory.App` in the solution as a deprecated fallback/testing shell for a short transition period, but move shared runtime/host logic out of it so both shells use the same backend behavior.
- Preserve the current backend behavior exactly: background tracking starts hidden, keeps running from the tray, closing the window hides it instead of exiting, and the tray menu remains available for export and control actions.
- Match the visual direction to your references: Apple Music-inspired, Windows 11 materials, large artwork hero, color-driven background, glass surfaces, and slow animated blurred artwork in the backdrop.
- Use the right-side area as a tracker dashboard rather than lyrics, and use a player-inspired footer command bar whose actions control the tracker rather than Apple Music playback.
- Standardize the repo on `.NET 8` and `win-x64` for the new primary desktop app. The current WinUI project already builds successfully as `x64`; its current failure is only the default `AnyCPU` packaging path.

## Chosen Defaults
- `AppleMusicHistory.WinUI` becomes the main app users launch.
- `AppleMusicHistory.App` stays temporarily for testing/comparison only and is marked deprecated.
- Startup behavior is `Start Hidden To Tray`.
- Distribution is `Unpackaged Desktop First`.
- Framework alignment is `Standardize On .NET 8`.
- The large secondary panel shows tracker information, not lyrics.
- The footer uses tracker commands, not real media transport control.
- The theme should follow the provided references: warm/teal color fields, acrylic/mica feel, oversized blurred album art motion in the background, and a premium now-playing layout.

## Public APIs, Interfaces, and Project Changes
- Add a new shared desktop host project named `AppleMusicHistory.Host` targeting `net8.0-windows10.0.19041.0`.
- Move `TrackerRuntime` and `RuntimeStatus` out of `AppleMusicHistory.App` into `AppleMusicHistory.Host`.
- Add a new host-facing orchestrator class `TrackerApplicationHost` in `AppleMusicHistory.Host` that owns startup, runtime lifecycle, status updates, export actions, startup registration, and database-folder opening.
- Add a new UI-neutral state model `DashboardState` in `AppleMusicHistory.Host` that contains all currently displayed values plus progress/timeline fields needed by the WinUI layout.
- Add a new enum `ExportKind` in `AppleMusicHistory.Host` with `SessionsCsv`, `SessionsJson`, `TracksCsv`, and `TracksJson`.
- Expose `TrackerApplicationHost` methods/events as:
  - `Task InitializeAsync()`
  - `Task SetTrackingPausedAsync(bool paused)`
  - `Task UpdateLaunchAtStartupAsync(bool enabled)`
  - `Task ExportAsync(ExportKind kind)`
  - `void OpenDatabaseFolder()`
  - `event Action<DashboardState> DashboardStateChanged`
- Retarget `AppleMusicHistory.Core`, `AppleMusicHistory.Infrastructure`, `AppleMusicHistory.App`, and `AppleMusicHistory.Tests` to `.NET 8`.
- Update tests to reference `AppleMusicHistory.Host` instead of depending on the WPF shell for runtime types.
- Update `AppleMusicHistory.WinUI` to reference `Core`, `Infrastructure`, and `Host`.
- Update `AppleMusicHistory.slnx` to include `AppleMusicHistory.WinUI` and `AppleMusicHistory.Host`.

## Implementation Plan
### 1. Shared host extraction
- Move all non-WPF orchestration out of `AppleMusicHistory.App\App.xaml.cs` into `TrackerApplicationHost`.
- Keep repository initialization, crash recovery, runtime startup, metadata enricher creation, exporter wiring, startup shortcut management, and database-folder launch in `TrackerApplicationHost`.
- Move the formatting logic from `App.OnRuntimeStatusChanged` into a host-side mapper that converts `RuntimeStatus` into `DashboardState`.
- Keep the backend data contract identical to today so no behavior changes land in sessionization, exports, or storage.

### 2. WinUI shell foundation
- Keep `AppleMusicHistory.WinUI` as a desktop WinUI 3 app targeting `win-x64`.
- Set the project up for unpackaged-first desktop execution and remove reliance on the default neutral/`AnyCPU` packaged build path.
- Enable `UseWindowsForms` in the WinUI project so it can reuse `System.Windows.Forms.NotifyIcon` for tray support.
- Use `MainWindow` as the only WinUI view in v1, since the existing WPF app has only one real view to migrate.
- Create a WinUI `MainWindowViewModel` that subscribes to `TrackerApplicationHost.DashboardStateChanged` and exposes bindable properties/commands for the page.

### 3. Tray/taskbar/background behavior
- Create the tray icon during app launch before any visible window is shown.
- Start the backend host immediately on launch and keep the main window hidden by default.
- Show the dashboard only when the user clicks the tray icon, double-clicks it, or selects `Open Dashboard` from the tray menu.
- Intercept window close and minimize so they hide the window to tray instead of shutting down the process.
- Keep the tray menu actions functionally equivalent to today:
  - `Open Dashboard`
  - `Pause Tracking` / `Resume Tracking`
  - `Export Sessions CSV`
  - `Export Sessions JSON`
  - `Export Tracks CSV`
  - `Export Tracks JSON`
  - `Open Database Folder`
  - `Exit`
- Only the tray `Exit` action performs full app shutdown and host disposal.
- Reuse the existing startup shortcut registration path so launch-at-login behavior continues to work the same way.

### 4. Window and visual design
- Set the default window size to approximately `1400x860` with a minimum around `1180x720`.
- Use Mica as the window backdrop and a custom title bar with transparent caption buttons styled to blend into the artwork-driven surface.
- Build the window in three visual layers:
  - An animated color-and-art background layer.
  - A translucent content surface layer with glass cards.
  - A lightweight top chrome/title layer.
- Drive the main background gradient from the current artwork when available, with fallback palettes inspired by your examples: amber-brown for warm artwork and blue-teal for cool artwork.
- Add two oversized background artwork visuals behind the content, rendered with low opacity, gaussian blur, and slow drift/rotation animation so the window always feels alive.
- Use the Windows composition layer for the background motion so it remains smooth and independent from the dashboard content.

### 5. Layout structure
- Split the content into a left hero pane and a right dashboard pane.
- The left hero pane should contain:
  - A large artwork card.
  - Current title, artist, and album.
  - The observed audio badge/format.
  - A live progress bar driven by `CurrentPositionSeconds` and `DurationSeconds`.
  - Current elapsed and remaining time labels.
  - A player-inspired footer command bar for tracker actions.
- The right dashboard pane should contain four glass cards:
  - `Status` card with Apple Music state, active session, last observed time, and tracker paused/running status.
  - `Metadata` card with composer, release date, genres, track/disc numbers, ISRC, and song/album/artist links.
  - `Statistics` card with track count, session count, open-session count, and database path.
  - `Tools & Settings` card with exports, open-folder action, launch-at-startup toggle, and metadata enrichment indicator.
- Keep all fields currently shown in the WPF window present somewhere in the WinUI layout so migration is feature-complete.

### 6. Command surface design
- Style the bottom action area like a media controller, but use tracker-specific commands and tooltips instead of fake playback transport.
- Make `Pause/Resume Tracking` the primary center action.
- Group all export commands into a polished split-button or flyout so session and track exports stay easy to access without clutter.
- Keep `Open Database Folder` as a visible action in both the right-side tools card and the tray menu.
- Surface the current Apple Music URLs as clickable `HyperlinkButton` actions in the metadata card rather than dumping raw URLs as long text lines.

### 7. WPF fallback shell
- Keep `AppleMusicHistory.App` buildable during transition.
- Rewire it to consume `AppleMusicHistory.Host` instead of owning the runtime directly.
- Mark the WPF shell as deprecated in project naming/comments/docs and exclude it from primary user-facing instructions.
- Do not add new UI work to WPF beyond whatever is required to keep it compiling against the extracted host.

## Data Mapping Rules
- Use the current `PlaybackSnapshot` position and duration values to power the hero progress bar and the elapsed/remaining labels.
- Prefer enriched metadata for artwork, album, composer, release date, URLs, and genres, with snapshot fallbacks exactly as the WPF app does today.
- If artwork is missing, use a generated fallback gradient background and a placeholder artwork frame rather than leaving the hero area empty.
- If Apple Music is not running, keep the dashboard alive and show the idle state rather than collapsing the layout.
- If no track is detected, keep the last-known palette softened in the background but switch the main content to an idle/empty-state presentation.

## Test Cases and Scenarios
- Build `AppleMusicHistory.WinUI` successfully with `x64`.
- Build the deprecated WPF app successfully against the extracted host.
- Run all existing repository/coordinator tests after retargeting to `.NET 8`.
- Add unit tests for `DashboardState` mapping from `RuntimeStatus`.
- Add unit tests for timeline formatting with known position/duration combinations.
- Add unit tests for status-text mapping for `AppNotRunning`, `NoTrackDetected`, `Recovering`, `Paused`, and `Playing`.
- Add host tests for launch-at-startup updates and export dispatch routing by `ExportKind`.
- Manual smoke test: app launches hidden to tray and backend starts without showing a window.
- Manual smoke test: tray `Open Dashboard` shows the WinUI window and repeated open/hide cycles do not restart the backend.
- Manual smoke test: clicking window close hides to tray and does not stop capture.
- Manual smoke test: tray `Exit` shuts down cleanly and disposes the tray icon.
- Manual smoke test: all four export actions still produce the same files as the current WPF shell.
- Manual smoke test: launch-at-startup toggle still creates/removes the startup shortcut.
- Manual smoke test: playing a track updates artwork, colors, audio format, title, position, and right-pane metadata live.
- Manual smoke test: Apple Music not running still shows a polished idle state with tray menu working.

## Acceptance Criteria
- The WinUI app fully replaces the current WPF window for normal usage while preserving tray/background behavior.
- All fields and actions from the current WPF status window are present in the WinUI app.
- The WinUI design visually matches the provided Apple Music-inspired references in structure and mood, not just raw functionality.
- The app starts hidden to tray, continues tracking while hidden, and only exits from the explicit tray exit command.
- The deprecated WPF app remains available only as a temporary validation shell.
- The new primary app builds and runs as `win-x64` without depending on the default `AnyCPU` packaged configuration.

## Assumptions and Defaults
- No real lyrics support is added in this migration.
- No real Apple Music transport control is added in this migration.
- The existing database path, settings path, export formats, and startup shortcut location remain unchanged.
- Packaging polish for MSIX is deferred until after the unpackaged WinUI migration is stable.
- The background artwork blur/animation is part of scope and should be implemented with composition-based visuals rather than a static image backdrop.

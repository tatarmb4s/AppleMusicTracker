CREATE TABLE IF NOT EXISTS app_runs (
    app_run_id INTEGER PRIMARY KEY AUTOINCREMENT,
    started_at_utc TEXT NOT NULL,
    app_version TEXT NOT NULL,
    machine_name TEXT NOT NULL,
    user_name TEXT NOT NULL,
    runtime TEXT NOT NULL,
    os_version TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS tracks (
    track_id INTEGER PRIMARY KEY AUTOINCREMENT,
    fingerprint TEXT NOT NULL UNIQUE,
    title TEXT NOT NULL,
    artist TEXT NOT NULL,
    album TEXT NOT NULL,
    subtitle TEXT NOT NULL,
    normalized_title TEXT NOT NULL,
    normalized_artist TEXT NOT NULL,
    normalized_album TEXT NOT NULL,
    duration_seconds INTEGER NULL,
    song_url TEXT NULL,
    artist_url TEXT NULL,
    artwork_url TEXT NULL,
    first_seen_utc TEXT NOT NULL,
    last_seen_utc TEXT NOT NULL,
    enriched_at_utc TEXT NULL
);

CREATE TABLE IF NOT EXISTS listening_sessions (
    session_id INTEGER PRIMARY KEY AUTOINCREMENT,
    track_id INTEGER NOT NULL,
    app_run_id INTEGER NOT NULL,
    started_at_utc TEXT NOT NULL,
    ended_at_utc TEXT NULL,
    first_position_seconds INTEGER NOT NULL,
    last_position_seconds INTEGER NOT NULL,
    max_position_seconds INTEGER NOT NULL,
    heard_seconds REAL NOT NULL DEFAULT 0,
    pause_count INTEGER NOT NULL DEFAULT 0,
    resume_count INTEGER NOT NULL DEFAULT 0,
    replay_index INTEGER NOT NULL DEFAULT 0,
    state INTEGER NOT NULL,
    end_reason INTEGER NULL,
    last_observed_utc TEXT NOT NULL,
    FOREIGN KEY(track_id) REFERENCES tracks(track_id),
    FOREIGN KEY(app_run_id) REFERENCES app_runs(app_run_id)
);

CREATE TABLE IF NOT EXISTS session_events (
    session_event_id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id INTEGER NOT NULL,
    event_type INTEGER NOT NULL,
    observed_at_utc TEXT NOT NULL,
    position_seconds INTEGER NOT NULL,
    payload_json TEXT NULL,
    FOREIGN KEY(session_id) REFERENCES listening_sessions(session_id)
);

CREATE INDEX IF NOT EXISTS ix_tracks_fingerprint ON tracks(fingerprint);
CREATE INDEX IF NOT EXISTS ix_sessions_track_id ON listening_sessions(track_id);
CREATE INDEX IF NOT EXISTS ix_sessions_state ON listening_sessions(state);
CREATE INDEX IF NOT EXISTS ix_events_session_id ON session_events(session_id);

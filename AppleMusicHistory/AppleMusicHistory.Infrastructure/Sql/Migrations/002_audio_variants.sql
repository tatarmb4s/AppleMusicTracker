ALTER TABLE tracks ADD COLUMN catalog_audio_variants_json TEXT NULL;

ALTER TABLE listening_sessions ADD COLUMN last_observed_audio_badge_raw TEXT NULL;
ALTER TABLE listening_sessions ADD COLUMN last_observed_audio_variant INTEGER NULL;

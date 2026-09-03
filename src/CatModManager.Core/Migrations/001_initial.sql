-- The schema as AppDatabase.Initialize() created it, word for word.
--
-- Existing installations already have every one of these tables and no Migrations ledger, so this
-- script will run against them. It passes because it is entirely CREATE TABLE IF NOT EXISTS — this
-- is the one migration allowed that luxury, precisely because it is the one that has to be a no-op
-- on databases that predate migrations. Every script from 002 on must assume it runs exactly once.

CREATE TABLE IF NOT EXISTS app_config (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS active_mounts (
    original_path TEXT PRIMARY KEY,
    backup_path   TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS installed_plugins (
    package_id   TEXT PRIMARY KEY,
    version      TEXT NOT NULL,
    installed_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS root_swap_entries (
    id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    game_folder           TEXT NOT NULL,
    source_path           TEXT NOT NULL,
    dest_path             TEXT NOT NULL,
    original_backup_path  TEXT
);

CREATE TABLE IF NOT EXISTS hardlink_entries (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    mount_point TEXT NOT NULL,
    rel_path    TEXT NOT NULL,
    dest_path   TEXT NOT NULL,
    backup_path TEXT
);

CREATE TABLE IF NOT EXISTS plugin_settings (
    plugin_id TEXT NOT NULL,
    key       TEXT NOT NULL,
    value     TEXT NOT NULL,
    PRIMARY KEY (plugin_id, key)
);

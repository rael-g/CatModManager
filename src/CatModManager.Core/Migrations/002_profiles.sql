-- Profiles move out of <data>/profiles/*.toml and into cmm.db.
--
-- The TOML files are not touched by this script, and not by the importer either. They stay where
-- they are as a backup until a later version removes them, because deleting the only copy of a
-- user's profiles in the same release that debuts the code replacing them is how profiles get lost.

CREATE TABLE profiles (
    name                  TEXT PRIMARY KEY,
    mods_folder_path      TEXT NOT NULL DEFAULT '',
    downloads_folder_path TEXT NOT NULL DEFAULT '',
    base_data_path        TEXT NOT NULL DEFAULT '',
    game_executable_path  TEXT NOT NULL DEFAULT '',
    game_support_id       TEXT NOT NULL DEFAULT 'generic',
    launch_arguments      TEXT NOT NULL DEFAULT ''
);

-- position, not priority, is what identifies a row.
--
-- The plan called for (profile_name, priority) as the key, on the grounds that priority is the order
-- of the list and unique within a profile. Nothing enforces that: Priority is a plain settable
-- property, and a profile that was never renumbered can hold ties. Keyed on priority, a tie would
-- not be a bug report — it would be a mod silently missing after a save.
--
-- position is the index in the list, so it is unique by construction, and priority rides along as
-- the value it actually is.
CREATE TABLE profile_mods (
    profile_name   TEXT    NOT NULL REFERENCES profiles(name) ON DELETE CASCADE,
    position       INTEGER NOT NULL,
    priority       INTEGER NOT NULL DEFAULT 0,
    name           TEXT    NOT NULL DEFAULT '',
    mod_root_path  TEXT    NOT NULL DEFAULT '',
    is_enabled     INTEGER NOT NULL DEFAULT 1,
    is_archive     INTEGER NOT NULL DEFAULT 0,
    is_separator   INTEGER NOT NULL DEFAULT 0,
    category       TEXT    NOT NULL DEFAULT 'Uncategorized',
    version        TEXT    NOT NULL DEFAULT '1.0.0',
    mount_point_id TEXT,
    PRIMARY KEY (profile_name, position)
);

CREATE TABLE profile_tools (
    profile_name        TEXT    NOT NULL REFERENCES profiles(name) ON DELETE CASCADE,
    position            INTEGER NOT NULL,
    name                TEXT    NOT NULL DEFAULT '',
    executable_path     TEXT    NOT NULL DEFAULT '',
    arguments           TEXT    NOT NULL DEFAULT '',
    mount_before_launch INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (profile_name, position)
);

-- Only the user's own mount points. The game-defined ones come from the game definition on every
-- load and are merged in at runtime, so storing them here would be a stale second copy.
CREATE TABLE profile_mount_points (
    profile_name TEXT NOT NULL REFERENCES profiles(name) ON DELETE CASCADE,
    id           TEXT NOT NULL,
    name         TEXT NOT NULL DEFAULT '',
    path         TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (profile_name, id)
);

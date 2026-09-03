-- Game becomes an entity, and the mod inventory moves onto it.
--
-- Until now two profiles of the same game worked by coincidence: each one derived the same paths and
-- carried its own private copy of the mod list, so switching profiles looked like everything had to
-- be downloaded and installed again. Nothing was shared because there was nothing to share it on.
--
-- Everything the new shape needs is already recorded, so this is a regrouping and not a guess:
-- profiles that agree on base_data_path are the same game.

CREATE TABLE games (
    id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    base_data_path        TEXT NOT NULL DEFAULT '',
    mods_folder_path      TEXT NOT NULL DEFAULT '',
    downloads_folder_path TEXT NOT NULL DEFAULT '',
    game_executable_path  TEXT NOT NULL DEFAULT '',
    game_support_id       TEXT NOT NULL DEFAULT 'generic'
);

-- Partial, so that base_data_path identifies a game only once it says something.
--
-- A plain UNIQUE column would make the empty string an identity of its own, and every
-- half-configured game in the database would collide with every other one. The folder is how two
-- profiles recognise they are the same installation, and "not set yet" is not a folder.
CREATE UNIQUE INDEX games_by_folder ON games (base_data_path) WHERE base_data_path <> '';

-- The inventory: what is installed for this game, once, regardless of which profiles use it.
-- Keyed on the path because that is what identifies an installed mod on disk — two rows for one
-- folder would be two views of the same files, and removing a mod deletes those files.
CREATE TABLE game_mods (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    game_id       INTEGER NOT NULL REFERENCES games(id) ON DELETE CASCADE,
    mod_root_path TEXT    NOT NULL,
    name          TEXT    NOT NULL DEFAULT '',
    category      TEXT    NOT NULL DEFAULT 'Uncategorized',
    version       TEXT    NOT NULL DEFAULT '1.0.0',
    is_archive    INTEGER NOT NULL DEFAULT 0,
    UNIQUE (game_id, mod_root_path)
);

-- What the profile actually owns: the order, what is ticked, and where each one goes.
--
-- game_mod_id is nullable because separators live here too. A separator is a label the user dropped
-- into their list to organise it — it has no files and belongs to no game, so it cannot be inventory,
-- but it does have a position among the mods.
CREATE TABLE profile_entries (
    profile_name   TEXT    NOT NULL REFERENCES profiles(name) ON DELETE CASCADE,
    position       INTEGER NOT NULL,
    game_mod_id    INTEGER REFERENCES game_mods(id) ON DELETE CASCADE,
    separator_name TEXT,
    is_enabled     INTEGER NOT NULL DEFAULT 1,
    priority       INTEGER NOT NULL DEFAULT 0,
    mount_point_id TEXT,
    PRIMARY KEY (profile_name, position)
);

-- NULL means parked: a profile that never had a game folder set. Modded and NewProfile on the
-- developer's own machine are exactly this. They are kept and shown, never discarded — a row the
-- migration cannot classify is the user's data, not noise.
ALTER TABLE profiles ADD COLUMN game_id INTEGER REFERENCES games(id);

INSERT INTO games (base_data_path, mods_folder_path, downloads_folder_path,
                   game_executable_path, game_support_id)
SELECT base_data_path,
       -- Bare columns under GROUP BY: SQLite picks one row's values, and any row of the group will
       -- do because these profiles already agreed on the game folder. Where they disagree on a
       -- derived path, one of them has to win, and there is no better rule available than "one of
       -- the ones the user actually had".
       mods_folder_path, downloads_folder_path, game_executable_path, game_support_id
FROM profiles
WHERE base_data_path <> ''
GROUP BY base_data_path;

UPDATE profiles
SET game_id = (SELECT id FROM games WHERE games.base_data_path = profiles.base_data_path)
WHERE base_data_path <> '';

-- Separators carry no path, so they must not become inventory.
INSERT INTO game_mods (game_id, mod_root_path, name, category, version, is_archive)
SELECT p.game_id, m.mod_root_path, m.name, m.category, m.version, m.is_archive
FROM profile_mods m
JOIN profiles p ON p.name = m.profile_name
WHERE p.game_id IS NOT NULL AND m.is_separator = 0 AND m.mod_root_path <> ''
GROUP BY p.game_id, m.mod_root_path;

INSERT INTO profile_entries (profile_name, position, game_mod_id, separator_name,
                             is_enabled, priority, mount_point_id)
SELECT m.profile_name,
       m.position,
       g.id,
       CASE WHEN m.is_separator = 1 THEN m.name ELSE NULL END,
       m.is_enabled,
       m.priority,
       m.mount_point_id
FROM profile_mods m
JOIN profiles p ON p.name = m.profile_name
LEFT JOIN game_mods g ON g.game_id = p.game_id AND g.mod_root_path = m.mod_root_path
WHERE m.is_separator = 1 OR g.id IS NOT NULL;

DROP TABLE profile_mods;

-- The five game columns stay on profiles, and stay unread.
--
-- Dropping them is a second migration, once the code has run long enough that nobody wants to look
-- at what a profile used to say. They cost a few hundred bytes and they are the only copy of the
-- pre-split state that does not require restoring a backup file to read.

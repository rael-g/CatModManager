-- The game becomes something the user picks, and a profile name stops having to be unique across
-- the whole application.
--
-- 003 made the game an entity but left it anonymous, and left the profile keyed on its name. Both
-- get in the way of the game-first flow: a menu needs something to show, and two games both want a
-- profile called "Default".

-- ── A game the user can recognise ─────────────────────────────────────────────
--
-- Filled in from the last segment of the game folder, which is what the folder is usually named
-- after — or of the executable, for a game half configured, where "Skyrim.exe" still says more
-- than the game mode does. It is a starting point, not an identity: the user can rename it, and
-- nothing keys off it.

ALTER TABLE games ADD COLUMN display_name TEXT NOT NULL DEFAULT '';

UPDATE games
   SET display_name = rtrim(replace(
           CASE WHEN base_data_path <> '' THEN base_data_path ELSE game_executable_path END,
           '\', '/'), '/');

-- Last path segment, the way SQLite makes you ask for it: rtrim with the path's own characters
-- minus the separators strips the final segment, and removing that prefix leaves it by itself.
UPDATE games
   SET display_name = replace(display_name,
                              rtrim(display_name, replace(display_name, '/', '')),
                              '')
 WHERE display_name <> '';

UPDATE games SET display_name = game_support_id WHERE display_name = '';

-- ── Profiles keyed on a row, not on their name ────────────────────────────────
--
-- The whole 12-step rebuild, because the name is the primary key and three tables carry it as a
-- foreign key. What it buys: a name unique per game instead of per database, and a rename that is
-- one UPDATE rather than four statements needing deferred constraints to stay consistent halfway
-- through.
--
-- The five game columns 003 froze on profiles are not carried over. They were the pre-split
-- snapshot, they have been unread since, and the .bak AppDatabase takes before this script runs is
-- the copy worth keeping.

CREATE TABLE profiles_new (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    game_id          INTEGER REFERENCES games(id) ON DELETE CASCADE,
    name             TEXT    NOT NULL,
    launch_arguments TEXT    NOT NULL DEFAULT ''
);

INSERT INTO profiles_new (game_id, name, launch_arguments)
SELECT game_id, name, launch_arguments FROM profiles ORDER BY name;

CREATE TABLE profile_entries_new (
    profile_id     INTEGER NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
    position       INTEGER NOT NULL,
    game_mod_id    INTEGER REFERENCES game_mods(id) ON DELETE CASCADE,
    separator_name TEXT,
    is_enabled     INTEGER NOT NULL DEFAULT 1,
    priority       INTEGER NOT NULL DEFAULT 0,
    mount_point_id TEXT,
    PRIMARY KEY (profile_id, position)
);

INSERT INTO profile_entries_new (profile_id, position, game_mod_id, separator_name, is_enabled,
                                 priority, mount_point_id)
SELECT p.id, e.position, e.game_mod_id, e.separator_name, e.is_enabled, e.priority, e.mount_point_id
FROM profile_entries e JOIN profiles_new p ON p.name = e.profile_name;

CREATE TABLE profile_tools_new (
    profile_id          INTEGER NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
    position            INTEGER NOT NULL,
    name                TEXT    NOT NULL DEFAULT '',
    executable_path     TEXT    NOT NULL DEFAULT '',
    arguments           TEXT    NOT NULL DEFAULT '',
    mount_before_launch INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (profile_id, position)
);

INSERT INTO profile_tools_new (profile_id, position, name, executable_path, arguments,
                               mount_before_launch)
SELECT p.id, t.position, t.name, t.executable_path, t.arguments, t.mount_before_launch
FROM profile_tools t JOIN profiles_new p ON p.name = t.profile_name;

CREATE TABLE profile_mount_points_new (
    profile_id INTEGER NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
    id         TEXT    NOT NULL,
    name       TEXT    NOT NULL DEFAULT '',
    path       TEXT    NOT NULL DEFAULT '',
    PRIMARY KEY (profile_id, id)
);

INSERT INTO profile_mount_points_new (profile_id, id, name, path)
SELECT p.id, m.id, m.name, m.path
FROM profile_mount_points m JOIN profiles_new p ON p.name = m.profile_name;

DROP TABLE profile_entries;
DROP TABLE profile_tools;
DROP TABLE profile_mount_points;
DROP TABLE profiles;

ALTER TABLE profiles_new RENAME TO profiles;
ALTER TABLE profile_entries_new RENAME TO profile_entries;
ALTER TABLE profile_tools_new RENAME TO profile_tools;
ALTER TABLE profile_mount_points_new RENAME TO profile_mount_points;

-- Two indexes rather than one, because SQLite treats NULLs as distinct from each other: a plain
-- unique index on (game_id, name) would let any number of parked profiles share a name.
CREATE UNIQUE INDEX profiles_by_game_name ON profiles (game_id, name) WHERE game_id IS NOT NULL;
CREATE UNIQUE INDEX profiles_parked_name  ON profiles (name)          WHERE game_id IS NULL;

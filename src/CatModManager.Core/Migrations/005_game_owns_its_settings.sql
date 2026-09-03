-- Launch arguments and mount points move from the profile to the game.
--
-- They were on the profile because everything was, once. Neither is an arrangement of mods: the
-- launch line is how this installation is started, and a mount point is a folder inside it. Two
-- profiles of one game disagreeing about either would mean the same game launching two different
-- ways depending on which mod list happens to be open.
--
-- What stays on the profile is profile_entries.mount_point_id — which mod goes where. That really
-- does differ between arrangements, and it still points at these definitions by id.

ALTER TABLE games ADD COLUMN launch_arguments TEXT NOT NULL DEFAULT '';

-- One profile's copy wins, and it is the oldest, so the answer does not depend on how the rows
-- happen to be ordered today. Profiles of one game almost always agreed, and where they did not,
-- the one that has been there longest is the better guess.
--
-- Keep semicolons out of these comments, here and in every other migration. The runner splits the
-- file on that character without parsing it, so one inside a comment cuts the comment in half and
-- hands the tail to SQLite as if it were a statement.
UPDATE games SET launch_arguments = COALESCE((
    SELECT p.launch_arguments FROM profiles p
    WHERE p.game_id = games.id AND p.launch_arguments <> ''
    ORDER BY p.id
    LIMIT 1
), '');

CREATE TABLE game_mount_points (
    game_id INTEGER NOT NULL REFERENCES games(id) ON DELETE CASCADE,
    id      TEXT    NOT NULL,
    name    TEXT    NOT NULL DEFAULT '',
    path    TEXT    NOT NULL DEFAULT '',
    PRIMARY KEY (game_id, id)
);

-- Same collapsing: a mount point defined twice, once per profile of the same game, is one mount
-- point. GROUP BY takes it once rather than failing on the primary key.
INSERT INTO game_mount_points (game_id, id, name, path)
SELECT p.game_id, m.id, MIN(m.name), MIN(m.path)
FROM profile_mount_points m JOIN profiles p ON p.id = m.profile_id
WHERE p.game_id IS NOT NULL
GROUP BY p.game_id, m.id;

DROP TABLE profile_mount_points;

ALTER TABLE profiles DROP COLUMN launch_arguments;

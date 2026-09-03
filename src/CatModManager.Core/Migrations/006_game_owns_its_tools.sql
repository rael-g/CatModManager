-- External tools move from the profile to the game.
--
-- Same reasoning as the launch arguments in 005. A tool is SKSE, xEdit, Wrye Bash: something that
-- operates on the installation. Which mods are turned on does not change where xEdit lives, and two
-- profiles of one game disagreeing about that would mean the Tools tab emptying itself every time
-- the user switched mod lists.

CREATE TABLE game_tools (
    game_id             INTEGER NOT NULL REFERENCES games(id) ON DELETE CASCADE,
    position            INTEGER NOT NULL,
    name                TEXT    NOT NULL DEFAULT '',
    executable_path     TEXT    NOT NULL DEFAULT '',
    arguments           TEXT    NOT NULL DEFAULT '',
    mount_before_launch INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (game_id, position)
);

-- The union of what the game's profiles had, not one profile's copy.
--
-- 005 could take the oldest profile's launch line because there is one launch line and the profiles
-- almost certainly agreed. A tool list is a set the user built up, and different profiles may have
-- collected different halves of it. Dropping a tool somebody configured is worse than keeping one
-- they no longer use, so identical tools collapse and everything else survives.
--
-- Position is renumbered from zero per game, because the old ones came from separate lists and
-- would collide on the primary key. Order follows the profile that had the tool first.
--
-- A parked profile has no game to give its tools to, so it loses them. That is the same trade 005
-- made with its mount points, and it is the price of the setting belonging to the installation.
INSERT INTO game_tools (game_id, position, name, executable_path, arguments, mount_before_launch)
SELECT game_id,
       ROW_NUMBER() OVER (PARTITION BY game_id ORDER BY first_profile, first_position) - 1,
       name, executable_path, arguments, mount_before_launch
FROM (
    SELECT p.game_id            AS game_id,
           t.name               AS name,
           t.executable_path    AS executable_path,
           t.arguments          AS arguments,
           -- MAX rather than MIN: the checkbox is opt-in, and a tool that needed the mount in one
           -- profile needs it in all of them.
           MAX(t.mount_before_launch) AS mount_before_launch,
           MIN(p.id)            AS first_profile,
           MIN(t.position)      AS first_position
    FROM profile_tools t JOIN profiles p ON p.id = t.profile_id
    WHERE p.game_id IS NOT NULL
    GROUP BY p.game_id, t.name, t.executable_path, t.arguments
);

DROP TABLE profile_tools;

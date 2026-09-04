-- Tools that belong to no game in particular.
--
-- 006 moved tools from the profile to the game, which is right for xEdit pointed at one install.
-- It is wrong for the ones that are just programs -- a hex editor, an archive tool, a launcher --
-- which have nothing to do with which game is open and had to be retyped for every one of them.
--
-- Same columns as game_tools minus the owner, because a tool is the same thing either way and the
-- only difference is who gets to see it. A tool needing a per-game argument stays a game tool: the
-- user points a second entry at the same executable and gives it the argument that game wants.
CREATE TABLE global_tools (
    position            INTEGER NOT NULL PRIMARY KEY,
    name                TEXT    NOT NULL DEFAULT '',
    executable_path     TEXT    NOT NULL DEFAULT '',
    arguments           TEXT    NOT NULL DEFAULT '',
    mount_before_launch INTEGER NOT NULL DEFAULT 0
);

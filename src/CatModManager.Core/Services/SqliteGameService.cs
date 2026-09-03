using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace CatModManager.Core.Services;

/// <summary>Games in cmm.db. See <see cref="IGameService"/> for why they are stored on their own.</summary>
public class SqliteGameService : IGameService
{
    private readonly AppDatabase _db;

    public SqliteGameService(AppDatabase db) => _db = db;

    private const string Columns =
        "id, display_name, base_data_path, mods_folder_path, downloads_folder_path, " +
        "game_executable_path, game_support_id, launch_arguments";

    public Task<IReadOnlyList<Game>> ListGamesAsync() => Task.Run(() =>
    {
        using var conn = _db.Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM games ORDER BY display_name, id";

        var games = new List<Game>();
        using (var reader = cmd.ExecuteReader())
            while (reader.Read()) games.Add(Read(reader));

        foreach (var game in games) LoadMountPoints(conn, game);
        return (IReadOnlyList<Game>)games;
    });

    public Task<Game?> LoadGameAsync(long gameId) => Task.Run(() =>
    {
        using var conn = _db.Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM games WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", gameId);

        return ReadOne(conn, cmd);
    });

    public Task<Game?> FindByBasePathAsync(string baseDataPath) => Task.Run(() =>
    {
        // An empty folder identifies nothing — the unique index on base_data_path is partial for the
        // same reason. Half-configured games would otherwise all look like each other.
        if (string.IsNullOrWhiteSpace(baseDataPath)) return null;

        using var conn = _db.Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM games WHERE base_data_path = @base";
        cmd.Parameters.AddWithValue("@base", baseDataPath);

        return ReadOne(conn, cmd);
    });

    public Task<long> SaveGameAsync(Game game) => Task.Run(() =>
    {
        using var conn = _db.Open();

        if (game.Id == 0)
        {
            Db.Execute(conn, null, """
                INSERT INTO games (display_name, base_data_path, mods_folder_path,
                                   downloads_folder_path, game_executable_path, game_support_id,
                                   launch_arguments)
                VALUES (@name, @base, @mods, @downloads, @exe, @support, @args)
                """, Parameters(game));

            game.Id = Db.ScalarLong(conn, null, "SELECT last_insert_rowid()");
        }
        else
        {
            Db.Execute(conn, null, """
                UPDATE games SET display_name = @name, base_data_path = @base, mods_folder_path = @mods,
                                 downloads_folder_path = @downloads, game_executable_path = @exe,
                                 game_support_id = @support, launch_arguments = @args
                WHERE id = @id
                """, Parameters(game, ("@id", game.Id)));
        }

        SaveMountPoints(conn, game);
        return game.Id;
    });

    public Task DeleteGameAsync(long gameId) => Task.Run(() =>
    {
        using var conn = _db.Open();
        using var tx   = conn.BeginTransaction();

        // Spelled out rather than left to ON DELETE CASCADE. The cascade is declared, but it only
        // fires because Microsoft.Data.Sqlite happens to turn foreign keys on — they are off in
        // SQLite itself, and per connection. Deleting a game and leaving its profiles behind would
        // leave rows nothing can ever reach.
        Db.Execute(conn, tx, """
            DELETE FROM profile_entries
            WHERE profile_id IN (SELECT id FROM profiles WHERE game_id = @g)
            """, ("@g", gameId));
        Db.Execute(conn, tx, """
            DELETE FROM profile_tools
            WHERE profile_id IN (SELECT id FROM profiles WHERE game_id = @g)
            """, ("@g", gameId));
        Db.Execute(conn, tx, "DELETE FROM profiles  WHERE game_id = @g", ("@g", gameId));
        Db.Execute(conn, tx, "DELETE FROM game_mount_points WHERE game_id = @g", ("@g", gameId));
        Db.Execute(conn, tx, "DELETE FROM game_mods WHERE game_id = @g", ("@g", gameId));
        Db.Execute(conn, tx, "DELETE FROM games     WHERE id      = @g", ("@g", gameId));

        tx.Commit();
    });

    private static (string, object)[] Parameters(Game game, params (string, object)[] extra)
    {
        var own = new (string, object)[]
        {
            ("@name",      game.DisplayName         ?? ""),
            ("@base",      game.BaseDataPath        ?? ""),
            ("@mods",      game.ModsFolderPath      ?? ""),
            ("@downloads", game.DownloadsFolderPath ?? ""),
            ("@exe",       game.GameExecutablePath  ?? ""),
            ("@support",   game.GameSupportId       ?? "generic"),
            ("@args",      game.LaunchArguments     ?? ""),
        };

        if (extra.Length == 0) return own;

        var all = new (string, object)[own.Length + extra.Length];
        own.CopyTo(all, 0);
        extra.CopyTo(all, own.Length);
        return all;
    }

    /// <summary>The single row a command selects, with its mount points, or null.</summary>
    private static Game? ReadOne(SqliteConnection conn, SqliteCommand cmd)
    {
        Game game;
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read()) return null;
            game = Read(reader);
        }

        LoadMountPoints(conn, game);
        return game;
    }

    private static void LoadMountPoints(SqliteConnection conn, Game game)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, path FROM game_mount_points WHERE game_id = @g ORDER BY id";
        cmd.Parameters.AddWithValue("@g", game.Id);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            game.UserMountPoints.Add(
                new MountPointDef(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
    }

    /// <summary>
    /// Delete-then-insert, like the profile's lists: the set is a handful of rows, and a diff would
    /// have to reason about a mount point being renamed, which is how one goes missing.
    ///
    /// Game-defined mount points are merged in from the game definition on load, so storing them
    /// would leave a copy that goes stale the moment the definition changes.
    /// </summary>
    private static void SaveMountPoints(SqliteConnection conn, Game game)
    {
        using var tx = conn.BeginTransaction();

        Db.Execute(conn, tx, "DELETE FROM game_mount_points WHERE game_id = @g", ("@g", game.Id));

        foreach (var mp in (game.UserMountPoints ?? new List<MountPointDef>())
                     .Where(mp => !mp.IsGameDefined && !string.IsNullOrEmpty(mp.Id))
                     .GroupBy(mp => mp.Id).Select(g => g.Last()))
        {
            Db.Execute(conn, tx, """
                INSERT INTO game_mount_points (game_id, id, name, path) VALUES (@g, @id, @name, @path)
                """,
                ("@g", game.Id), ("@id", mp.Id), ("@name", mp.Name ?? ""), ("@path", mp.Path ?? ""));
        }

        tx.Commit();
    }

    private static Game Read(SqliteDataReader reader) => new()
    {
        Id                  = reader.GetInt64(0),
        DisplayName         = reader.GetString(1),
        BaseDataPath        = reader.GetString(2),
        ModsFolderPath      = reader.GetString(3),
        DownloadsFolderPath = reader.GetString(4),
        GameExecutablePath  = reader.GetString(5),
        GameSupportId       = reader.GetString(6),
        LaunchArguments     = reader.GetString(7),
    };
}

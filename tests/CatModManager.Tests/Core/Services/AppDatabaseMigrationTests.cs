using System;
using System.IO;
using CatModManager.Core.Services;
using CatModManager.Tests.Support;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CatModManager.Tests.Core.Services;

/// <summary>
/// The schema used to be recreated on every startup with CREATE TABLE IF NOT EXISTS, which could
/// never alter an existing table. It is now applied by Lilmihe, once per script, and the case that
/// decides whether that switch was safe is the one nobody can test by installing fresh: a database
/// that already has every table and has never heard of a migrations ledger.
/// </summary>
public class AppDatabaseMigrationTests : IDisposable
{
    private readonly string _dir;
    private readonly MockCatPathService _paths;

    public AppDatabaseMigrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "CMM_AppDb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _paths = new MockCatPathService(_dir);
    }

    private string DbPath => Path.Combine(_dir, "cmm.db");

    private long Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    [Fact]
    public void AFreshDatabaseGetsEverySchemaTableAndALedger()
    {
        _ = new AppDatabase(_paths);

        Assert.Equal(6, Scalar(
            """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name IN
                ('app_config', 'active_mounts', 'installed_plugins',
                 'root_swap_entries', 'hardlink_entries', 'plugin_settings');
            """));

        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM Migrations WHERE Id = '001_initial.sql';"));
    }

    /// <summary>
    /// Running the same migrations again must change nothing — that is the whole promise of the
    /// ledger, and the reason startup can call it unconditionally.
    /// </summary>
    [Fact]
    public void OpeningTheDatabaseTwiceAppliesTheMigrationsOnce()
    {
        _ = new AppDatabase(_paths);
        long afterFirstRun = Scalar("SELECT COUNT(*) FROM Migrations;");

        SqliteConnection.ClearAllPools();
        _ = new AppDatabase(_paths);

        // Against the first run's own count rather than a literal: hardcoding the number means
        // every new migration fails this test for the one reason that is not a bug.
        Assert.Equal(afterFirstRun, Scalar("SELECT COUNT(*) FROM Migrations;"));
    }

    /// <summary>
    /// Profiles moved out of TOML and into the database; 002 is the script that makes room for them.
    /// </summary>
    [Fact]
    public void AFreshDatabaseGetsTheProfileTables()
    {
        _ = new AppDatabase(_paths);

        // profile_mods is deliberately absent: 003 replaced it with profile_entries, which points at
        // the shared inventory instead of carrying a private copy of it.
        Assert.Equal(7, Scalar(
            """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name IN
                ('profiles', 'profile_entries',
                 'games', 'game_mods', 'game_mount_points', 'game_tools', 'global_tools');
            """));

        // Both went to the game, in 005 and 006. A mount point is a folder of the installation and
        // a tool is a program that operates on it — neither is an arrangement of mods. What stays
        // on profile_entries is which mod goes into which mount point.
        Assert.Equal(0, Scalar(
            """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name IN ('profile_mount_points', 'profile_tools');
            """));

        Assert.Equal(0, Scalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'profile_mods';"));

        Assert.Equal(6, Scalar(
            """
            SELECT COUNT(*) FROM Migrations WHERE Id IN
                ('002_profiles.sql', '003_games.sql', '004_named_games_and_profile_ids.sql',
                 '005_game_owns_its_settings.sql', '006_game_owns_its_tools.sql',
                 '007_global_tools.sql');
            """));

        // 004's two halves: the game has a name, and the profile has an id its children point at.
        Assert.Equal(1, Scalar(
            "SELECT COUNT(*) FROM pragma_table_info('games') WHERE name = 'display_name';"));
        Assert.Equal(1, Scalar(
            "SELECT COUNT(*) FROM pragma_table_info('profiles') WHERE name = 'id';"));
        // The child keys on that id now, and no longer carries the name.
        Assert.Equal(1, Scalar(
            "SELECT COUNT(*) FROM pragma_table_info('profile_entries') WHERE name = 'profile_id';"));
    }

    /// <summary>
    /// Every existing installation looks like this: the six tables, some data, no ledger. The first
    /// migration has to run over it and leave the data alone — which works only because 001 is
    /// entirely CREATE TABLE IF NOT EXISTS.
    /// </summary>
    [Fact]
    public void ADatabaseFromBeforeMigrationsKeepsItsDataAndGainsALedger()
    {
        using (var connection = new SqliteConnection($"Data Source={DbPath}"))
        {
            connection.Open();
            using var setup = connection.CreateCommand();
            setup.CommandText = """
                CREATE TABLE app_config (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO app_config (key, value) VALUES ('LastProfileName', 'Starfield');
                CREATE TABLE active_mounts (original_path TEXT PRIMARY KEY, backup_path TEXT NOT NULL);
                CREATE TABLE installed_plugins (package_id TEXT PRIMARY KEY, version TEXT NOT NULL, installed_at TEXT NOT NULL);
                CREATE TABLE root_swap_entries (id INTEGER PRIMARY KEY AUTOINCREMENT, game_folder TEXT NOT NULL, source_path TEXT NOT NULL, dest_path TEXT NOT NULL, original_backup_path TEXT);
                CREATE TABLE hardlink_entries (id INTEGER PRIMARY KEY AUTOINCREMENT, mount_point TEXT NOT NULL, rel_path TEXT NOT NULL, dest_path TEXT NOT NULL, backup_path TEXT);
                CREATE TABLE plugin_settings (plugin_id TEXT NOT NULL, key TEXT NOT NULL, value TEXT NOT NULL, PRIMARY KEY (plugin_id, key));
                """;
            setup.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        _ = new AppDatabase(_paths);

        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM Migrations WHERE Id = '001_initial.sql';"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM app_config WHERE value = 'Starfield';"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}

using System.IO;
using Microsoft.Data.Sqlite;

namespace CatModManager.Core.Services;

/// <summary>
/// Singleton that owns cmm.db. All core services share this connection factory.
/// </summary>
public class AppDatabase
{
    private readonly string _dbPath;

    public AppDatabase(ICatPathService pathService)
    {
        _dbPath = Path.Combine(pathService.BaseDataPath, "cmm.db");
        Directory.CreateDirectory(pathService.BaseDataPath);
        Initialize();
    }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    private void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
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
            """;
        cmd.ExecuteNonQuery();
    }
}

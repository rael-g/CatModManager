using System.IO;
using Microsoft.Data.Sqlite;

namespace CmmPlugin.NexusMods;

/// <summary>
/// Manages nexus.db — owns the tracks and downloads tables for this plugin.
/// </summary>
public class NexusDatabase
{
    private readonly string _dbPath;

    public NexusDatabase(string appDataPath)
    {
        _dbPath = Path.Combine(appDataPath, "nexus.db");
        Directory.CreateDirectory(appDataPath);
        Initialize();
    }

    public string? GetSetting(string key)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        var result = cmd.ExecuteScalar();
        return result as string;
    }

    public void SetSetting(string key, string value)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO settings (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
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
            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS tracks (
                mod_folder_path     TEXT PRIMARY KEY,
                mod_id              INTEGER NOT NULL,
                file_id             INTEGER NOT NULL,
                version             TEXT    NOT NULL,
                game_domain         TEXT    NOT NULL,
                source_archive_path TEXT
            );
            CREATE TABLE IF NOT EXISTS downloads (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                profile_name TEXT    NOT NULL,
                mod_name     TEXT    NOT NULL,
                file_name    TEXT    NOT NULL,
                local_path   TEXT    NOT NULL,
                mod_id       INTEGER NOT NULL,
                file_id      INTEGER NOT NULL,
                game_domain  TEXT    NOT NULL,
                version      TEXT    NOT NULL,
                category     TEXT    NOT NULL,
                has_failed   INTEGER NOT NULL DEFAULT 0
            );
            """;
        cmd.ExecuteNonQuery();
    }
}

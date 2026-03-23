using System;
using System.Text.Json;
using CatModManager.Core.Services;
using CatModManager.PluginSdk;

namespace CatModManager.Ui.Services;

/// <summary>
/// ICmmSettings backed by the SQLite plugin_settings table.
/// Each plugin gets an isolated namespace via its plugin ID.
/// Values are stored as JSON text to preserve type information.
/// </summary>
public class SqliteCmmSettings : ICmmSettings
{
    private readonly AppDatabase _db;
    private readonly string      _pluginId;

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = false };

    public SqliteCmmSettings(AppDatabase db, string pluginId)
    {
        _db       = db;
        _pluginId = pluginId;
    }

    public T? Get<T>(string key)
    {
        try
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM plugin_settings WHERE plugin_id = @pid AND key = @key";
            cmd.Parameters.AddWithValue("@pid", _pluginId);
            cmd.Parameters.AddWithValue("@key", key);
            var raw = cmd.ExecuteScalar() as string;
            if (raw == null) return default;
            return JsonSerializer.Deserialize<T>(raw, _opts);
        }
        catch { return default; }
    }

    public void Set<T>(string key, T data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, _opts);
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO plugin_settings (plugin_id, key, value) VALUES (@pid, @key, @val)
                ON CONFLICT(plugin_id, key) DO UPDATE SET value = excluded.value
                """;
            cmd.Parameters.AddWithValue("@pid", _pluginId);
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@val", json);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    /// <summary>No-op: SqliteCmmSettings writes on every Set call.</summary>
    public void Save() { }
}

/// <summary>
/// Creates per-plugin SqliteCmmSettings instances scoped by plugin ID.
/// </summary>
public class CmmSettingsFactory
{
    private readonly AppDatabase _db;

    public CmmSettingsFactory(AppDatabase db)
    {
        _db = db;
    }

    public ICmmSettings CreateForPlugin(string pluginId)
        => new SqliteCmmSettings(_db, pluginId);
}

using CatModManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace CatModManager.Core.Services;

public class ConfigService : IConfigService
{
    private readonly AppDatabase _db;
    private AppConfig _current = new();

    public AppConfig Current => _current;

    public ConfigService(AppDatabase db)
    {
        _db = db;
        Load();
    }

    public void Load()
    {
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM app_config";
            using var reader = cmd.ExecuteReader();
            var config = new AppConfig();
            while (reader.Read())
            {
                switch (reader.GetString(0))
                {
                    case "LastProfileName": config.LastProfileName = reader.GetString(1); break;
                    case "LastGameId":      config.LastGameId      = ReadLong(reader); break;
                    case "LastProfileId":   config.LastProfileId   = ReadLong(reader); break;
                    case "Theme":           config.Theme           = reader.GetString(1); break;
                }
            }
            _current = config;
        }
        catch (Exception ex) 
        { 
            _current = new AppConfig(); 
            System.Console.WriteLine($"[Config] Load failed, using defaults: {ex.Message}");
        }
    }

    /// <summary>
    /// The value stored under this key, or zero. Values are held as text, so a key written by an
    /// older version — or by hand — is a reason to fall back, not to fail to start.
    /// </summary>
    private static long ReadLong(Microsoft.Data.Sqlite.SqliteDataReader reader)
        => long.TryParse(reader.GetString(1), out var value) ? value : 0;

    public void Save()
    {
        try
        {
            using var conn = _db.Open();

            void Upsert(string key, string value)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO app_config (key, value) VALUES (@key, @value) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
                cmd.Parameters.AddWithValue("@key",   key);
                cmd.Parameters.AddWithValue("@value", value);
                cmd.ExecuteNonQuery();
            }

            Upsert("LastProfileName", _current.LastProfileName ?? string.Empty);
            Upsert("LastGameId",      _current.LastGameId.ToString());
            Upsert("LastProfileId",   _current.LastProfileId.ToString());
            Upsert("Theme",           _current.Theme           ?? "Dark");
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ConfigService.Save error: {ex}"); }
    }
}

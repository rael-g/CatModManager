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
                    case "Theme":           config.Theme           = reader.GetString(1); break;
                }
            }
            _current = config;
        }
        catch { _current = new AppConfig(); }
    }

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
            Upsert("Theme",           _current.Theme           ?? "Dark");
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ConfigService.Save error: {ex}"); }
    }
}

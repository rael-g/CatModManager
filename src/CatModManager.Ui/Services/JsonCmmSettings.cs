using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CatModManager.PluginSdk;

namespace CatModManager.Ui.Services;

/// <summary>
/// ICmmSettings implementation backed by a JSON file on disk.
/// Each plugin receives an isolated instance pointed at its own file
/// (AppDataPath/plugin_settings/{pluginId}.json).
/// </summary>
public class JsonCmmSettings : ICmmSettings
{
    private readonly string _filePath;
    private Dictionary<string, JsonElement> _data = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true
    };

    public JsonCmmSettings(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public T? Get<T>(string key)
    {
        if (!_data.TryGetValue(key, out var element)) return default;
        try { return element.Deserialize<T>(_opts); }
        catch { return default; }
    }

    public void Set<T>(string value, T data)
    {
        _data[value] = JsonSerializer.SerializeToElement(data, _opts);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_data, _opts));
        }
        catch { /* best-effort */ }
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            _data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, _opts)
                    ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { _data = new(StringComparer.OrdinalIgnoreCase); }
    }
}

/// <summary>
/// Creates per-plugin JsonCmmSettings instances scoped by plugin ID.
/// </summary>
public class CmmSettingsFactory
{
    private readonly string _baseDataPath;

    public CmmSettingsFactory(string baseDataPath)
    {
        _baseDataPath = baseDataPath;
    }

    public ICmmSettings CreateForPlugin(string pluginId)
    {
        var filePath = Path.Combine(_baseDataPath, "plugin_settings", pluginId + ".json");
        return new JsonCmmSettings(filePath);
    }
}

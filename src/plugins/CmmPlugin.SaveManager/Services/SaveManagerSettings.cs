using System.Text.Json;
using CatModManager.PluginSdk;

namespace CmmPlugin.SaveManager.Services;

/// <summary>What the user configured for one game.</summary>
public class GameSaveSettings
{
    /// <summary>Save folder chosen by hand. Wins over detection; null means detect.</summary>
    public string? SaveFolder { get; set; }

    public bool AutoSaveEnabled { get; set; }

    public int AutoSaveMinutes { get; set; } = DefaultAutoSaveMinutes;

    public const int DefaultAutoSaveMinutes = 5;
    public const int MinAutoSaveMinutes     = 1;
}

/// <summary>
/// The plugin's own small settings file, keyed by game.
///
/// Per game rather than global because both settings are about a particular game's saves: where they
/// live differs by install, and whether you want timed snapshots depends on what you are playing.
///
/// The save-folder override in particular is what keeps the tab usable at all. Automatic detection
/// covers the games we ship definitions for, installed where Steam put them, and nothing else — a
/// game with no definition, a hand-rolled Wine prefix, or saves on another disk left the whole tab
/// reading "No compatible game detected" with no way forward.
/// </summary>
public class SaveManagerSettings
{
    private readonly string        _path;
    private readonly IPluginLogger _log;
    private Model                  _model = new();

    private class Model
    {
        public Dictionary<string, GameSaveSettings> Games { get; set; } = new();
    }

    public SaveManagerSettings(string appDataPath, IPluginLogger log)
    {
        _path = Path.Combine(appDataPath, "save_backups", "settings.json");
        _log  = log;
        Load();
    }

    /// <summary>Settings for a game, defaulted if it has none yet. Never null, never persisted by reading.</summary>
    public GameSaveSettings For(string? gameId)
    {
        if (string.IsNullOrEmpty(gameId)) return new GameSaveSettings();
        return _model.Games.TryGetValue(gameId, out var s) ? s : new GameSaveSettings();
    }

    public void Update(string? gameId, Action<GameSaveSettings> change)
    {
        if (string.IsNullOrEmpty(gameId)) return;

        if (!_model.Games.TryGetValue(gameId, out var s))
            _model.Games[gameId] = s = new GameSaveSettings();

        change(s);
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            _model = JsonSerializer.Deserialize<Model>(File.ReadAllText(_path)) ?? new Model();
        }
        catch (Exception ex)
        {
            // A settings file we cannot read must not take the tab down with it; detection still works.
            _log.LogError("[SaveManager] Could not read settings; using defaults", ex);
            _model = new Model();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_model, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _log.LogError("[SaveManager] Could not write settings", ex);
        }
    }
}

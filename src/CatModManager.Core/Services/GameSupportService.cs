using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CatModManager.Core.Services;

public class GameSupportService : IGameSupportService
{
    private readonly ICatPathService _pathService;
    private readonly ILogService _logService;
    private readonly List<IGameSupport> _supports = new();

    public IGameSupport Default { get; } = new GenericGameSupport();

    public GameSupportService(ICatPathService pathService, ILogService logService)
    {
        _pathService = pathService;
        _logService = logService;
        RefreshSupports();
    }

    public void RefreshSupports()
    {
        _supports.Clear();
        _supports.Add(Default);

        // 1. Bundled definitions (shipped alongside the executable)
        var bundled = Path.Combine(AppContext.BaseDirectory, "game_definitions");
        LoadFromDirectory(bundled);

        // 2. User-installed definitions (AppData — override or extend bundled ones)
        LoadFromDirectory(_pathService.GameSupportsPath);
    }

    private void LoadFromDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.GetFiles(path, "*.toml"))
        {
            try
            {
                var custom = CustomGameSupport.LoadFromFile(file);
                if (custom == null) continue;
                // User definitions override bundled ones with the same GameId
                var existing = _supports.FindIndex(s => s.GameId == custom.GameId);
                if (existing >= 0) _supports[existing] = custom;
                else _supports.Add(custom);
            }
            catch (Exception ex)
            {
                _logService.LogError($"Failed to load game support from {file}", ex);
            }
        }
    }

    public IEnumerable<IGameSupport> GetAllSupports() => _supports;

    public IGameSupport GetSupportById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return Default;
        return _supports.FirstOrDefault(s => s.GameId == id) ?? Default;
    }

    public IGameSupport DetectSupport(string? gameExecutablePath)
    {
        if (string.IsNullOrEmpty(gameExecutablePath)) return Default;

        // 1. Local (Portabilidade)
        string localDef = Path.Combine(Path.GetDirectoryName(gameExecutablePath)!, "game_definition.toml");
        if (File.Exists(localDef))
        {
            var local = CustomGameSupport.LoadFromFile(localDef);
            if (local != null) return local;
        }

        // 2. Registrado (AppData)
        foreach (var support in _supports.Where(s => s.GameId != "generic"))
        {
            if (support.CanSupport(gameExecutablePath)) return support;
        }

        return Default;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Nett;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

public class GameDefinition
{
    public string GameId { get; set; } = "unknown";
    public string DisplayName { get; set; } = "Unknown Game";

    /// <summary>Nexus Mods game domain (e.g. "skyrimspecialedition"). Used by the NexusMods plugin.</summary>
    public string NexusDomain { get; set; } = "";
    /// <summary>Steam App ID. Used for future Steam integration.</summary>
    public int SteamAppId { get; set; } = 0;

    /// <summary>Relative path inside the game folder where the VFS mounts (e.g. "Data" or "LiesofP\Content\Paks\~mods").</summary>
    public string DataSubFolder { get; set; } = "";

    public string[] RequiredFiles { get; set; } = Array.Empty<string>();
}

public class CustomGameSupport : IGameSupport
{
    private readonly GameDefinition _def;

    public string GameId => _def.GameId;
    public string DisplayName => _def.DisplayName;
    public string? NexusDomain => string.IsNullOrEmpty(_def.NexusDomain) ? null : _def.NexusDomain;
    public int SteamAppId => _def.SteamAppId;
    public string DataSubFolder => _def.DataSubFolder;

    public CustomGameSupport(GameDefinition def) => _def = def;

    public bool CanSupport(string gameExecutablePath)
    {
        if (string.IsNullOrEmpty(gameExecutablePath)) return false;
        var dir = Path.GetDirectoryName(gameExecutablePath);
        if (string.IsNullOrEmpty(dir)) return false;

        foreach (var file in _def.RequiredFiles)
        {
            if (!File.Exists(Path.Combine(dir, file)) && !Directory.Exists(Path.Combine(dir, file)))
                return false;
        }
        return true;
    }

    public string GetLaunchArguments(IEnumerable<Mod> activeMods) => "";

    public static CustomGameSupport? LoadFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            var toml = File.ReadAllText(filePath);
            var def = Toml.ReadString<GameDefinition>(toml);
            return def != null ? new CustomGameSupport(def) : null;
        }
        catch { return null; }
    }
}



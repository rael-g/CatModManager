using System.Collections.Generic;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

public interface IGameSupport
{
    string GameId { get; }
    string DisplayName { get; }

    bool CanSupport(string gameExecutablePath);
    string GetLaunchArguments(IEnumerable<Mod> activeMods);

    /// <summary>Nexus Mods game domain. Null if not configured.</summary>
    string? NexusDomain { get; }
    /// <summary>Steam App ID. 0 if not configured.</summary>
    int SteamAppId { get; }

    /// <summary>
    /// Relative path inside the game folder where the VFS mounts (e.g. "Data" for Skyrim).
    /// Empty string combined with <see cref="RootSwapOnly"/> means no VFS — only RootSwap.
    /// </summary>
    string DataSubFolder { get; }

    /// <summary>
    /// When true the VFS is never mounted; mods are deployed exclusively via RootSwap.
    /// Use for games where the engine cannot load executables from a virtual filesystem
    /// (e.g. RE Engine games).
    /// </summary>
    bool RootSwapOnly { get; }

    /// <summary>
    /// Files/folders (relative to the game root) required for detection.
    /// The first .exe entry is treated as the primary executable.
    /// </summary>
    string[] RequiredFiles { get; }
}

public class GenericGameSupport : IGameSupport
{
    public string GameId => "generic";
    public string DisplayName => "Generic (No Special Logic)";

    public bool CanSupport(string gameExecutablePath) => true;
    public string GetLaunchArguments(IEnumerable<Mod> activeMods) => "";

    public string? NexusDomain => null;
    public int SteamAppId => 0;
    public string DataSubFolder => "";
    public bool RootSwapOnly => false;
    public string[] RequiredFiles => [];
}

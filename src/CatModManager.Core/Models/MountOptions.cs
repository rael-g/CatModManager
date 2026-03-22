using System.Collections.Generic;

namespace CatModManager.Core.Models;

public class MountOptions
{
    /// <summary>Real game root folder (e.g. C:\Games\Skyrim\). Never touched by the VFS.</summary>
    public string? GameFolderPath { get; set; }
    /// <summary>Relative path inside the game folder where mods are loaded (e.g. "Data" or "LiesofP\Content\Paks\~mods"). VFS mounts here.</summary>
    public string? DataSubFolder { get; set; }
    public List<Mod> ActiveMods { get; set; } = new();
    /// <summary>
    /// When true the VFS driver is never involved; mods are deployed exclusively via RootSwap.
    /// The game folder itself is never renamed. Suitable for RE Engine games.
    /// </summary>
    public bool RootSwapOnly { get; set; } = false;
}

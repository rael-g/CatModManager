using System.Collections.Generic;

namespace CatModManager.Core.Models;

public class MountOptions
{
    /// <summary>Real game root folder (e.g. C:\Games\Skyrim\). Never touched by the VFS.</summary>
    public string? GameFolderPath { get; set; }

    /// <summary>
    /// Legacy: relative path inside the game folder where the VFS mounts.
    /// Ignored when <see cref="MountPoints"/> is non-empty.
    /// </summary>
    public string? DataSubFolder { get; set; }

    public List<Mod> ActiveMods { get; set; } = new();

    /// <summary>
    /// When true the VFS driver is never involved; mods are deployed via RootSwap only.
    /// </summary>
    public bool RootSwapOnly { get; set; } = false;

    /// <summary>
    /// Effective mount points for this session.
    /// Built by combining game-defined + user-defined mount points.
    /// When empty the orchestrator falls back to <see cref="DataSubFolder"/>.
    /// </summary>
    public List<MountPointDef> MountPoints { get; set; } = new();
}

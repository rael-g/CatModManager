using System.Collections.Generic;

namespace CatModManager.Core.Models;

public class MountOptions
{
    /// <summary>Real game root folder (e.g. C:\Games\Skyrim\). Never touched by the VFS.</summary>
    public string? GameFolderPath { get; set; }

    public List<Mod> ActiveMods { get; set; } = new();

    /// <summary>
    /// Effective mount points for this session.
    /// Built by combining game-defined + user-defined mount points.
    /// </summary>
    public List<MountPointDef> MountPoints { get; set; } = new();
}

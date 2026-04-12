namespace CatModManager.Core.Models;

/// <summary>
/// Defines a location where mod files can be deployed.
/// <para>
/// <see cref="Path"/> can be:
///   • Relative (e.g. "Data") → resolved against the game folder at mount time.
///   • Absolute (e.g. "C:\Users\...\AppData\Local\...") → used as-is.
///   • Empty string → game root folder itself.
/// </para>
/// </summary>
public class MountPointDef
{
    /// <summary>Machine-readable key. Must be unique within a profile.</summary>
    public string Id   { get; set; } = "";

    /// <summary>Human-readable label shown in the UI.</summary>
    public string Name { get; set; } = "";

    /// <summary>Relative-to-game-folder or absolute path of the mount target.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// True when this mount point comes from the game definition TOML (read-only in UI).
    /// False when added by the user.
    /// </summary>
    public bool IsGameDefined { get; set; }

    public MountPointDef() { }

    public MountPointDef(string id, string name, string path, bool isGameDefined = false)
    {
        Id            = id;
        Name          = name;
        Path          = path;
        IsGameDefined = isGameDefined;
    }
}

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

    /// <summary>This mount point's absolute target, following the three rules documented above.</summary>
    public string ResolveAbsolute(string? gameFolder) => Resolve(Path, gameFolder);

    /// <summary>
    /// Turns a mount-point path into an absolute one. The rules live here, next to the doc comment
    /// that states them, because they used to be reimplemented at five call sites that disagreed
    /// about the edge cases — whether environment variables were expanded, and what an empty path or
    /// an unset game folder meant.
    /// </summary>
    public static string Resolve(string? path, string? gameFolder)
    {
        var expanded = System.Environment.ExpandEnvironmentVariables(path ?? "");

        // Empty means the game root itself, not "the current directory".
        if (string.IsNullOrEmpty(expanded)) return gameFolder ?? "";

        if (System.IO.Path.IsPathRooted(expanded)) return expanded;

        // A relative path with nowhere to be relative to is returned as-is: the caller checks
        // whether it exists, and inventing a root here would silently point at the wrong place.
        return string.IsNullOrEmpty(gameFolder)
            ? expanded
            : System.IO.Path.Combine(gameFolder, expanded);
    }

    public MountPointDef() { }

    public MountPointDef(string id, string name, string path, bool isGameDefined = false)
    {
        Id            = id;
        Name          = name;
        Path          = path;
        IsGameDefined = isGameDefined;
    }
}

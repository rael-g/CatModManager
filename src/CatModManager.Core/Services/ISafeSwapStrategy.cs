namespace CatModManager.Core.Services;

/// <summary>
/// Abstracts the folder-swap operation that some VFS drivers require before mounting.
///
/// Implementations:
///   NoBaseSwapStrategy  — HardlinkDriver (Windows): no folder rename, no base files
///                         needed; the driver deploys mod files directly into the game root.
///   PassthroughStrategy — FuseDriver (Linux): no folder rename; base files are served
///                         straight from the game folder via FUSE.
/// </summary>
public interface ISafeSwapStrategy
{
    /// <summary>
    /// Called before the VFS driver mounts.
    /// Returns the effective base folder path for the conflict resolver,
    /// or <c>null</c> if the driver handles all file deployment itself (HardlinkDriver).
    /// </summary>
    string? Prepare(string gameFolderPath, string mountPoint);

    /// <summary>Called after the VFS driver unmounts. Reverses any preparation.</summary>
    void Restore(string gameFolderPath, string mountPoint);
}

/// <summary>
/// For HardlinkDriver (Windows): no folder rename, no base game files in the map.
/// The driver creates hard links directly in the game root at mount time.
/// </summary>
public sealed class NoBaseSwapStrategy : ISafeSwapStrategy
{
    public string? Prepare(string gameFolderPath, string mountPoint) => null;
    public void Restore(string gameFolderPath, string mountPoint) { }
}

/// <summary>
/// For FuseDriver (Linux): no folder rename.
/// The game folder is passed as the base path so FUSE can serve original files
/// alongside mod overrides.
/// </summary>
public sealed class PassthroughSwapStrategy : ISafeSwapStrategy
{
    public string? Prepare(string gameFolderPath, string mountPoint) => gameFolderPath;
    public void Restore(string gameFolderPath, string mountPoint) { }
}

using System;
using System.IO;
using System.Linq;

namespace CatModManager.Core.Services;

/// <summary>
/// Abstracts the folder-swap operation that some VFS drivers require before mounting.
///
/// Implementations:
///   NoBaseSwapStrategy  — HardlinkDriver (Windows): no folder rename, no base files
///                         needed; the driver deploys mod files directly into the game root.
///   PassthroughStrategy — FuseDriver (Linux): no folder rename; base files are served
///                         straight from the game folder via FUSE.
///   FolderSwapStrategy  — WinFspDriver: renames the game folder to a hidden backup so the
///                         VFS can mount at the original path; records the swap in the DB
///                         so it can be recovered after a crash.
/// </summary>
public interface ISafeSwapStrategy
{
    /// <summary>
    /// Called before the VFS driver mounts.
    /// May perform a folder rename or other preparation.
    /// </summary>
    /// <param name="gameFolderPath">Real game root folder (never touched by callers).</param>
    /// <param name="mountPoint">
    ///   Path where the driver will actually mount (may equal gameFolderPath, or a
    ///   subdirectory when DataSubFolder is set).
    /// </param>
    /// <returns>
    ///   The effective base folder path to pass to the conflict resolver so it can
    ///   include original game files in the file map — or <c>null</c> if the driver
    ///   handles all file deployment itself (e.g. HardlinkDriver).
    /// </returns>
    string? Prepare(string gameFolderPath, string mountPoint);

    /// <summary>
    /// Called after the VFS driver unmounts.
    /// Reverses any folder operations done in <see cref="Prepare"/>.
    /// </summary>
    void Restore(string gameFolderPath, string mountPoint);
}

// ── Implementations ───────────────────────────────────────────────────────────

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

/// <summary>
/// For WinFspDriver: renames the mount-point folder to a hidden ".name.CMM_base"
/// backup so the WinFsp virtual filesystem can mount at the original path.
/// Registers the swap in <see cref="IVfsStateService"/> for crash recovery.
/// </summary>
public sealed class FolderSwapStrategy : ISafeSwapStrategy
{
    private const string BackupSuffix = ".CMM_base";

    private readonly IVfsStateService _stateService;
    private readonly ILogService      _log;

    public FolderSwapStrategy(IVfsStateService stateService, ILogService log)
    {
        _stateService = stateService;
        _log          = log;
    }

    public string? Prepare(string gameFolderPath, string mountPoint)
    {
        string normalized = mountPoint.TrimEnd(Path.DirectorySeparatorChar,
                                               Path.AltDirectorySeparatorChar);
        string folderName = Path.GetFileName(normalized);
        string backupPath = Path.Combine(
            Path.GetDirectoryName(normalized)!,
            '.' + folderName + BackupSuffix);

        if (!Directory.Exists(backupPath))
        {
            _log.Log($"[SafeSwap] Moving '{normalized}' → backup.");
            _stateService.RegisterMount(normalized, backupPath);
            Directory.Move(normalized, backupPath);
            try { new DirectoryInfo(backupPath).Attributes |= System.IO.FileAttributes.Hidden; } catch { }
        }
        else
        {
            _log.Log("[SafeSwap] Backup already exists — clearing current target.");
            if (Directory.Exists(normalized) &&
                !Directory.EnumerateFileSystemEntries(normalized).Any())
                Directory.Delete(normalized);
        }

        if (!Directory.Exists(normalized))
            Directory.CreateDirectory(normalized);

        return backupPath;
    }

    public void Restore(string gameFolderPath, string mountPoint)
    {
        _stateService.UnregisterMount(mountPoint);
        _stateService.RecoverStaleMounts();
    }
}

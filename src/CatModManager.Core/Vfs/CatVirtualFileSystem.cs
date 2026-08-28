using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.VirtualFileSystem;

namespace CatModManager.Core.Vfs;

/// <summary>
/// Orchestrates mod files into a virtual view and mounts them via a low-level driver.
/// Implements IFileSystem to satisfy driver requirements by delegating to an internal backend.
/// </summary>
public class CatVirtualFileSystem : IVirtualFileSystem, IFileSystem
{
    private readonly IConflictResolver  _resolver;
    private readonly IFileSystemDriver  _driver;
    
    private ModFileSystemBackend? _backend;

    public bool IsMounted => _driver.IsMounted;
    public event EventHandler<string>? ErrorOccurred;

    public CatVirtualFileSystem(IConflictResolver resolver, IFileSystemDriver driver)
    {
        _resolver = resolver;
        _driver   = driver;
    }

    public void Mount(string gameFolderPath, List<Mod> activeMods)
        => MountPrepared(gameFolderPath, BuildFileMap(gameFolderPath, activeMods));

    /// <summary>
    /// Resolves the mods into the map of what this mount point would deploy, without touching the
    /// filesystem. Split out from <see cref="Mount"/> so the orchestrator can hold every mount
    /// point's map at once and settle the overlaps between them before anything is linked: two
    /// mount points resolving to the same file used to deploy over each other, each keeping its own
    /// backup, which overwrote the game's original with the other one's mod file.
    /// </summary>
    public Dictionary<string, IFileSource> BuildFileMap(string gameFolderPath, List<Mod> activeMods)
    {
        // MountPoint is now implicitly gameFolderPath (already resolved by orchestrator)
        var rawMap = _resolver.ResolveConflicts(activeMods, gameFolderPath, null, gameFolderPath);

        var fileMap = new Dictionary<string, IFileSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in rawMap)
        {
            string cleanKey = kvp.Key.Replace('/', '\\').Trim('\\');
            if (!string.IsNullOrEmpty(cleanKey))
                fileMap[cleanKey] = kvp.Value;
        }
        return fileMap;
    }

    public void MountPrepared(string gameFolderPath, Dictionary<string, IFileSource> fileMap)
    {
        if (IsMounted) return;

        try
        {
            _backend = new ModFileSystemBackend(fileMap);
            _driver.Mount(gameFolderPath, this);
        }
        catch (Exception ex)
        {
            ReleaseBackend();
            ErrorOccurred?.Invoke(this, ex.Message);
            throw;
        }
    }

    public void Unmount()
    {
        // Before the driver, not after. Unmounting deletes the deployed links and renames the
        // displaced game files back, and Windows will not let go of a file this process still
        // holds a descriptor on — the deletes fail quietly and the mod files stay in the game
        // folder for good.
        ReleaseBackend();
        _driver.Unmount();
    }

    private void ReleaseBackend()
    {
        _backend?.Dispose();
        _backend = null;
    }

    public void Dispose() => _driver.Dispose();

    // ── IFileSystem Delegation ──────────────────────────────────────────────

    public FileSystemNodeInfo? GetInfo(string path) => _backend?.GetInfo(path);
    public IEnumerable<string> ReadDirectory(string path) => _backend?.ReadDirectory(path) ?? Enumerable.Empty<string>();
    public Stream? OpenFile(string path) => _backend?.OpenFile(path);
    public string? GetPhysicalPath(string path) => _backend?.GetPhysicalPath(path);
}

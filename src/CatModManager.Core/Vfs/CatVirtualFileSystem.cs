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
    {
        if (IsMounted) return;

        try
        {
            // MountPoint is now implicitly gameFolderPath (already resolved by orchestrator)
            string mountPoint = gameFolderPath;

            var rawMap = _resolver.ResolveConflicts(activeMods, gameFolderPath, null, mountPoint);

            var fileMap = new Dictionary<string, IFileSource>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in rawMap)
            {
                string cleanKey = kvp.Key.Replace('/', '\\').Trim('\\');
                if (!string.IsNullOrEmpty(cleanKey))
                    fileMap[cleanKey] = kvp.Value;
            }

            _backend = new ModFileSystemBackend(fileMap);
            _driver.Mount(mountPoint, this);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            throw;
        }
    }

    public void Unmount()
    {
        _driver.Unmount();
        _backend = null;
    }

    public void Dispose() => _driver.Dispose();

    // ── IFileSystem Delegation ──────────────────────────────────────────────

    public FileSystemNodeInfo? GetInfo(string path) => _backend?.GetInfo(path);
    public IEnumerable<string> ReadDirectory(string path) => _backend?.ReadDirectory(path) ?? Enumerable.Empty<string>();
    public Stream? OpenFile(string path) => _backend?.OpenFile(path);
    public string? GetPhysicalPath(string path) => _backend?.GetPhysicalPath(path);
}

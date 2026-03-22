using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.VirtualFileSystem;

namespace CatModManager.Core.Vfs;

public class CatVirtualFileSystem : IVirtualFileSystem, IFileSystem
{
    private readonly IConflictResolver  _resolver;
    private readonly IFileSystemDriver  _driver;
    private readonly ISafeSwapStrategy  _swapStrategy;

    private IDictionary<string, IFileSource> _fileMap =
        new Dictionary<string, IFileSource>(StringComparer.OrdinalIgnoreCase);

    // O(1) directory listing cache built at mount time.
    private readonly Dictionary<string, HashSet<string>> _directoryCache =
        new(StringComparer.OrdinalIgnoreCase);

    // Stored so Unmount() can pass them to the strategy.
    private string? _lastGameFolderPath;
    private string? _lastMountPoint;

    public bool IsMounted => _driver.IsMounted;
    public event EventHandler<string>? ErrorOccurred;

    // ── Constructors ─────────────────────────────────────────────────────────

    /// <summary>DI constructor — strategy injected by the container.</summary>
    public CatVirtualFileSystem(
        IConflictResolver  resolver,
        IFileSystemDriver  driver,
        ISafeSwapStrategy  swapStrategy)
    {
        _resolver     = resolver;
        _driver       = driver;
        _swapStrategy = swapStrategy;
    }

    /// <summary>
    /// Convenience constructor used in tests that don't care about swap behaviour.
    /// Uses <see cref="NoBaseSwapStrategy"/> so the test sees only mod files.
    /// </summary>
    public CatVirtualFileSystem(IConflictResolver resolver, IFileSystemDriver driver)
        : this(resolver, driver, new NoBaseSwapStrategy())
    {
    }

    // ── IVirtualFileSystem ───────────────────────────────────────────────────

    public void Mount(string gameFolderPath, List<Mod> activeMods, string? dataSubFolder = null)
    {
        try
        {
            // Compute the VFS/hardlink mount point (may be a subfolder).
            string mountPoint = string.IsNullOrEmpty(dataSubFolder)
                ? gameFolderPath
                : Path.Combine(gameFolderPath, dataSubFolder);

            _resolver.ForbiddenPath = mountPoint;

            // Let the strategy prepare the mount (e.g. rename folder for WinFSP,
            // or no-op for HardlinkDriver / FuseDriver).
            // It returns the effective baseFolderPath for the conflict resolver:
            //   null         → driver handles all files itself (HardlinkDriver)
            //   gameFolderPath → serve base files from game folder (FuseDriver)
            //   gameFolderPath → serve base files from the game folder (FuseDriver)
            string? effectiveBase = _swapStrategy.Prepare(gameFolderPath, mountPoint);

            // DataSubFolder stripping makes sense only when the VFS mounts at a
            // subfolder and serves ALL files. When effectiveBase is null (HardlinkDriver),
            // mod files keep their full game-relative paths — no stripping needed.
            string? effectiveDataSub = effectiveBase != null ? dataSubFolder : null;

            var rawMap = _resolver.ResolveConflicts(activeMods, effectiveBase, effectiveDataSub);

            _fileMap = new Dictionary<string, IFileSource>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in rawMap)
            {
                string cleanKey = kvp.Key.Replace('/', '\\').Trim('\\');
                if (!string.IsNullOrEmpty(cleanKey))
                    _fileMap[cleanKey] = kvp.Value;
            }

            BuildDirectoryCache();
            _driver.Mount(mountPoint, this);

            _lastGameFolderPath = gameFolderPath;
            _lastMountPoint     = mountPoint;
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

        if (_lastGameFolderPath != null && _lastMountPoint != null)
            _swapStrategy.Restore(_lastGameFolderPath, _lastMountPoint);

        _fileMap.Clear();
        _directoryCache.Clear();
        _lastGameFolderPath = null;
        _lastMountPoint     = null;
    }

    public void Dispose() => _driver.Dispose();

    // ── IFileSystem ──────────────────────────────────────────────────────────

    public FileSystemNodeInfo? GetInfo(string path)
    {
        string normalized = path.Replace('/', '\\').Trim('\\');

        if (string.IsNullOrEmpty(normalized))
            return new FileSystemNodeInfo { IsDirectory = true };

        if (_fileMap.TryGetValue(normalized, out var source))
            return new FileSystemNodeInfo { IsDirectory = false, Size = source.Length, LastWriteTime = source.LastWriteTime };

        if (_directoryCache.ContainsKey(normalized))
            return new FileSystemNodeInfo { IsDirectory = true };

        return null;
    }

    public IEnumerable<string> ReadDirectory(string path)
    {
        string normalized = path.Replace('/', '\\').Trim('\\');
        return _directoryCache.TryGetValue(normalized, out var entries)
            ? entries
            : Enumerable.Empty<string>();
    }

    public Stream? OpenFile(string path)
    {
        string normalized = path.Replace('/', '\\').Trim('\\');
        if (_fileMap.TryGetValue(normalized, out var source)) return source.OpenRead();
        return null;
    }

    public string? GetPhysicalPath(string path)
    {
        string normalized = path.Replace('/', '\\').Trim('\\');
        if (_fileMap.TryGetValue(normalized, out var source) && source is PhysicalFileSource pfs)
            return pfs.FilePath;
        return null;
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private void BuildDirectoryCache()
    {
        _directoryCache.Clear();
        _directoryCache[""] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in _fileMap.Keys)
        {
            string[] parts     = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            string   parentPath = "";

            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i];

                if (!_directoryCache.ContainsKey(parentPath))
                    _directoryCache[parentPath] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                _directoryCache[parentPath].Add(name);

                if (i < parts.Length - 1)
                    parentPath = string.IsNullOrEmpty(parentPath)
                        ? name
                        : parentPath + "\\" + name;
            }
        }
    }
}

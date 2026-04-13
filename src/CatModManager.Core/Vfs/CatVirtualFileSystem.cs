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

    private IDictionary<string, IFileSource> _fileMap =
        new Dictionary<string, IFileSource>(StringComparer.OrdinalIgnoreCase);

    // O(1) directory listing cache built at mount time.
    private readonly Dictionary<string, HashSet<string>> _directoryCache =
        new(StringComparer.OrdinalIgnoreCase);

    private string? _lastGameFolderPath;
    private string? _lastMountPoint;

    public bool IsMounted => _driver.IsMounted;
    public event EventHandler<string>? ErrorOccurred;

    // ── Constructor ──────────────────────────────────────────────────────────

    public CatVirtualFileSystem(IConflictResolver resolver, IFileSystemDriver driver)
    {
        _resolver = resolver;
        _driver   = driver;
    }

    // ── IVirtualFileSystem ───────────────────────────────────────────────────

    public void Mount(string gameFolderPath, List<Mod> activeMods, string? dataSubFolder = null)
    {
        try
        {
            string mountPoint = string.IsNullOrEmpty(dataSubFolder)
                ? gameFolderPath
                : Path.IsPathRooted(dataSubFolder)
                    ? dataSubFolder
                    : Path.Combine(gameFolderPath, dataSubFolder);

            // With multi-mount VFS, we no longer swap folders physically.
            // All drivers now serve mod files directly or via hardlinks.
            var rawMap = _resolver.ResolveConflicts(activeMods, gameFolderPath, dataSubFolder, mountPoint);

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

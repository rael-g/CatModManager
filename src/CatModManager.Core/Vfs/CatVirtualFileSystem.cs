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
    private readonly IConflictResolver _resolver;
    private readonly IFileSystemDriver _driver;
    private IDictionary<string, IFileSource> _fileMap = new Dictionary<string, IFileSource>(StringComparer.OrdinalIgnoreCase);

    // Directory cache providing O(1) access to folder structures during game execution
    private readonly Dictionary<string, HashSet<string>> _directoryCache = new(StringComparer.OrdinalIgnoreCase);

    public bool IsMounted => _driver.IsMounted;
    public event EventHandler<string>? ErrorOccurred;

    public CatVirtualFileSystem(IConflictResolver resolver)
        : this(resolver, FileSystemFactory.CreateDriver())
    {
    }

    public CatVirtualFileSystem(IConflictResolver resolver, IFileSystemDriver driver)
    {
        _resolver = resolver;
        _driver = driver;
    }

    public void Mount(string mountPoint, List<Mod> activeMods, string? baseFolderPath, string? dataSubFolder = null)
    {
        try
        {
            _resolver.ForbiddenPath = mountPoint;

            var rawMap = _resolver.ResolveConflicts(activeMods, baseFolderPath, dataSubFolder);

            _fileMap = new Dictionary<string, IFileSource>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in rawMap)
            {
                string cleanKey = kvp.Key.Replace('/', '\\').Trim('\\');
                if (!string.IsNullOrEmpty(cleanKey))
                    _fileMap[cleanKey] = kvp.Value;
            }

            BuildDirectoryCache();
            _driver.Mount(mountPoint, this);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            throw;
        }
    }

    private void BuildDirectoryCache()
    {
        _directoryCache.Clear();
        _directoryCache[""] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in _fileMap.Keys)
        {
            string[] parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            string parentPath = "";

            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i];

                if (!_directoryCache.ContainsKey(parentPath))
                    _directoryCache[parentPath] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                _directoryCache[parentPath].Add(name);

                if (i < parts.Length - 1)
                {
                    parentPath = string.IsNullOrEmpty(parentPath) ? name : parentPath + "\\" + name;
                }
            }
        }
    }

    public void Unmount()
    {
        _driver.Unmount();
        _fileMap.Clear();
        _directoryCache.Clear();
    }

    public void Dispose() => _driver.Dispose();

    public FileSystemNodeInfo? GetInfo(string path)
    {
        string normalized = path.Replace('/', '\\').Trim('\\');

        if (string.IsNullOrEmpty(normalized))
            return new FileSystemNodeInfo { IsDirectory = true };

        if (_fileMap.TryGetValue(normalized, out var source))
        {
            return new FileSystemNodeInfo { IsDirectory = false, Size = source.Length, LastWriteTime = source.LastWriteTime };
        }

        if (_directoryCache.ContainsKey(normalized))
        {
            return new FileSystemNodeInfo { IsDirectory = true };
        }

        return null;
    }

    public IEnumerable<string> ReadDirectory(string path)
    {
        string normalized = path.Replace('/', '\\').Trim('\\');
        if (_directoryCache.TryGetValue(normalized, out var entries))
        {
            return entries;
        }
        return Enumerable.Empty<string>();
    }

    public Stream? OpenFile(string path)
    {
        string normalized = path.Replace('/', '\\').Trim('\\');
        if (_fileMap.TryGetValue(normalized, out var source)) return source.OpenRead();
        return null;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatModManager.Core.Services;
using CatModManager.VirtualFileSystem;

namespace CatModManager.Core.Vfs;

/// <summary>
/// Implements the low-level IFileSystem interface expected by VFS drivers.
/// Decouples the file mapping and directory caching from the VFS orchestration.
/// </summary>
internal class ModFileSystemBackend : IFileSystem
{
    private readonly IDictionary<string, IFileSource> _fileMap;
    private readonly Dictionary<string, HashSet<string>> _directoryCache = new(StringComparer.OrdinalIgnoreCase);

    public ModFileSystemBackend(IDictionary<string, IFileSource> fileMap)
    {
        _fileMap = fileMap;
        BuildDirectoryCache();
    }

    public FileSystemNodeInfo? GetInfo(string path)
    {
        string normalized = Normalize(path);

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
        string normalized = Normalize(path);
        return _directoryCache.TryGetValue(normalized, out var entries)
            ? entries
            : Enumerable.Empty<string>();
    }

    public Stream? OpenFile(string path)
    {
        string normalized = Normalize(path);
        if (_fileMap.TryGetValue(normalized, out var source)) return source.OpenRead();
        return null;
    }

    public string? GetPhysicalPath(string path)
    {
        string normalized = Normalize(path);
        if (_fileMap.TryGetValue(normalized, out var source) && source is PhysicalFileSource pfs)
            return pfs.FilePath;
        return null;
    }

    private string Normalize(string path) => path.Replace('/', '\\').Trim('\\');

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
                    parentPath = string.IsNullOrEmpty(parentPath)
                        ? name
                        : parentPath + "\\" + name;
            }
        }
    }
}

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
internal class ModFileSystemBackend : IFileSystem, IDisposable
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

    /// <summary>
    /// Releases the descriptors the resolver pinned on game-folder files. Nothing kept them alive
    /// beyond the mount before, so they lingered until the GC happened to run — leaving handles
    /// open on files the unmount is trying to delete, on no schedule anyone can predict.
    /// </summary>
    public void Dispose()
    {
        foreach (var source in _fileMap.Values)
            if (source is IDisposable d)
                try { d.Dispose(); } catch { /* one stuck handle must not stop the rest */ }
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

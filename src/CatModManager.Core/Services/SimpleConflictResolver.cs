using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatModManager.Core.Models;
using SharpCompress.Archives;

namespace CatModManager.Core.Services;

public class SimpleConflictResolver : IConflictResolver
{
    private readonly ILogService _logService;

    public SimpleConflictResolver(ILogService logService)
    {
        _logService = logService;
    }

    public IDictionary<string, IFileSource> ResolveConflicts(
        IEnumerable<Mod> activeMods,
        string? baseFolderPath,
        string? dataSubFolder = null,
        string? forbiddenPath = null)
    {
        var finalMap = new Dictionary<string, IFileSource>(StringComparer.OrdinalIgnoreCase);

        string? normalizedForbidden = !string.IsNullOrEmpty(forbiddenPath)
            ? Path.GetFullPath(forbiddenPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : null;

        if (!string.IsNullOrEmpty(baseFolderPath) && Directory.Exists(baseFolderPath))
        {
            string fullRoot = Path.GetFullPath(baseFolderPath);
            ScanRecursive(fullRoot, fullRoot, finalMap, relPath => NormalizePath(relPath), normalizedForbidden);
        }

        var prefixesToStrip = BuildPrefixList(dataSubFolder);

        foreach (var mod in activeMods.OrderBy(m => m.Priority))
        {
            int before = finalMap.Count;
            if (mod.IsDirectory)
            {
                string fullRoot = Path.GetFullPath(mod.ModRootPath);
                ScanRecursive(fullRoot, fullRoot, finalMap, relPath =>
                {
                    var normalized = NormalizePath(relPath);
                    return StripDataPrefix(normalized, prefixesToStrip);
                }, null);
            }
            else if (mod.IsPhysicalArchive)
            {
                try
                {
                    using var archive = ArchiveFactory.Open(mod.ModRootPath);
                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    {
                        string targetPath = StripDataPrefix(NormalizePath(entry.Key ?? ""), prefixesToStrip);
                        if (!string.IsNullOrEmpty(targetPath))
                            finalMap[targetPath] = new ArchiveFileSource(mod.ModRootPath, entry.Key ?? "", (long)entry.Size, entry.LastModifiedTime ?? DateTime.Now);
                    }
                }
                catch (Exception ex) { _logService.LogError($"Failed to read mod archive: {mod.Name}", ex); }
            }
            else
            {
                _logService.Log($"  WARN: mod path not found: {mod.ModRootPath}");
            }
            int added = finalMap.Count - before;
            _logService.Log($"  {mod.Name}: {added} file(s) added/overridden (path exists: {mod.IsDirectory || mod.IsPhysicalArchive})");
        }

        return finalMap;
    }

    /// <summary>
    /// Generates all suffixes of <paramref name="dataSubFolder"/>, longest first.
    /// Example: "A\B\C" → ["A\B\C", "B\C", "C"]
    /// </summary>
    private static IReadOnlyList<string> BuildPrefixList(string? dataSubFolder)
    {
        if (string.IsNullOrWhiteSpace(dataSubFolder)) return Array.Empty<string>();
        var parts = dataSubFolder.Replace('/', '\\').Trim('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>(parts.Length);
        for (int i = 0; i < parts.Length; i++)
            result.Add(string.Join('\\', parts.Skip(i)));
        return result; 
    }

    private static string StripDataPrefix(string normalizedPath, IReadOnlyList<string> prefixesToStrip)
    {
        foreach (var prefix in prefixesToStrip)
            if (normalizedPath.StartsWith(prefix + '\\', StringComparison.OrdinalIgnoreCase))
                return normalizedPath[(prefix.Length + 1)..];

        var firstSep = normalizedPath.IndexOf('\\');
        if (firstSep > 0)
        {
            var withoutWrapper = normalizedPath[(firstSep + 1)..];
            foreach (var prefix in prefixesToStrip)
                if (withoutWrapper.StartsWith(prefix + '\\', StringComparison.OrdinalIgnoreCase))
                    return withoutWrapper[(prefix.Length + 1)..];
        }

        return normalizedPath;
    }

    public IReadOnlyList<ConflictReport> GetConflictReport(IEnumerable<Mod> activeMods)
    {
        var modList = activeMods.OrderBy(m => m.Priority).ToList();

        var winnerMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var reportMap = new Dictionary<string, List<ModConflictInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in modList)
            reportMap[mod.Name] = new List<ModConflictInfo>();

        foreach (var mod in modList)
        {
            var files = GetModFiles(mod);
            foreach (var file in files)
            {
                if (winnerMap.TryGetValue(file, out var previousWinner))
                {
                    reportMap[previousWinner].Add(new ModConflictInfo(file, mod.Name, ConflictType.Loses));
                    reportMap[mod.Name].Add(new ModConflictInfo(file, previousWinner, ConflictType.Wins));
                }
                winnerMap[file] = mod.Name;
            }
        }

        return modList
            .Select(m => new ConflictReport { ModName = m.Name, Conflicts = reportMap[m.Name] })
            .ToList();
    }

    private IEnumerable<string> GetModFiles(Mod mod)
    {
        if (Directory.Exists(mod.ModRootPath))
        {
            string fullRoot = Path.GetFullPath(mod.ModRootPath);
            return EnumerateFiles(fullRoot, fullRoot);
        }
        if (File.Exists(mod.ModRootPath))
        {
            try
            {
                using var archive = ArchiveFactory.Open(mod.ModRootPath);
                return archive.Entries
                    .Where(e => !e.IsDirectory)
                    .Select(e => NormalizePath(e.Key ?? ""))
                    .ToList();
            }
            catch { }
        }
        return Enumerable.Empty<string>();
    }

    private IEnumerable<string> EnumerateFiles(string root, string current)
    {
        var result = new List<string>();
        try
        {
            foreach (var file in Directory.GetFiles(current))
                result.Add(NormalizePath(Path.GetRelativePath(root, file)));
            foreach (var dir in Directory.GetDirectories(current))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith(".") || name.Contains("CMM_base")) continue;
                var di = new DirectoryInfo(dir);
                if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                result.AddRange(EnumerateFiles(root, dir));
            }
        }
        catch { }
        return result;
    }

    private void ScanRecursive(string root, string current, Dictionary<string, IFileSource> map, Func<string, string?> pathMapper, string? forbidden)
    {
        try
        {
            var entries = Directory.GetFileSystemEntries(current);

            foreach (var entry in entries)
            {
                bool isDir = Directory.Exists(entry);
                string entryName = Path.GetFileName(entry);

                if (isDir)
                {
                    string fullPath = Path.GetFullPath(entry).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    if (forbidden != null && fullPath.Equals(forbidden, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Avoid recursive loops with CMM internal backups
                    if (entryName.StartsWith(".") || entryName.Contains("CMM_base"))
                        continue;


                    // Skip junctions/symlinks to prevent infinite Windows loops
                    var di = new DirectoryInfo(entry);
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;

                    ScanRecursive(root, entry, map, pathMapper, forbidden);
                }
                else
                {
                    string relativePath = Path.GetRelativePath(root, entry);
                    string? targetKey = pathMapper(relativePath);

                    if (!string.IsNullOrEmpty(targetKey))
                        map[targetKey] = new PhysicalFileSource(entry);
                }
            }
        }
        catch { }
    }

    private string NormalizePath(string path) => path.Replace('/', '\\').Trim('\\');
}

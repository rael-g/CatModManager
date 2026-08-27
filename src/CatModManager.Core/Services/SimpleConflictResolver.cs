using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatModManager.Core.Models;
using CatModManager.PluginSdk;

namespace CatModManager.Core.Services;

/// <summary>
/// Scans mod folders and resolves file conflicts by priority.
/// Now uses IArchiveExtractor to handle mods that are still in archive format.
/// </summary>
public class SimpleConflictResolver : IConflictResolver
{
    private readonly ILogService _logService;
    private readonly IArchiveExtractor _extractor;

    public SimpleConflictResolver(ILogService logService, IArchiveExtractor extractor)
    {
        _logService = logService;
        _extractor = extractor;
    }

    public IDictionary<string, IFileSource> ResolveConflicts(
        IEnumerable<Mod> mods, 
        string? baseFolderPath,
        string? dataSubFolder = null,
        string? forbiddenPath = null)
    {
        var finalMap = new Dictionary<string, IFileSource>(StringComparer.OrdinalIgnoreCase);

        // 1. Map physical game files first (lowest priority)
        if (!string.IsNullOrEmpty(baseFolderPath) && Directory.Exists(baseFolderPath))
        {
            string scanRoot = MountPointDef.Resolve(dataSubFolder, baseFolderPath);

            if (Directory.Exists(scanRoot))
            {
                ScanRecursive(scanRoot, scanRoot, finalMap, p => p, forbiddenPath);
            }
        }

        // 2. Overlay enabled mods (sorted by priority ASC, so higher priority overwrites)
        var sortedMods = mods.Where(m => m.IsEnabled && !m.IsBroken).OrderBy(m => m.Priority);

        foreach (var mod in sortedMods)
        {
            if (mod.IsArchive)
            {
                try
                {
                    var files = _extractor.GetFileList(mod.ModRootPath);
                    foreach (var file in files)
                    {
                        // Archive entries usually use '/' — GetFileList already normalized to '\'
                        string cleanKey = file.Trim('\\');
                        if (string.IsNullOrEmpty(cleanKey)) continue;

                        finalMap[cleanKey] = new ArchiveFileSource(mod.ModRootPath, cleanKey);
                    }
                }
                catch (Exception ex)
                {
                    _logService.LogError($"ConflictResolver: Failed to read archive mod {mod.Name}", ex);
                }
            }
            else if (Directory.Exists(mod.ModRootPath))
            {
                ScanRecursive(mod.ModRootPath, mod.ModRootPath, finalMap, p => p, null);
            }
        }

        return finalMap;
    }

    public IReadOnlyList<ConflictReport> GetConflictReport(IEnumerable<Mod> activeMods)
    {
        // Conceptual implementation — usually shows UI which mod wins for each file.
        return Array.Empty<ConflictReport>();
    }

    private void ScanRecursive(
        string currentDir, 
        string rootDir, 
        IDictionary<string, IFileSource> map, 
        Func<string, string> pathTransform,
        string? forbiddenPath)
    {
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(currentDir))
            {
                if (forbiddenPath != null && entry.Equals(forbiddenPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Directory.Exists(entry))
                {
                    ScanRecursive(entry, rootDir, map, pathTransform, forbiddenPath);
                }
                else
                {
                    string relPath = Path.GetRelativePath(rootDir, entry);
                    string targetKey = pathTransform(relPath).Replace('/', '\\').Trim('\\');

                    if (!string.IsNullOrEmpty(targetKey))
                        map[targetKey] = new PhysicalFileSource(entry);
                }
            }
        }
        catch { }
    }
}

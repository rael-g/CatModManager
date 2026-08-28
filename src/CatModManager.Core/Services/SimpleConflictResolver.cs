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

        // Where the overlay will land. Files under it get a descriptor held open, because after the
        // mount their path resolves back through the overlay; files outside it can be re-opened
        // normally. Mods usually live outside — but nothing stops one sitting inside, so this is
        // decided per path rather than per scan.
        string? shadowRoot = null;

        // 1. Map physical game files first (lowest priority)
        if (!string.IsNullOrEmpty(baseFolderPath) && Directory.Exists(baseFolderPath))
        {
            string scanRoot = MountPointDef.Resolve(dataSubFolder, baseFolderPath);

            if (Directory.Exists(scanRoot))
            {
                // Everything here sits under the directory about to be mounted over.
                ScanRecursive(scanRoot, scanRoot, finalMap, p => p, forbiddenPath, pinHandles: true);
            }

            shadowRoot = scanRoot;
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
                ScanRecursive(mod.ModRootPath, mod.ModRootPath, finalMap, p => p, null,
                              pinHandles: IsUnder(mod.ModRootPath, shadowRoot));
            }
        }

        return finalMap;
    }

    public IReadOnlyList<ConflictReport> GetConflictReport(IEnumerable<Mod> activeMods)
    {
        // Conceptual implementation — usually shows UI which mod wins for each file.
        return Array.Empty<ConflictReport>();
    }

    /// <summary>
    /// Whether <paramref name="path"/> sits inside <paramref name="root"/> — i.e. whether a mount
    /// over <paramref name="root"/> will shadow it. Linux paths are case-sensitive; Windows are not.
    /// </summary>
    internal static bool IsUnder(string? path, string? root)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        try
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            string under = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

            return full.Equals(under, comparison)
                || full.StartsWith(under + Path.DirectorySeparatorChar, comparison);
        }
        catch { return false; }
    }

    private void ScanRecursive(
        string currentDir, 
        string rootDir, 
        IDictionary<string, IFileSource> map, 
        Func<string, string> pathTransform,
        string? forbiddenPath,
        bool pinHandles = false)
    {
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(currentDir))
            {
                if (forbiddenPath != null && entry.Equals(forbiddenPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Directory.Exists(entry))
                {
                    ScanRecursive(entry, rootDir, map, pathTransform, forbiddenPath, pinHandles);
                }
                else
                {
                    string relPath = Path.GetRelativePath(rootDir, entry);
                    string targetKey = pathTransform(relPath).Replace('/', '\\').Trim('\\');

                    if (!string.IsNullOrEmpty(targetKey))
                    {
                        // Per entry, so one unreadable file costs one file — the whole scan used to
                        // stop at the first one, because the catch below wraps the enumeration.
                        try { map[targetKey] = new PhysicalFileSource(entry, pinHandles); }
                        catch (Exception ex)
                        {
                            _logService.LogError($"ConflictResolver: skipping unreadable file '{entry}'", ex);
                        }
                    }
                }
            }
        }
        catch { }
    }
}

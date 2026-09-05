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

    /// <summary>
    /// Which mods overwrite which, file by file.
    ///
    /// Deliberately mod-versus-mod only: a mod covering a base game file is the entire point of a
    /// mod, not a conflict worth reporting, so the base folder is never scanned here. Files are
    /// keyed exactly as <see cref="ResolveConflicts"/> keys them, because a report that disagreed
    /// with the mount about what counts as the same file would be worse than no report.
    /// </summary>
    public IReadOnlyList<ConflictReport> GetConflictReport(IEnumerable<Mod> activeMods)
    {
        // Ascending priority, matching ResolveConflicts: later wins.
        var mods = activeMods.Where(m => m.IsEnabled && !m.IsBroken)
                             .OrderBy(m => m.Priority)
                             .ToList();

        // Key -> the mods claiming it, already in ascending priority so the last one is the winner.
        var claims = new Dictionary<string, List<Mod>>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
            foreach (var key in EnumerateKeys(mod))
            {
                if (!claims.TryGetValue(key, out var list))
                    claims[key] = list = new List<Mod>();
                list.Add(mod);
            }

        var reports = mods.ToDictionary(m => m, _ => new List<ModConflictInfo>());

        foreach (var (key, claimants) in claims)
        {
            if (claimants.Count < 2) continue;

            var winner = claimants[^1];
            for (int i = 0; i < claimants.Count - 1; i++)
            {
                var loser = claimants[i];
                reports[loser].Add(new ModConflictInfo(key, winner.Name, ConflictType.Loses));
                reports[winner].Add(new ModConflictInfo(key, loser.Name, ConflictType.Wins));
            }
        }

        return mods.Select(m => new ConflictReport
        {
            ModName   = m.Name,
            Conflicts = reports[m].OrderBy(c => c.FilePath, StringComparer.OrdinalIgnoreCase).ToList()
        }).ToList();
    }

    /// <summary>
    /// The file keys a mod contributes, without opening a single file.
    ///
    /// Who overwrites whom is decided by paths and priority alone, so this reads directory entries
    /// and archive tables of contents and nothing else — the panel must stay cheap enough to run on
    /// every reorder.
    /// </summary>
    private IEnumerable<string> EnumerateKeys(Mod mod)
    {
        if (mod.IsArchive)
        {
            List<string> entries;
            try { entries = _extractor.GetFileList(mod.ModRootPath).ToList(); }
            catch (Exception ex)
            {
                _logService.LogError($"ConflictResolver: Failed to read archive mod {mod.Name}", ex);
                yield break;
            }

            foreach (var file in entries)
            {
                string key = file.Trim('\\');
                if (!string.IsNullOrEmpty(key)) yield return key;
            }
            yield break;
        }

        if (!Directory.Exists(mod.ModRootPath)) yield break;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(mod.ModRootPath, "*", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            _logService.LogError($"ConflictResolver: cannot list mod '{mod.Name}'", ex);
            yield break;
        }

        // Not ScanRecursive: that builds a PhysicalFileSource per entry, and the constructor stats
        // the file for size and mtime. None of that decides an override, and paying for it on every
        // reorder would make the panel cost what the mount costs.
        foreach (var file in files)
        {
            string key = Path.GetRelativePath(mod.ModRootPath, file).Replace('/', '\\').Trim('\\');
            if (!string.IsNullOrEmpty(key)) yield return key;
        }
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

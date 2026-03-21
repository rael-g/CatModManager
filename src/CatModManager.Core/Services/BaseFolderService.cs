using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CatModManager.Core.Services;

/// <summary>
/// Handles physical installation of mods directly into the game folder ("base folder install").
/// A manifest file (<see cref="ManifestFileName"/>) is written inside the mod's mods-folder entry
/// so that files can be cleanly removed on uninstall.
/// </summary>
public static class BaseFolderService
{
    public const string ManifestFileName = ".cmm_basefolder.manifest";

    /// <summary>
    /// Copies all files from <paramref name="modFolder"/> into <paramref name="gameFolder"/>,
    /// preserving relative paths, and writes a manifest listing what was installed.
    /// </summary>
    public static async Task InstallAsync(string modFolder, string gameFolder)
    {
        var files = Directory.GetFiles(modFolder, "*", SearchOption.AllDirectories);

        var relativePaths = files
            .Select(f => Path.GetRelativePath(modFolder, f))
            .Where(r => !r.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        await Task.Run(() =>
        {
            foreach (var rel in relativePaths)
            {
                var src = Path.Combine(modFolder, rel);
                var dst = Path.Combine(gameFolder, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }
        });

        await File.WriteAllLinesAsync(Path.Combine(modFolder, ManifestFileName), relativePaths);
    }

    /// <summary>
    /// Reads the manifest written by <see cref="InstallAsync"/> and deletes those files from
    /// <paramref name="gameFolder"/>. Empty parent directories are pruned up to (but not including)
    /// the game folder itself.
    /// </summary>
    public static async Task UninstallAsync(string modFolder, string gameFolder)
    {
        var manifest = Path.Combine(modFolder, ManifestFileName);
        if (!File.Exists(manifest)) return;

        IEnumerable<string> lines = await File.ReadAllLinesAsync(manifest);

        await Task.Run(() =>
        {
            foreach (var rel in lines)
            {
                if (string.IsNullOrWhiteSpace(rel)) continue;
                var dst = Path.Combine(gameFolder, rel);
                if (File.Exists(dst)) File.Delete(dst);
                TryDeleteEmptyParents(Path.GetDirectoryName(dst), gameFolder);
            }
        });

        File.Delete(manifest);
    }

    /// <summary>Returns true if the mod was installed to the game folder (manifest exists).</summary>
    public static bool IsInstalled(string modFolder) =>
        File.Exists(Path.Combine(modFolder, ManifestFileName));

    private static void TryDeleteEmptyParents(string? dir, string stopAt)
    {
        while (!string.IsNullOrEmpty(dir) &&
               !dir.Equals(stopAt, StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(dir) &&
               !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }
}

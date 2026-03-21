using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.PluginSdk;
using CmmPlugin.Capcom.Services;
using SharpCompress.Archives;

namespace CmmPlugin.Capcom.Installers;

/// <summary>
/// Smart mod installer for RE Engine games.
/// Automatically routes files to the correct location:
///   - reframework/** → kept under reframework/ in the mod folder (VFS-managed)
///   - *.pak, *.dll   → placed in Root/ (deployed to game root at mount time via RootSwap)
///   - everything else → mod folder root (preview images, modinfo.ini, etc.)
/// </summary>
public class ReEngineModInstaller : IModInstaller
{
    private static readonly HashSet<string> RootExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pak", ".dll", ".exe" };

    private static readonly HashSet<string> MetaFiles =
        new(StringComparer.OrdinalIgnoreCase) { "modinfo.ini", "readme.txt", "readme.md", "license.txt" };

    private readonly IModManagerState _state;

    public ReEngineModInstaller(IModManagerState state) => _state = state;

    public bool CanInstall(string archivePath) =>
        ReEngineDetector.Detect(_state.GameExecutablePath) != null &&
        IsArchive(archivePath);

    public Task<InstallResult> InstallAsync(string archivePath, IInstallContext ctx)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            var entries = archive.Entries
                .Where(e => !e.IsDirectory && e.Key != null)
                .ToList();

            foreach (var entry in entries)
            {
                var srcPath  = entry.Key!.Replace('\\', '/').Trim('/');
                var fileName = Path.GetFileName(srcPath);
                var ext      = Path.GetExtension(fileName);

                string destPath;

                if (srcPath.StartsWith("reframework/", StringComparison.OrdinalIgnoreCase))
                {
                    // REFramework subtree: keep structure; VFS mounts here
                    destPath = srcPath;
                }
                else if (RootExtensions.Contains(ext))
                {
                    // Pak/DLL mods: go to Root/ for RootSwap deployment at mount time
                    destPath = "Root/" + fileName;
                }
                else if (MetaFiles.Contains(fileName) ||
                         ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif")
                {
                    // Preview images and metadata: stay at mod root
                    destPath = fileName;
                }
                else
                {
                    // Default: preserve original structure
                    destPath = srcPath;
                }

                // Last writer wins on collision (archive order; caller handles priority)
                mapping[destPath] = srcPath;
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(InstallResult.Failure($"[RE Engine] Failed to read archive: {ex.Message}"));
        }

        return Task.FromResult(InstallResult.Success(mapping));
    }

    private static bool IsArchive(string path)
    {
        var ext = Path.GetExtension(path);
        return ext is ".zip" or ".7z" or ".rar" or ".tar";
    }
}

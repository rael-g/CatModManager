using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Services;
using SharpCompress.Archives;

namespace CmmPlugin.BethesdaTools.Installers;

/// <summary>
/// Mod installer for Bethesda games (Skyrim, Fallout, Starfield, etc.).
///
/// Routing rules:
///   1. Strip single wrapper folder if all entries share the same top-level directory.
///   2. Strip "Data/" prefix — the VFS mounts the mod root as the game's Data/ directory.
///   3. Everything else lands at mod root as-is (treated as Data content).
///
/// For mods that must go to the game root (e.g. SKSE), use "Install to Root" from
/// the right-click context menu on the completed download.
/// </summary>
public class BethesdaModInstaller : IModInstaller
{
    private readonly IModManagerState _state;

    public BethesdaModInstaller(IModManagerState state) => _state = state;

    public bool CanInstall(string archivePath) =>
        BethesdaDetector.IsBethesdaExecutable(_state.GameExecutablePath) &&
        IsArchive(archivePath) &&
        !HasFomodConfig(archivePath);

    private static bool HasFomodConfig(string archivePath)
    {
        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            return archive.Entries.Any(e =>
                !e.IsDirectory &&
                e.Key?.Replace('\\', '/').EndsWith("fomod/ModuleConfig.xml", StringComparison.OrdinalIgnoreCase) == true);
        }
        catch { return false; }
    }

    public Task<InstallResult> InstallAsync(string archivePath, IInstallContext ctx)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            var entries = archive.Entries
                .Where(e => !e.IsDirectory && e.Key != null)
                .ToList();

            // Detect single wrapper folder (e.g. "skse64_2_02_06/...") and strip it.
            var topDirs = entries
                .Select(e => e.Key!.Replace('\\', '/').Trim('/').Split('/')[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            string? wrapperPrefix = topDirs.Count == 1 ? topDirs[0] + "/" : null;

            foreach (var entry in entries)
            {
                var originalKey = entry.Key!.Replace('\\', '/').Trim('/');

                // Strip wrapper prefix for routing, but keep originalKey for extraction lookup.
                var stripped = wrapperPrefix != null && originalKey.StartsWith(wrapperPrefix, StringComparison.OrdinalIgnoreCase)
                    ? originalKey[wrapperPrefix.Length..]
                    : originalKey;

                // Strip "Data/" prefix — VFS mounts mod root AS Data/
                var destPath = stripped.StartsWith("Data/", StringComparison.OrdinalIgnoreCase) && stripped.Length > 5
                    ? stripped[5..]
                    : stripped;

                if (!string.IsNullOrEmpty(destPath))
                    mapping[destPath] = originalKey;
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(InstallResult.Failure($"[Bethesda] Failed to read archive: {ex.Message}"));
        }

        return Task.FromResult(InstallResult.Success(mapping));
    }

    private static bool IsArchive(string path)
    {
        var ext = Path.GetExtension(path);
        return ext is ".zip" or ".7z" or ".rar" or ".tar";
    }
}

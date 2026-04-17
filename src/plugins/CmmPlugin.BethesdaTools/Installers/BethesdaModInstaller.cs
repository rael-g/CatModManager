using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Services;

namespace CmmPlugin.BethesdaTools.Installers;

/// <summary>
/// Mod installer for Bethesda games (Skyrim, Fallout, Starfield, etc.).
/// Uses IArchiveExtractor to handle routing and file discovery.
/// </summary>
public class BethesdaModInstaller : IModInstaller
{
    private readonly IModManagerState _state;
    private readonly IArchiveExtractor _extractor;
    private readonly BethesdaDetector _detector;

    public BethesdaModInstaller(IModManagerState state, IArchiveExtractor extractor, BethesdaDetector detector)
    {
        _state = state;
        _extractor = extractor;
        _detector = detector;
    }

    public bool CanInstall(string archivePath) =>
        _detector.IsBethesdaExecutable(_state.GameExecutablePath) &&
        IsArchive(archivePath) &&
        !HasFomodConfig(archivePath);

    private bool HasFomodConfig(string archivePath)
    {
        try
        {
            var files = _extractor.GetFileList(archivePath);
            return files.Any(f => 
                f.Replace('\\', '/').EndsWith("fomod/ModuleConfig.xml", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    public Task<InstallResult> InstallAsync(string archivePath, IInstallContext ctx)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var entries = _extractor.GetFileList(archivePath)
                .Select(e => e.Replace('\\', '/').Trim('/'))
                .ToList();

            // Detect single wrapper folder (e.g. "skse64_2_02_06/...") and strip it.
            var topDirs = entries
                .Select(e => e.Split('/')[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            string? wrapperPrefix = topDirs.Count == 1 ? topDirs[0] + "/" : null;

            foreach (var entryKey in entries)
            {
                // Strip wrapper prefix for routing
                var stripped = wrapperPrefix != null && entryKey.StartsWith(wrapperPrefix, StringComparison.OrdinalIgnoreCase)
                    ? entryKey[wrapperPrefix.Length..]
                    : entryKey;

                // Strip "Data/" prefix — VFS mounts mod root AS Data/
                var destPath = stripped.StartsWith("Data/", StringComparison.OrdinalIgnoreCase) && stripped.Length > 5
                    ? stripped[5..]
                    : stripped;

                if (!string.IsNullOrEmpty(destPath))
                    mapping[entryKey] = destPath;
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

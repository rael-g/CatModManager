using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CatModManager.PluginSdk;
using CmmPlugin.REEngine.Services;

namespace CmmPlugin.REEngine.Installers;

/// <summary>
/// Mod installer for RE Engine games.
/// Uses IArchiveExtractor to remain independent of specific compression libraries.
/// </summary>
public class ReEngineModInstaller : IModInstaller
{
    private readonly IModManagerState _state;
    private readonly IArchiveExtractor _extractor;

    public ReEngineModInstaller(IModManagerState state, IArchiveExtractor extractor)
    {
        _state = state;
        _extractor = extractor;
    }

    public bool CanInstall(string archivePath) =>
        ReEngineDetector.Detect(_state.GameExecutablePath) != null &&
        IsArchive(archivePath);

    public async Task<InstallResult> InstallAsync(string archivePath, IInstallContext ctx)
    {
        List<string> entries;
        try
        {
            // Use the abstraction instead of SharpCompress directly
            entries = _extractor.GetFileList(archivePath)
                .Select(e => e.Replace('\\', '/').Trim('/'))
                .ToList();
        }
        catch (Exception ex)
        {
            return InstallResult.Failure($"[RE Engine] Failed to read archive: {ex.Message}");
        }

        // Top-level names (first path segment of every entry)
        var topFolders = entries
            .Select(e => e.Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Variant zip: ≥2 top-level folders each containing a modinfo.ini
        var variantFolders = topFolders
            .Where(f => entries.Any(e =>
                e.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(e).Equals("modinfo.ini", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f)
            .ToList();

        bool isVariantZip = variantFolders.Count >= 2;

        if (!isVariantZip)
        {
            // Single wrapper folder? All entries share the same top-level directory prefix.
            bool isWrapper = topFolders.Count == 1
                && entries.All(e => e.StartsWith(topFolders[0] + "/", StringComparison.OrdinalIgnoreCase));
            string sourceKey = isWrapper ? topFolders[0] + "/" : "";
            return InstallResult.Success(new Dictionary<string, string> { [sourceKey] = "" });
        }

        // Show variant picker
        IReadOnlyList<string> chosenVariants = [];
        ReEngineVariantPickerWindow? picker = null;
        bool picked = false;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var mainWindow = (Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            picker = new ReEngineVariantPickerWindow(variantFolders);
            picked = await picker.ShowDialog<bool?>(mainWindow!) == true;
        });

        if (!picked || picker == null || picker.SelectedVariants.Count == 0)
            return InstallResult.Failure("Installation cancelled.");

        chosenVariants = picker.SelectedVariants;

        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variant in chosenVariants)
            mapping[variant + "/"] = "";

        return InstallResult.Success(mapping);
    }

    private static bool IsArchive(string path)
    {
        var ext = Path.GetExtension(path);
        return ext is ".zip" or ".7z" or ".rar" or ".tar";
    }
}

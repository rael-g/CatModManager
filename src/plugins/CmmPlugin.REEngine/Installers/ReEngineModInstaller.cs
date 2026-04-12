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
using SharpCompress.Archives;

namespace CmmPlugin.REEngine.Installers;

/// <summary>
/// Mod installer for RE Engine games.
///
/// Mirrors the Fluffy Mod Manager convention:
///   - Single wrapper folder: stripped automatically (via empty-source mapping).
///   - Variant zip (≥2 top-level folders each with a modinfo.ini): user picks which to install.
///   - Everything else: all files extracted as-is to the mod folder root.
///
/// We deliberately avoid per-file mapping built from archive.Entries because some solid
/// RARs omit large entries from the TOC enumeration while ExtractAllEntries streams them
/// correctly. Instead we use coarse folder-level mappings ("" = copy everything).
/// </summary>
public class ReEngineModInstaller : IModInstaller
{
    private readonly IModManagerState _state;

    public ReEngineModInstaller(IModManagerState state) => _state = state;

    public bool CanInstall(string archivePath) =>
        ReEngineDetector.Detect(_state.GameExecutablePath) != null &&
        IsArchive(archivePath);

    public async Task<InstallResult> InstallAsync(string archivePath, IInstallContext ctx)
    {
        // Read entry keys just for variant detection — we don't need every file, only
        // the folder structure (top-level names + modinfo.ini presence).
        List<string> entries;
        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            entries = archive.Entries
                .Where(e => e.Key != null)
                .Select(e => e.Key!.Replace('\\', '/').Trim('/'))
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
            // Map that prefix → mod root so the wrapper is stripped automatically.
            // Otherwise map archive root → mod root ("" = copy everything as-is).
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

        // Map each chosen variant folder → mod root.
        // The trailing "/" on the key causes InstallModFromMappingAsync to use the
        // StartsWith folder-match branch, stripping the variant prefix automatically.
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


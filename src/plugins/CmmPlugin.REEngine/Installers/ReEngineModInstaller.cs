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
/// Smart mod installer for RE Engine games.
///
/// The VFS mounts at the game's reframework/ sub-folder (DataSubFolder = "reframework"),
/// so only files under reframework/ are served by the VFS. Everything that must live
/// at the game root (natives/, *.pak, *.dll) is placed in the mod's Root/ sub-folder
/// and deployed via RootSwap at mount time.
///
/// Routing table:
///   reframework/**   → reframework/... (VFS-managed)
///   natives/**       → Root/natives/... (RootSwap → game root)
///   *.pak / *.dll    → Root/*.pak|dll   (RootSwap → game root)
///   images / meta    → mod root only    (inspector display)
///
/// Many Nexus/Fluffy zips bundle several mutually-exclusive variants as
/// top-level sub-folders (each with a modinfo.ini). This installer detects
/// that pattern and shows a simple picker so the user selects which to install.
/// </summary>
public class ReEngineModInstaller : IModInstaller
{
    private static readonly HashSet<string> RootExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pak", ".dll" };

    private static readonly HashSet<string> MetaFiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "modinfo.ini", "readme.txt", "readme.md", "license.txt", "changelog.txt"
        };

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".gif" };

    private readonly IModManagerState _state;

    public ReEngineModInstaller(IModManagerState state) => _state = state;

    public bool CanInstall(string archivePath) =>
        ReEngineDetector.Detect(_state.GameExecutablePath) != null &&
        IsArchive(archivePath);

    public async Task<InstallResult> InstallAsync(string archivePath, IInstallContext ctx)
    {
        List<string> entries;
        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            entries = archive.Entries
                .Where(e => !e.IsDirectory && e.Key != null)
                .Select(e => e.Key!.Replace('\\', '/').Trim('/'))
                .ToList();
        }
        catch (Exception ex)
        {
            return InstallResult.Failure($"[RE Engine] Failed to read archive: {ex.Message}");
        }

        // ── Detect archive layout ─────────────────────────────────────────────

        var topFolders = entries
            .Select(e => e.Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A "variant zip" has ≥2 top-level folders each containing a modinfo.ini
        var variantFolders = topFolders
            .Where(f => entries.Any(e =>
                e.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(e).Equals("modinfo.ini", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f)
            .ToList();

        bool isVariantZip = variantFolders.Count >= 2;
        bool hasWrapper   = !isVariantZip &&
                            topFolders.Count == 1 &&
                            entries.All(e => e.StartsWith(topFolders[0] + "/", StringComparison.OrdinalIgnoreCase));

        // ── Variant picker ────────────────────────────────────────────────────

        IReadOnlyList<string> chosenVariants;
        if (isVariantZip)
        {
            bool? picked = null;
            ReEngineVariantPickerWindow? picker = null;

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var mainWindow = (Application.Current?.ApplicationLifetime
                    as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                picker = new ReEngineVariantPickerWindow(variantFolders);
                picked = await picker.ShowDialog<bool?>(mainWindow);
            });

            if (picked != true || picker == null || picker.SelectedVariants.Count == 0)
                return InstallResult.Failure("Installation cancelled.");

            chosenVariants = picker.SelectedVariants;
        }
        else
        {
            // Single wrapper or flat zip — treat the single top-level folder (if any) as the only variant
            chosenVariants = variantFolders.Count == 1 ? variantFolders : [];
        }

        // ── Build file mapping ────────────────────────────────────────────────

        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var srcPath in entries)
        {
            string effectivePath = srcPath;

            if (isVariantZip)
            {
                var matchingVariant = chosenVariants.FirstOrDefault(v =>
                    srcPath.StartsWith(v + "/", StringComparison.OrdinalIgnoreCase));

                if (matchingVariant == null) continue;  // top-level screenshot etc → skip
                effectivePath = srcPath[(matchingVariant.Length + 1)..];
                if (string.IsNullOrEmpty(effectivePath)) continue;
            }
            else if (hasWrapper)
            {
                int slash = effectivePath.IndexOf('/');
                if (slash < 0) continue;
                effectivePath = effectivePath[(slash + 1)..];
                if (string.IsNullOrEmpty(effectivePath)) continue;
            }

            string fileName = Path.GetFileName(effectivePath);
            string ext      = Path.GetExtension(fileName);

            string destPath;

            if (MetaFiles.Contains(fileName) || ImageExtensions.Contains(ext))
            {
                // Metadata and screenshots → mod root for inspector display only
                destPath = fileName;
            }
            else if (_state.RootSwapOnly)
            {
                // RootSwap-only mode (RE Engine): every game file goes to Root/ so it is
                // deployed to the actual game folder via RootSwap at mount time.
                // natives/, reframework/, *.pak, *.dll — all land at game root through RootSwap.
                destPath = "Root/" + effectivePath;
            }
            else if (effectivePath.StartsWith("reframework/", StringComparison.OrdinalIgnoreCase))
            {
                // VFS mode with DataSubFolder="reframework": scripts served by VFS
                destPath = effectivePath;
            }
            else if (effectivePath.StartsWith("natives/", StringComparison.OrdinalIgnoreCase) ||
                     RootExtensions.Contains(ext))
            {
                // Must live at game root → RootSwap
                destPath = "Root/" + (RootExtensions.Contains(ext) ? fileName : effectivePath);
            }
            else
            {
                destPath = effectivePath;
            }

            mapping[destPath] = srcPath;
        }

        if (mapping.Count == 0)
            return InstallResult.Failure("[RE Engine] No files were selected for installation.");

        return InstallResult.Success(mapping);
    }

    private static bool IsArchive(string path)
    {
        var ext = Path.GetExtension(path);
        return ext is ".zip" or ".7z" or ".rar" or ".tar";
    }
}

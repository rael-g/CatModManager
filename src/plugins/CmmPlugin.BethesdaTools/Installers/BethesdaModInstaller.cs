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
/// Smart mod installer for Bethesda games (Skyrim, Fallout, Starfield, etc.).
///
/// Routing rules (applied in order):
///   1. Path starts with "Data/" → strip prefix, place at mod root (VFS mounts at Data/)
///   2. Known plugin/BSA extensions (.esp, .esm, .esl, .bsa, .ba2) → mod root
///   3. Known Data sub-folders (meshes, textures, skse, …) → mod root
///   4. Known game-root sub-folders (enbseries, reshade-shaders, …) → Root/
///   5. DLL/EXE at archive top level → Root/
///   6. Known game-root config files (enblocal.ini, ReShade.ini, …) → Root/
///   7. Everything else → mod root (safe default; Data content)
/// </summary>
public class BethesdaModInstaller : IModInstaller
{
    // Subfolders that are always inside the game's Data directory
    private static readonly HashSet<string> DataSubfolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "animations", "animationdata", "animationdataoutput",
        "bodyslide",
        "distantlod", "dyndolod", "docs", "documentation",
        "facegendata", "fomod",
        "grass",
        "interface",
        "lodsettings",
        "meshes", "music",
        "pex", "psc",
        "scripts", "seq", "shaders", "skse", "sound", "sounds", "source", "strings",
        "terrain", "trees", "textures",
        "video", "voices",
    };

    // Extensions that always belong in Data (plugin files and archives)
    private static readonly HashSet<string> DataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".esp", ".esm", ".esl", ".bsa", ".ba2",
    };

    // Subfolders that live in the game root, not Data
    private static readonly HashSet<string> RootSubfolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "enbseries", "reshade", "reshade-shaders",
    };

    // Extensions that, when found at the archive top level, belong in the game root
    private static readonly HashSet<string> RootTopLevelExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe",
    };

    // Specific file names that always go in the game root
    private static readonly HashSet<string> RootFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "enblocal.ini", "enbseries.ini",
        "reshade.ini", "reshade.log",
        "skse64_loader.exe", "skse_loader.exe",
    };

    private readonly IModManagerState _state;

    public BethesdaModInstaller(IModManagerState state) => _state = state;

    public bool CanInstall(string archivePath) =>
        BethesdaDetector.IsBethesdaExecutable(_state.GameExecutablePath) &&
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
                var parts    = srcPath.Split('/');
                var topDir   = parts[0];
                var fileName = parts[^1];
                var ext      = Path.GetExtension(fileName);
                var isAtRoot = parts.Length == 1;

                string destPath;

                if (topDir.Equals("Data", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
                {
                    // Rule 1: strip "Data/" prefix — VFS mounts the mod root AS Data/
                    destPath = string.Join("/", parts.Skip(1));
                }
                else if (DataExtensions.Contains(ext))
                {
                    // Rule 2: plugin or BSA file → always Data
                    destPath = fileName;
                }
                else if (DataSubfolders.Contains(topDir))
                {
                    // Rule 3: well-known Data subfolder
                    destPath = srcPath;
                }
                else if (RootSubfolders.Contains(topDir))
                {
                    // Rule 4: known game-root subfolder (ENBSeries, ReShade, …)
                    destPath = "Root/" + srcPath;
                }
                else if (isAtRoot && RootTopLevelExtensions.Contains(ext))
                {
                    // Rule 5: DLL / EXE at archive root → game root
                    destPath = "Root/" + fileName;
                }
                else if (isAtRoot && RootFileNames.Contains(fileName))
                {
                    // Rule 6: known game-root config / loader
                    destPath = "Root/" + fileName;
                }
                else
                {
                    // Rule 7: default → treat as Data content
                    destPath = srcPath;
                }

                mapping[destPath] = srcPath;
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

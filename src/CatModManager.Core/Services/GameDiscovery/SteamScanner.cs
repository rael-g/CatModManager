using System;
using System.Collections.Generic;
using System.IO;
using CatModManager.PluginSdk;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;

namespace CatModManager.Core.Services.GameDiscovery;

public class SteamScanner : IGameScanner
{
    private readonly IFileService _fileService;
    private readonly IRegistryService _registryService;

    public SteamScanner(IFileService fileService, IRegistryService registryService)
    {
        _fileService = fileService;
        _registryService = registryService;
    }

    public string PlatformName => "Steam";
    public string StoreName => "Steam";

    public IEnumerable<GameInstallationInfo> Scan(CancellationToken ct)
    {
        var results = new List<GameInstallationInfo>();
        foreach (var libraryRoot in GetLibraryRoots())
        {
            ct.ThrowIfCancellationRequested();
            var appsPath = Path.Combine(libraryRoot, "steamapps");
            if (!_fileService.DirectoryExists(appsPath)) continue;

            foreach (var acf in _fileService.GetFiles(appsPath, "appmanifest_*.acf"))
            {
                ct.ThrowIfCancellationRequested();
                var content    = _fileService.ReadAllText(acf);
                var appIdStr   = Extract(content, "appid");
                var name       = Extract(content, "name") ?? "Unknown";
                var installDir = Extract(content, "installdir");
                var stateFlags = Extract(content, "StateFlags");
                var sizeOnDisk = Extract(content, "SizeOnDisk");

                if (appIdStr == null || installDir == null) continue;
                if (!uint.TryParse(appIdStr, out var appId)) continue;

                if (stateFlags != null && int.TryParse(stateFlags, out var flags) && (flags & 4) == 0)
                    continue;

                if (sizeOnDisk != null && long.TryParse(sizeOnDisk, out var size) && size < 50L * 1024 * 1024)
                    continue;

                var commonPath = Path.Combine(libraryRoot, "steamapps", "common");
                var gameFolder = Path.GetFullPath(Path.Combine(commonPath, installDir));
                
                results.Add(new GameInstallationInfo(name, string.Empty, gameFolder, "Steam", appId));
            }
        }
        return results;
    }

    private IEnumerable<string> GetLibraryRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in GetSteamInstallCandidates())
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            string norm;
            try { norm = Normalize(candidate); } catch { continue; }

            if (!_fileService.DirectoryExists(Path.Combine(norm, "steamapps"))) continue;
            if (!seen.Add(norm)) continue;

            yield return norm;

            var vdf = Path.Combine(norm, "steamapps", "libraryfolders.vdf");
            if (!_fileService.FileExists(vdf)) continue;

            string text;
            try { text = _fileService.ReadAllText(vdf); } catch { continue; }

            foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
            {
                string libNorm;
                try { libNorm = Normalize(m.Groups[1].Value.Replace("\\\\", "\\")); } catch { continue; }
                if (seen.Add(libNorm))
                    yield return libNorm;
            }
        }
    }

    /// <summary>
    /// Absolute path with symlinks resolved. The Linux candidates deliberately overlap
    /// (~/.steam/steam is usually a symlink to ~/.local/share/Steam), so without resolving them the
    /// same library would be reported two or three times under different names.
    /// </summary>
    private static string Normalize(string path)
    {
        string full = Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));

        try { return Directory.ResolveLinkTarget(full, returnFinalTarget: true)?.FullName ?? full; }
        catch { return full; }
    }

    /// <summary>
    /// Where a Steam client install may live. Only this differs per platform — appmanifest parsing
    /// and libraryfolders.vdf are identical everywhere.
    /// </summary>
    private IEnumerable<string?> GetSteamInstallCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return _registryService.GetCurrentUserValue(@"Software\Valve\Steam", "SteamPath");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam");
            yield break;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) yield break;

        // ~/.steam/steam and ~/.steam/root are symlinks into one of the others; they're resolved and
        // deduplicated by the caller, so listing them costs nothing and covers older layouts.
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".steam", "root");
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, ".steam", "debian-installation");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
    }

    private static string? Extract(string content, string key)
    {
        var m = Regex.Match(content, $"\"{Regex.Escape(key)}\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
}

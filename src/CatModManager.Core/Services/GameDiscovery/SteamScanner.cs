using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;

namespace CatModManager.Core.Services.GameDiscovery;

/// <summary>Scans Steam libraries to find installed games.</summary>
public class SteamScanner : IGameScanner
{
    public string PlatformName => "Steam";

    public IEnumerable<GameInstallationInfo> Scan(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<GameInstallationInfo>();

        var results = new List<GameInstallationInfo>();
        foreach (var libraryRoot in GetLibraryRoots())
        {
            ct.ThrowIfCancellationRequested();
            var appsPath = Path.Combine(libraryRoot, "steamapps");
            if (!Directory.Exists(appsPath)) continue;

            foreach (var acf in Directory.GetFiles(appsPath, "appmanifest_*.acf"))
            {
                ct.ThrowIfCancellationRequested();
                var content    = File.ReadAllText(acf);
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
                
                // Note: We don't find the EXE here yet, GameDiscoveryService handles the heuristic.
                results.Add(new GameInstallationInfo(name, string.Empty, gameFolder, "Steam", appId));
            }
        }
        return results;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> GetLibraryRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string?> { GetSteamPath() };
        candidates.AddRange(GetWellKnownSteamPaths());

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            string norm;
            try { norm = Normalize(candidate); } catch { continue; }

            if (!Directory.Exists(Path.Combine(norm, "steamapps"))) continue;
            if (!seen.Add(norm)) continue;

            yield return norm;

            var vdf = Path.Combine(norm, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;

            string text;
            try { text = File.ReadAllText(vdf); } catch { continue; }

            foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
            {
                string libNorm;
                try { libNorm = Normalize(m.Groups[1].Value.Replace("\\\\", "\\")); } catch { continue; }
                if (seen.Add(libNorm))
                    yield return libNorm;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> GetWellKnownSteamPaths()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles    = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        yield return Path.Combine(programFilesX86, "Steam");
        yield return Path.Combine(programFiles,    "Steam");
    }

    private static string Normalize(string path)
        => Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));

    [SupportedOSPlatform("windows")]
    private static string? GetSteamPath()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") as string;
        }
        catch { return null; }
    }

    private static string? Extract(string content, string key)
    {
        var m = Regex.Match(content, $"\"{Regex.Escape(key)}\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
}

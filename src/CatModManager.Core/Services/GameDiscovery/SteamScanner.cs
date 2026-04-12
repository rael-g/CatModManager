using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace CatModManager.Core.Services.GameDiscovery;

/// <summary>Scans Steam libraries to find installed games.</summary>
public static class SteamScanner
{
    [SupportedOSPlatform("windows")]
    public static IEnumerable<(int AppId, string Name, string InstallDir, string CommonPath)> GetInstalledApps()
    {
        foreach (var libraryRoot in GetLibraryRoots())
        {
            var commonPath = Path.Combine(libraryRoot, "steamapps", "common");
            var appsPath   = Path.Combine(libraryRoot, "steamapps");
            if (!Directory.Exists(appsPath)) continue;

            foreach (var acf in Directory.GetFiles(appsPath, "appmanifest_*.acf"))
            {
                var content    = File.ReadAllText(acf);
                var appId      = Extract(content, "appid");
                var name       = Extract(content, "name") ?? "Unknown";
                var installDir = Extract(content, "installdir");
                var stateFlags = Extract(content, "StateFlags");
                var sizeOnDisk = Extract(content, "SizeOnDisk");

                if (appId == null || installDir == null) continue;
                if (!int.TryParse(appId, out var id)) continue;

                // StateFlags bit 2 (value 4) = fully installed.
                if (stateFlags != null && int.TryParse(stateFlags, out var flags) && (flags & 4) == 0)
                    continue;

                // SizeOnDisk = 0 means the game was never actually downloaded.
                // Require at least 50 MB to rule out ghost entries from managers like Hydra.
                if (sizeOnDisk != null && long.TryParse(sizeOnDisk, out var size) && size < 50L * 1024 * 1024)
                    continue;

                yield return (id, name, installDir, commonPath);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> GetLibraryRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Seed candidates: registry-reported Steam path + well-known default locations.
        // This handles setups where a modded Steam client (e.g. SteamVerde) overwrites the
        // registry entry, making the original Steam installation invisible to a registry-only scan.
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

            // Also yield any additional library folders listed in this installation's VDF.
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

    /// <summary>
    /// Returns well-known Steam installation paths to use as fallbacks when the registry
    /// points to a different Steam client (e.g. SteamVerde, Steam++ variants).
    /// </summary>
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
            using var key = Microsoft.Win32.Registry.CurrentUser
                                .OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") as string;
        }
        catch { return null; }
    }

    private static string? Extract(string content, string key)
    {
        var m = Regex.Match(content, $"\"{Regex.Escape(key)}\"\\s+\"([^\"]+)\"",
                            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace CatModManager.Core.Services.GameDiscovery;

/// <summary>Scans Steam libraries to find installed games.</summary>
public static class SteamScanner
{
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

    private static IEnumerable<string> GetLibraryRoots()
    {
        var steamPath = GetSteamPath();
        if (steamPath == null) yield break;

        var normalizedSteam = Normalize(steamPath);
        yield return normalizedSteam;

        var vdf = Path.Combine(normalizedSteam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        var text = File.ReadAllText(vdf);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalizedSteam };

        foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
        {
            var norm = Normalize(m.Groups[1].Value.Replace("\\\\", "\\"));
            if (seen.Add(norm))
                yield return norm;
        }
    }

    private static string Normalize(string path)
        => Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));

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

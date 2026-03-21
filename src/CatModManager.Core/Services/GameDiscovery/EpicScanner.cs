using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CatModManager.Core.Services.GameDiscovery;

/// <summary>Scans Epic Games Launcher manifests to find installed games.</summary>
public static class EpicScanner
{
    private static readonly string ManifestsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "EpicGamesLauncher", "Data", "Manifests");

    public static IEnumerable<(string ExecutablePath, string InstallFolder, string Name)> GetInstalledGames()
    {
        if (!Directory.Exists(ManifestsPath)) yield break;

        foreach (var item in Directory.GetFiles(ManifestsPath, "*.item"))
        {
            string? installLocation = null;
            string? launchExe       = null;
            string? displayName     = null;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(item));
                var root = doc.RootElement;

                installLocation = root.TryGetProperty("InstallLocation",  out var il) ? il.GetString() : null;
                launchExe       = root.TryGetProperty("LaunchExecutable", out var le) ? le.GetString() : null;
                displayName     = root.TryGetProperty("DisplayName",      out var dn) ? dn.GetString() : null;
            }
            catch { continue; }

            if (string.IsNullOrEmpty(installLocation) || string.IsNullOrEmpty(launchExe)) continue;

            var exePath = Path.Combine(installLocation, launchExe);
            yield return (exePath, installLocation, displayName ?? Path.GetFileNameWithoutExtension(launchExe));
        }
    }
}

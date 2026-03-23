using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CatModManager.Core.Services.GameDiscovery;

/// <summary>Scans the Windows registry for GOG Galaxy game installations.</summary>
public static class GogScanner
{
    private const string GogGamesKey = @"SOFTWARE\WOW6432Node\GOG.com\Games";

    [SupportedOSPlatform("windows")]
    public static IEnumerable<(string ExecutablePath, string InstallFolder, string Name)> GetInstalledGames()
    {
        RegistryKey? root = null;
        try { root = Registry.LocalMachine.OpenSubKey(GogGamesKey); }
        catch { yield break; }

        if (root == null) yield break;

        foreach (var subName in root.GetSubKeyNames())
        {
            RegistryKey? sub = null;
            try { sub = root.OpenSubKey(subName); }
            catch { continue; }

            if (sub == null) continue;

            var exe    = sub.GetValue("exe")      as string;
            var folder = sub.GetValue("path")     as string;
            var name   = sub.GetValue("gameName") as string ?? sub.GetValue("GAMENAME") as string ?? "Unknown";
            sub.Dispose();

            if (!string.IsNullOrEmpty(exe) && !string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                yield return (exe, folder, name);
        }

        root.Dispose();
    }
}

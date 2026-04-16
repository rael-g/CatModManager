using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Win32;

namespace CatModManager.Core.Services.GameDiscovery;

public class GogScanner : IGameScanner
{
    private const string GogGamesKey = @"SOFTWARE\WOW6432Node\GOG.com\Games";
    public string PlatformName => "GOG";

    public IEnumerable<GameInstallationInfo> Scan(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<GameInstallationInfo>();

        var results = new List<GameInstallationInfo>();
        RegistryKey? root = null;
        try { root = Registry.LocalMachine.OpenSubKey(GogGamesKey); }
        catch { return results; }

        if (root == null) return results;

        foreach (var subName in root.GetSubKeyNames())
        {
            ct.ThrowIfCancellationRequested();
            RegistryKey? sub = null;
            try { sub = root.OpenSubKey(subName); }
            catch { continue; }

            if (sub == null) continue;

            var exe    = sub.GetValue("exe")      as string;
            var folder = sub.GetValue("path")     as string;
            var name   = sub.GetValue("gameName") as string ?? sub.GetValue("GAMENAME") as string ?? "Unknown";
            sub.Dispose();

            if (!string.IsNullOrEmpty(exe) && !string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                results.Add(new GameInstallationInfo(name, exe, folder, "GOG"));
            }
        }

        root.Dispose();
        return results;
    }
}

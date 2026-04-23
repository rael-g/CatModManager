using System;
using System.Collections.Generic;
using System.IO;
using CatModManager.PluginSdk;
using System.Text.Json;
using System.Threading;

namespace CatModManager.Core.Services.GameDiscovery;

public class EpicScanner : IGameScanner
{
    private readonly IFileService _fileService;
    private static readonly string ManifestsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "EpicGamesLauncher", "Data", "Manifests");

    public EpicScanner(IFileService fileService)
    {
        _fileService = fileService;
    }

    public string StoreName => "Epic";

    public IEnumerable<GameInstallationInfo> Scan(CancellationToken ct)
    {
        if (!_fileService.DirectoryExists(ManifestsPath)) return Array.Empty<GameInstallationInfo>();

        var results = new List<GameInstallationInfo>();
        foreach (var item in _fileService.GetFiles(ManifestsPath, "*.item"))
        {
            ct.ThrowIfCancellationRequested();
            string? installLocation = null;       
            string? launchExe       = null;       
            string? displayName     = null;       

            try
            {
                var content = _fileService.ReadAllText(item);
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;       

                installLocation = root.TryGetProperty("InstallLocation",  out var il) ? il.GetString() : null;
                launchExe       = root.TryGetProperty("LaunchExecutable", out var le) ? le.GetString() : null;
                displayName     = root.TryGetProperty("DisplayName",      out var dn) ? dn.GetString() : null;
            }
            catch { continue; }

            if (string.IsNullOrEmpty(installLocation) || string.IsNullOrEmpty(launchExe)) continue; 

            var exePath = Path.Combine(installLocation, launchExe);
            results.Add(new GameInstallationInfo(displayName ?? Path.GetFileNameWithoutExtension(launchExe), exePath, installLocation, "Epic"));
        }
        return results;
    }
}

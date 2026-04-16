using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;

namespace CatModManager.Core.Services.GameDiscovery;

public class GogScanner : IGameScanner
{
    private readonly IRegistryService _registryService;
    private readonly IFileService _fileService;
    private const string GogGamesKey = @"SOFTWARE\WOW6432Node\GOG.com\Games";

    public GogScanner(IRegistryService registryService, IFileService fileService)
    {
        _registryService = registryService;
        _fileService = fileService;
    }

    public string StoreName => "GOG";

    public IEnumerable<GameInstallationInfo> Scan(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<GameInstallationInfo>();

        var results = new List<GameInstallationInfo>();
        var subKeys = _registryService.GetLocalMachineSubKeys(GogGamesKey);

        foreach (var subName in subKeys)
        {
            ct.ThrowIfCancellationRequested();

            var exe    = _registryService.GetLocalMachineSubKeyValue(GogGamesKey, subName, "exe");
            var folder = _registryService.GetLocalMachineSubKeyValue(GogGamesKey, subName, "path");
            var name   = _registryService.GetLocalMachineSubKeyValue(GogGamesKey, subName, "gameName") 
                      ?? _registryService.GetLocalMachineSubKeyValue(GogGamesKey, subName, "GAMENAME") 
                      ?? "Unknown";

            if (!string.IsNullOrEmpty(exe) && !string.IsNullOrEmpty(folder) && _fileService.DirectoryExists(folder))
            {
                results.Add(new GameInstallationInfo(name, exe, folder, "GOG"));
            }
        }

        return results;
    }
}

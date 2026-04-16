using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CatModManager.Core.Services.GameDiscovery;

public class GameDiscoveryService : IGameDiscoveryService
{
    private const long MinExeSizeBytes = 512 * 1024; // 512 KB

    private static readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "UnityCrashHandler64.exe", "UnityCrashHandler32.exe",
        "UE4PrereqSetup_x64.exe", "UE4PrereqSetup_x86.exe",
        "UE5PrereqSetup_x64.exe",
        "DXSETUP.exe", "dxsetup.exe",
        "vcredist_x64.exe", "vcredist_x86.exe",
        "dotnet.exe", "dotnetfx.exe",
        "crashpad_handler.exe", "CrashReportClient.exe",
        "EpicInstaller.exe", "EasyAntiCheat.exe", "EasyAntiCheat_EOS.exe",
        "BattlEye.exe", "BEService.exe",
        "GameOverlayUI.exe", "steam.exe",
        "installerw.exe", "unins000.exe",
    };

    private readonly IGameSupportService _gameSupportService;
    private readonly IEnumerable<IGameScanner> _scanners;

    public GameDiscoveryService(IGameSupportService gameSupportService, IEnumerable<IGameScanner> scanners)
    {
        _gameSupportService = gameSupportService;
        _scanners = scanners;
    }

    public Task<IReadOnlyList<GameInstallation>> ScanAsync(CancellationToken ct = default)
        => Task.Run(() => Scan(ct), ct);

    private IReadOnlyList<GameInstallation> Scan(CancellationToken ct)
    {
        var results = new List<GameInstallation>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var supports = _gameSupportService.GetAllSupports()
                           .Where(s => s.GameId != "generic")
                           .ToList();

        var bySteamId = supports
            .Where(s => s.SteamAppId > 0)
            .GroupBy(s => s.SteamAppId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var scanner in _scanners)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var info in scanner.Scan(ct))
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(info.InstallDir) || !Directory.Exists(info.InstallDir)) continue;
                    if (!seenFolders.Add(info.InstallDir)) continue;

                    IGameSupport? knownSupport = null;
                    if (info.StoreName == "Steam" && info.AppId.HasValue)
                        knownSupport = bySteamId.GetValueOrDefault((int)info.AppId.Value);

                    var exe = info.ExePath;
                    if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                        exe = FindExe(info.InstallDir, knownSupport, info.Name);

                    if (string.IsNullOrEmpty(exe) || !seenExes.Add(exe)) continue;

                    var detected = knownSupport ?? supports.FirstOrDefault(s => s.CanSupport(exe));
                    results.Add(new GameInstallation(info.Name, exe, info.InstallDir, info.StoreName, detected));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* Log error via event bus if needed */ }
        }

        return results.OrderBy(r => r.DisplayName).ToList();
    }

    private static string? FindExe(string gameFolder, IGameSupport? knownSupport, string gameName)
    {
        if (knownSupport != null)
        {
            var rel = knownSupport.RequiredFiles
                .FirstOrDefault(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (rel != null)
            {
                var full = Path.GetFullPath(Path.Combine(gameFolder, rel));
                if (File.Exists(full)) return full;
            }
        }

        string[] candidates;
        try { candidates = Directory.GetFiles(gameFolder, "*.exe", SearchOption.TopDirectoryOnly); }
        catch { return null; }

        var nonExcluded = candidates
            .Where(e => !_excluded.Contains(Path.GetFileName(e)))
            .ToList();

        var valid = nonExcluded
            .Where(e => new FileInfo(e).Length >= MinExeSizeBytes)
            .ToList();

        var pool = valid.Count > 0 ? valid : nonExcluded;
        if (pool.Count == 0) return null;
        if (pool.Count == 1) return pool[0];

        var nameToken = Path.GetFileNameWithoutExtension(
            gameName.Replace(" ", "").Replace(":", "").Replace("'", ""));

        var byName = pool.FirstOrDefault(e =>
            Path.GetFileNameWithoutExtension(e)
                .Contains(nameToken, StringComparison.OrdinalIgnoreCase));
        if (byName != null) return byName;

        return pool
            .OrderByDescending(e => new FileInfo(e).Length)
            .First();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CatModManager.Core.Services.GameDiscovery;

public class GameDiscoveryService : IGameDiscoveryService
{
    // Minimum size (bytes) for a file to be considered a real game executable.
    private const long MinExeSizeBytes = 512 * 1024; // 512 KB

    // Executables that are never the main game binary.
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

    public GameDiscoveryService(IGameSupportService gameSupportService)
        => _gameSupportService = gameSupportService;

    public Task<IReadOnlyList<GameInstallation>> ScanAsync(CancellationToken ct = default)
        => Task.Run(() => Scan(ct), ct);

    private IReadOnlyList<GameInstallation> Scan(CancellationToken ct)
    {
        var results       = new List<GameInstallation>();
        var seenFolders   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenExes      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var supports = _gameSupportService.GetAllSupports()
                           .Where(s => s.GameId != "generic")
                           .ToList();

        var bySteamId = supports
            .Where(s => s.SteamAppId > 0)
            .GroupBy(s => s.SteamAppId)
            .ToDictionary(g => g.Key, g => g.First());

        if (OperatingSystem.IsWindows())
        {
            try
            {
                foreach (var (appId, name, installDir, commonPath) in SteamScanner.GetInstalledApps())
                {
                    ct.ThrowIfCancellationRequested();

                    var gameFolder = Path.GetFullPath(Path.Combine(commonPath, installDir));
                    if (!Directory.Exists(gameFolder) || !seenFolders.Add(gameFolder)) continue;

                    IGameSupport? knownSupport = bySteamId.GetValueOrDefault(appId);
                    var exe = FindExe(gameFolder, knownSupport, name);
                    if (exe == null || !seenExes.Add(exe)) continue;

                    var detected = knownSupport ?? supports.FirstOrDefault(s => s.CanSupport(exe));

                    results.Add(new GameInstallation(name, exe, gameFolder, "Steam", detected));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        ct.ThrowIfCancellationRequested();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                foreach (var (exe, folder, name) in GogScanner.GetInstalledGames())
                {
                    ct.ThrowIfCancellationRequested();
                    if (!seenFolders.Add(folder) || !seenExes.Add(exe)) continue;

                    var detected = supports.FirstOrDefault(s => s.CanSupport(exe));
                    results.Add(new GameInstallation(name, exe, folder, "GOG", detected));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        ct.ThrowIfCancellationRequested();

        try
        {
            foreach (var (exe, folder, name) in EpicScanner.GetInstalledGames())
            {
                ct.ThrowIfCancellationRequested();
                if (!seenFolders.Add(folder) || !seenExes.Add(exe)) continue;

                var detected = supports.FirstOrDefault(s => s.CanSupport(exe));
                results.Add(new GameInstallation(name, exe, folder, "Epic", detected));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        return results.OrderBy(r => r.DisplayName).ToList();
    }

    /// <summary>
    /// Finds the main game executable using known support hints, name matching, and file size heuristics.
    /// </summary>
    private static string? FindExe(string gameFolder, IGameSupport? knownSupport, string gameName)
    {
        if (knownSupport != null)
        {
            var rel = knownSupport.RequiredFiles
                .FirstOrDefault(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (rel != null)
            {
                // RequiredFiles is an explicit hint from game support — skip size filter.
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

        // Fall back to the largest one if all root exes are small launchers, to avoid missing the game.
        var pool = valid.Count > 0 ? valid : nonExcluded;
        if (pool.Count == 0) return null;
        if (pool.Count == 1) return pool[0];

        var nameToken = Path.GetFileNameWithoutExtension(
            gameName.Replace(" ", "").Replace(":", "").Replace("'", ""));

        var byName = pool.FirstOrDefault(e =>
            Path.GetFileNameWithoutExtension(e)
                .Contains(nameToken, StringComparison.OrdinalIgnoreCase));
        if (byName != null) return byName;

        // Game binaries are typically the largest executables in the folder.
        return pool
            .OrderByDescending(e => new FileInfo(e).Length)
            .First();
    }
}

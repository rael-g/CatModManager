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

        _gameSupportService.RefreshSupports();
        var supports = _gameSupportService.GetAllSupports()
                           .Where(s => s.GameId != "generic")
                           .ToList();

        var bySteamId = supports
            .Where(s => s.SteamAppId > 0)
            .GroupBy(s => s.SteamAppId)
            .ToDictionary(g => g.Key, g => g.First());

        // ── Steam ──────────────────────────────────────────────────────────
        try
        {
            foreach (var (appId, name, installDir, commonPath) in SteamScanner.GetInstalledApps())
            {
                ct.ThrowIfCancellationRequested();

                var gameFolder = Path.GetFullPath(Path.Combine(commonPath, installDir));
                if (!Directory.Exists(gameFolder) || !seenFolders.Add(gameFolder)) continue;

                // Find the best exe for this game folder.
                IGameSupport? knownSupport = bySteamId.GetValueOrDefault(appId);
                var exe = FindExe(gameFolder, knownSupport, name);
                if (exe == null || !seenExes.Add(exe)) continue;

                // Auto-detect game support: first by AppId, then by CanSupport.
                var detected = knownSupport ?? supports.FirstOrDefault(s => s.CanSupport(exe));

                results.Add(new GameInstallation(name, exe, gameFolder, "Steam", detected));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* Steam not installed or unreadable */ }

        ct.ThrowIfCancellationRequested();

        // ── GOG ────────────────────────────────────────────────────────────
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
        catch { /* GOG not installed */ }

        ct.ThrowIfCancellationRequested();

        // ── Epic ───────────────────────────────────────────────────────────
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
        catch { /* Epic not installed */ }

        return results.OrderBy(r => r.DisplayName).ToList();
    }

    /// <summary>
    /// Finds the most likely main executable for a game folder.
    /// Priority: 1) known support's RequiredFiles .exe  2) exe matching the game name  3) largest non-excluded exe
    /// </summary>
    private static string? FindExe(string gameFolder, IGameSupport? knownSupport, string gameName)
    {
        // 1. Use the support's RequiredFiles if we already know the game.
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

        // 2. Scan root-level executables, exclude launchers/tools.
        string[] candidates;
        try { candidates = Directory.GetFiles(gameFolder, "*.exe", SearchOption.TopDirectoryOnly); }
        catch { return null; }

        var valid = candidates
            .Where(e => !_excluded.Contains(Path.GetFileName(e))
                     && new FileInfo(e).Length >= MinExeSizeBytes)
            .ToList();

        if (valid.Count == 0) return null;
        if (valid.Count == 1) return valid[0];

        // 3. Prefer the exe whose name is closest to the game/install-dir name.
        var nameToken = Path.GetFileNameWithoutExtension(
            gameName.Replace(" ", "").Replace(":", "").Replace("'", ""));

        var byName = valid.FirstOrDefault(e =>
            Path.GetFileNameWithoutExtension(e)
                .Contains(nameToken, StringComparison.OrdinalIgnoreCase));
        if (byName != null) return byName;

        // 4. Fall back to the largest file (heuristic: game binaries are big).
        return valid
            .OrderByDescending(e => new FileInfo(e).Length)
            .First();
    }
}

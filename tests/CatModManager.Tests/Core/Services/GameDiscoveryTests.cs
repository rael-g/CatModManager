using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using NSubstitute;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Core.Services;

namespace CatModManager.Tests.Core.Services;

public class GameDiscoveryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IGameSupportService _supportService;

    public GameDiscoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_Discovery_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _supportService = Substitute.For<IGameSupportService>();
        _supportService.GetAllSupports().Returns(Enumerable.Empty<IGameSupport>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task ScanAsync_CombinesResults_FromMultipleScanners()
    {
        // Setup dummy game files
        string steamPath = Path.Combine(_tempDir, "SteamGame");
        Directory.CreateDirectory(steamPath);
        string steamExe = Path.Combine(steamPath, "steam_game.exe");
        File.WriteAllBytes(steamExe, new byte[1024 * 1024]); // 1MB

        string gogPath = Path.Combine(_tempDir, "GogGame");
        Directory.CreateDirectory(gogPath);
        string gogExe = Path.Combine(gogPath, "gog_game.exe");
        File.WriteAllBytes(gogExe, new byte[1024 * 1024]);

        var steamScanner = Substitute.For<IGameScanner>();
        steamScanner.Scan(Arg.Any<CancellationToken>()).Returns(new[] { 
            new GameInstallationInfo("Steam Game", steamExe, steamPath, "Steam", 123) 
        });

        var gogScanner = Substitute.For<IGameScanner>();
        gogScanner.Scan(Arg.Any<CancellationToken>()).Returns(new[] { 
            new GameInstallationInfo("Gog Game", gogExe, gogPath, "GOG") 
        });

        var service = new GameDiscoveryService(_supportService, new[] { steamScanner, gogScanner });
        var results = await service.ScanAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.StoreName == "Steam");
        Assert.Contains(results, r => r.StoreName == "GOG");
    }

    [Fact]
    public async Task ScanAsync_UsesHeuristic_WhenExePathIsMissing()
    {
        string gamePath = Path.Combine(_tempDir, "HeuristicGame");
        Directory.CreateDirectory(gamePath);
        string gameExe = Path.Combine(gamePath, "MyGame.exe");
        File.WriteAllBytes(gameExe, new byte[1024 * 1024]);

        var scanner = Substitute.For<IGameScanner>();
        scanner.Scan(Arg.Any<CancellationToken>()).Returns(new[] { 
            new GameInstallationInfo("My Game", "", gamePath, "Test") 
        });

        var service = new GameDiscoveryService(_supportService, new[] { scanner });
        var results = await service.ScanAsync();

        Assert.Single(results);
        Assert.Equal(gameExe, results[0].ExecutablePath);
    }

    [Fact]
    public async Task ScanAsync_FiltersOut_SmallExecutables()
    {
        string gamePath = Path.Combine(_tempDir, "SmallExeGame");
        Directory.CreateDirectory(gamePath);
        
        string launcherExe = Path.Combine(gamePath, "launcher.exe");
        File.WriteAllBytes(launcherExe, new byte[10 * 1024]); // 10KB
        
        string realExe = Path.Combine(gamePath, "GameBinary.exe");
        File.WriteAllBytes(realExe, new byte[1024 * 1024]); // 1MB

        var scanner = Substitute.For<IGameScanner>();
        scanner.Scan(Arg.Any<CancellationToken>()).Returns(new[] { 
            new GameInstallationInfo("My Game", "", gamePath, "Test") 
        });

        var service = new GameDiscoveryService(_supportService, new[] { scanner });
        var results = await service.ScanAsync();

        Assert.Single(results);
        Assert.Equal(realExe, results[0].ExecutablePath);
    }
}

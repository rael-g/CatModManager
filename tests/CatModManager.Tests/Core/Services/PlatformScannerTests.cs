using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;
using NSubstitute;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;

namespace CatModManager.Tests.Core.Services;

public class PlatformScannerTests
{
    [Fact]
    public void SteamScanner_Detects_Games_From_ACF()
    {
        var fileService = Substitute.For<IFileService>();
        var registry = Substitute.For<IRegistryService>();

        registry.GetCurrentUserValue(Arg.Any<string>(), "SteamPath").Returns("C:\\Steam");
        fileService.DirectoryExists("C:\\Steam\\steamapps").Returns(true);
        fileService.GetFiles("C:\\Steam\\steamapps", "appmanifest_*.acf").Returns(new[] { "C:\\Steam\\steamapps\\appmanifest_123.acf" });
        
        fileService.ReadAllText("C:\\Steam\\steamapps\\appmanifest_123.acf").Returns(@"
""AppState""
{
    ""appid"" ""123""
    ""name"" ""Test Game""
    ""installdir"" ""TestGame""
    ""StateFlags"" ""4""
    ""SizeOnDisk"" ""100000000""
}");

        var scanner = new SteamScanner(fileService, registry);
        var results = scanner.Scan(CancellationToken.None).ToList();

        Assert.Single(results);
        Assert.Equal("Test Game", results[0].Name);
        Assert.Equal(123u, results[0].AppId);
    }

    [Fact]
    public void GogScanner_Detects_Games_From_Registry()
    {
        var fileService = Substitute.For<IFileService>();
        var registry = Substitute.For<IRegistryService>();

        string gogKey = @"SOFTWARE\WOW6432Node\GOG.com\Games";
        registry.GetLocalMachineSubKeys(gogKey).Returns(new[] { "12345" });
        registry.GetLocalMachineSubKeyValue(gogKey, "12345", "exe").Returns("game.exe");
        registry.GetLocalMachineSubKeyValue(gogKey, "12345", "path").Returns("C:\\GOG\\Game");
        registry.GetLocalMachineSubKeyValue(gogKey, "12345", "gameName").Returns("GOG Game");

        fileService.DirectoryExists("C:\\GOG\\Game").Returns(true);

        var scanner = new GogScanner(registry, fileService);
        var results = scanner.Scan(CancellationToken.None).ToList();

        Assert.Single(results);
        Assert.Equal("GOG Game", results[0].Name);
        Assert.Equal("C:\\GOG\\Game", results[0].InstallDir);
    }

    [Fact]
    public void EpicScanner_Detects_Games_From_Manifests()
    {
        var fileService = Substitute.For<IFileService>();
        
        // ManifestsPath is hardcoded in EpicScanner, we just mock the directory existence
        fileService.DirectoryExists(Arg.Any<string>()).Returns(true);
        fileService.GetFiles(Arg.Any<string>(), "*.item").Returns(new[] { "manifest.item" });
        
        fileService.ReadAllText("manifest.item").Returns(@"{
            ""InstallLocation"": ""C:\\Epic\\Game"",
            ""LaunchExecutable"": ""game.exe"",
            ""DisplayName"": ""Epic Game""
        }");

        var scanner = new EpicScanner(fileService);
        var results = scanner.Scan(CancellationToken.None).ToList();

        Assert.Single(results);
        Assert.Equal("Epic Game", results[0].Name);
        Assert.Equal("C:\\Epic\\Game", results[0].InstallDir);
    }
}

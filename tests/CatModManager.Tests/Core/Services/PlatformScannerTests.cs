using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;
using NSubstitute;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.PluginSdk;

namespace CatModManager.Tests.Core.Services;

public class PlatformScannerTests
{
    /// <summary>
    /// A Steam root the scanner will actually probe on the current OS, so these tests exercise the
    /// real candidate list instead of hardcoding a Windows path that can never match on Linux.
    /// </summary>
    private static string SteamRoot => OperatingSystem.IsWindows()
        ? "C:\\Steam"
        : System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Steam");

    private static string P(params string[] parts) => System.IO.Path.Combine(parts);

    [Fact]
    public void SteamScanner_Detects_Games_From_ACF()
    {
        var fileService = Substitute.For<IFileService>();
        var registry = Substitute.For<IRegistryService>();

        registry.GetCurrentUserValue(Arg.Any<string>(), "SteamPath").Returns(SteamRoot);
        fileService.DirectoryExists(P(SteamRoot, "steamapps")).Returns(true);
        fileService.GetFiles(P(SteamRoot, "steamapps"), "appmanifest_*.acf")
            .Returns(new[] { P(SteamRoot, "steamapps", "appmanifest_123.acf") });

        fileService.ReadAllText(P(SteamRoot, "steamapps", "appmanifest_123.acf")).Returns(@"
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
    public void SteamScanner_ScansLinuxSteamRoots_WithoutRegistry()
    {
        // Regression: Scan() used to bail out with an empty list on any non-Windows OS, so Linux
        // users never got auto-detection even though .acf parsing is entirely platform-agnostic.
        if (OperatingSystem.IsWindows()) return;

        var fileService = Substitute.For<IFileService>();
        var registry = Substitute.For<IRegistryService>();

        // No registry on Linux — discovery has to come from the well-known install locations.
        registry.GetCurrentUserValue(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);

        string root = P(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".local", "share", "Steam");

        fileService.DirectoryExists(P(root, "steamapps")).Returns(true);
        fileService.GetFiles(P(root, "steamapps"), "appmanifest_*.acf")
            .Returns(new[] { P(root, "steamapps", "appmanifest_1716740.acf") });
        fileService.ReadAllText(P(root, "steamapps", "appmanifest_1716740.acf")).Returns(@"
""AppState""
{
    ""appid"" ""1716740""
    ""name"" ""Starfield""
    ""installdir"" ""Starfield""
    ""StateFlags"" ""4""
    ""SizeOnDisk"" ""100000000000""
}");

        var results = new SteamScanner(fileService, registry).Scan(CancellationToken.None).ToList();

        Assert.Single(results);
        Assert.Equal("Starfield", results[0].Name);
        Assert.Equal(1716740u, results[0].AppId);
        Assert.Equal(P(root, "steamapps", "common", "Starfield"), results[0].InstallDir);
    }

    [Fact]
    public void GogScanner_Detects_Games_From_Registry()
    {
        // GOG discovery reads the Windows registry; GOG Galaxy has no Linux client.
        if (!OperatingSystem.IsWindows()) return;

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

using System;
using Xunit;
using NSubstitute;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Services;

namespace CatModManager.Tests.Plugins.BethesdaTools;

public class BethesdaDetectorTests
{
    [Fact]
    public void Detect_DirectMatch_Works()
    {
        var fileService = Substitute.For<IFileService>();
        var detector = new BethesdaDetector(fileService);

        var game = detector.Detect("C:\\Games\\Skyrim Special Edition\\SkyrimSE.exe");

        Assert.NotNull(game);
        Assert.Equal("Skyrim Special Edition", game!.LocalAppDataFolder);
        Assert.True(game.UsesStarFormat);
    }

    [Fact]
    public void Detect_FallbackScan_Works()
    {
        var fileService = Substitute.For<IFileService>();
        var detector = new BethesdaDetector(fileService);

        // Assume user points to a launcher like SKSE
        string launcherPath = "C:\\Games\\Skyrim\\skse_loader.exe";
        string actualExePath = "C:\\Games\\Skyrim\\TESV.exe";

        fileService.FileExists(actualExePath).Returns(true);

        var game = detector.Detect(launcherPath);

        Assert.NotNull(game);
        Assert.Equal("Skyrim", game!.LocalAppDataFolder);
        Assert.False(game.UsesStarFormat);
    }

    [Fact]
    public void IsBethesdaExecutable_ReturnsCorrectResult()
    {
        var fileService = Substitute.For<IFileService>();
        var detector = new BethesdaDetector(fileService);

        Assert.True(detector.IsBethesdaExecutable("Starfield.exe"));
        Assert.False(detector.IsBethesdaExecutable("NotAGame.exe"));
    }
}

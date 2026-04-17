using System;
using System.IO;
using System.Linq;
using Xunit;
using NSubstitute;
using CatModManager.PluginSdk;
using CmmPlugin.SaveManager.Services;
using CmmPlugin.SaveManager.Models;

namespace CatModManager.Tests.Plugins.SaveManager;

public class SaveDetectorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IPluginLogger _log;

    public SaveDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_SaveDetect_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _log = Substitute.For<IPluginLogger>();
    }

    public void Dispose() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }

    [Fact]
    public void Load_ReadsTomls_WithSaveFolderPattern()
    {
        string defsDir = Path.Combine(_tempDir, "game_definitions");
        Directory.CreateDirectory(defsDir);
        
        File.WriteAllText(Path.Combine(defsDir, "test.toml"), @"
GameId = ""TestGame""
DisplayName = ""Test Game""
RequiredFiles = [""game.exe""]
SaveFolderPattern = ""C:\\Saves\\Test""
");

        var detector = new SaveDetector(_log);
        detector.Load(_tempDir);

        Assert.True(detector.Count >= 1);
        var def = detector.Detect("C:\\Games\\game.exe");
        Assert.NotNull(def);
        Assert.Equal("TestGame", def!.GameId);
        Assert.Equal("C:\\Saves\\Test", def.SaveFolderPattern);
    }

    [Fact]
    public void ResolveSaveFolder_ExpandsVariables_And_HandlesWildcard()
    {
        string baseDir = Path.Combine(_tempDir, "SteamSaves");
        string userDir = Path.Combine(baseDir, "123456");
        Directory.CreateDirectory(userDir);

        var def = new SaveGameDef { SaveFolderPattern = baseDir + "\\*" };
        
        string? resolved = SaveDetector.ResolveSaveFolder(def);
        
        Assert.Equal(userDir, resolved);
    }
}

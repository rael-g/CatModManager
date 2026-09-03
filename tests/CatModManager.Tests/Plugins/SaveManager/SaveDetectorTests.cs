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

    private SaveDetector NewDetector() =>
        new(_log, new WindowsUserFolders(new PhysicalFileService(), _log));

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

        var detector = NewDetector();
        detector.Load(_tempDir);

        Assert.True(detector.Count >= 1);
        var def = detector.Detect(Path.Combine("Games", "game.exe"));
        Assert.NotNull(def);
        Assert.Equal("TestGame", def!.GameId);
        Assert.Equal("C:\\Saves\\Test", def.SaveFolderPattern);
    }

    /// <summary>
    /// The shipped definitions all wrote SaveFolderPattern at the end of the file, after
    /// [[MountPoints]], which in TOML nests it inside that mount point instead of the game. The
    /// original test wrote its fixture with the key at the root, so it passed while every real
    /// definition was being skipped and the plugin logged "0 save-managed game(s)" forever.
    /// </summary>
    [Fact]
    public void Load_FindsThePattern_EvenWhenItIsWrittenAfterTheMountPoints()
    {
        string defsDir = Path.Combine(_tempDir, "game_definitions");
        Directory.CreateDirectory(defsDir);

        File.WriteAllText(Path.Combine(defsDir, "misplaced.toml"), @"
GameId = ""Misplaced""
RequiredFiles = [""game.exe""]

[[MountPoints]]
Id   = ""default""
Path = """"

SaveFolderPattern = ""%APPDATA%\\Misplaced""
");

        var detector = NewDetector();
        detector.Load(_tempDir);

        var def = detector.Detect(Path.Combine("Games", "game.exe"));
        Assert.NotNull(def);
        Assert.Equal(@"%APPDATA%\Misplaced", def!.SaveFolderPattern);
    }

    /// <summary>
    /// Guards the real files rather than a fixture. A definition whose save pattern lands in the
    /// wrong table parses fine and reads fine to a human — the only symptom is the game quietly
    /// vanishing from the Save Manager, which no synthetic test would notice.
    /// </summary>
    [Fact]
    public void Load_DetectsSaveManagedGames_FromTheShippedDefinitions()
    {
        var detector = NewDetector();
        detector.Load(_tempDir);   // _tempDir has no definitions; this loads the bundled ones

        Assert.True(detector.Count > 0,
            "No shipped game definition exposes a SaveFolderPattern — check that the key sits " +
            "above the first [[MountPoints]] header in samples/game_definitions/*.toml.");

        var eldenRing = detector.Detect("eldenring.exe");
        Assert.NotNull(eldenRing);
        Assert.Equal(@"%APPDATA%\EldenRing\*", eldenRing!.SaveFolderPattern);
    }

    [Fact]
    public void ResolveSaveFolder_ExpandsVariables_And_HandlesWildcard()
    {
        string baseDir = Path.Combine(_tempDir, "SteamSaves");
        string userDir = Path.Combine(baseDir, "123456");
        Directory.CreateDirectory(userDir);

        var def = new SaveGameDef { SaveFolderPattern = baseDir + "\\*" };
        
        string? resolved = NewDetector().ResolveSaveFolder(def);
        
        Assert.Equal(userDir, resolved);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;

namespace CatModManager.Tests;

public class RootSwapServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppDatabase _db;
    private readonly MockLogService _logService = new();
    private readonly RootSwapService _service;

    public RootSwapServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_RootSwap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        
        var pathService = new MockCatPathService(_tempDir);
        _db = new AppDatabase(pathService);
        _service = new RootSwapService(_db, _logService);
    }

    [Fact]
    public async Task DeployAsync_MovesFilesFromRootFolder()
    {
        string gameFolder = Path.Combine(_tempDir, "Game");
        Directory.CreateDirectory(gameFolder);
        string modFolder = Path.Combine(_tempDir, "Mod1");
        string modRoot = Path.Combine(modFolder, "Root");
        Directory.CreateDirectory(modRoot);
        File.WriteAllText(Path.Combine(modRoot, "root_file.txt"), "mod content");

        var mods = new List<Mod> { new Mod("Mod1", modFolder, 1) };
        await _service.DeployAsync(mods, gameFolder);

        Assert.True(File.Exists(Path.Combine(gameFolder, "root_file.txt")));
    }

    [Fact]
    public async Task UndeployAsync_RestoresOriginalFiles()
    {
        string gameFolder = Path.Combine(_tempDir, "GameRestore");
        Directory.CreateDirectory(gameFolder);
        string originalFile = Path.Combine(gameFolder, "original.txt");
        File.WriteAllText(originalFile, "original content");

        string modFolder = Path.Combine(_tempDir, "ModRestore");
        string modRoot = Path.Combine(modFolder, "Root");
        Directory.CreateDirectory(modRoot);
        File.WriteAllText(Path.Combine(modRoot, "original.txt"), "mod content");

        var mods = new List<Mod> { new Mod("ModRestore", modFolder, 1) };
        await _service.DeployAsync(mods, gameFolder);
        await _service.UndeployAsync(gameFolder);

        Assert.Equal("original content", File.ReadAllText(originalFile));
    }

    [Fact]
    public async Task UndeployAsync_RemovesMovedModFiles()
    {
        string gameFolder = Path.Combine(_tempDir, "GameCleanup");
        Directory.CreateDirectory(gameFolder);
        string modFolder = Path.Combine(_tempDir, "ModCleanup");
        string modRoot = Path.Combine(modFolder, "Root");
        Directory.CreateDirectory(modRoot);
        File.WriteAllText(Path.Combine(modRoot, "new_file.txt"), "mod content");

        var mods = new List<Mod> { new Mod("ModCleanup", modFolder, 1) };
        await _service.DeployAsync(mods, gameFolder);
        await _service.UndeployAsync(gameFolder);

        Assert.False(File.Exists(Path.Combine(gameFolder, "new_file.txt")));
    }

    [Fact]
    public void HasDeployedFiles_ReturnsTrue_WhenEntriesExist()
    {
        string gameFolder = Path.Combine(_tempDir, "GameStatus");
        Directory.CreateDirectory(gameFolder);
        // We'll manually insert an entry to avoid full deploy cycle if possible, or just deploy
        
        // Using deploy for simplicity
        string modFolder = Path.Combine(_tempDir, "ModStatus");
        string modRoot = Path.Combine(modFolder, "Root");
        Directory.CreateDirectory(modRoot);
        File.WriteAllText(Path.Combine(modRoot, "test.txt"), "content");
        
        _service.DeployAsync(new[] { new Mod("Mod", modFolder, 1) }, gameFolder).GetAwaiter().GetResult();

        Assert.True(_service.HasDeployedFiles(gameFolder));
    }

    [Fact]
    public async Task UndeployModAsync_OnlyRestoresSpecifiedMod()
    {
        string gameFolder = Path.Combine(_tempDir, "GamePartial");
        Directory.CreateDirectory(gameFolder);
        
        string mod1Folder = Path.Combine(_tempDir, "ModP1");
        string mod1Root = Path.Combine(mod1Folder, "Root");
        Directory.CreateDirectory(mod1Root);
        File.WriteAllText(Path.Combine(mod1Root, "f1.txt"), "c1");

        string mod2Folder = Path.Combine(_tempDir, "ModP2");
        string mod2Root = Path.Combine(mod2Folder, "Root");
        Directory.CreateDirectory(mod2Root);
        File.WriteAllText(Path.Combine(mod2Root, "f2.txt"), "c2");

        var mods = new List<Mod> { new Mod("M1", mod1Folder, 1), new Mod("M2", mod2Folder, 2) };
        await _service.DeployAsync(mods, gameFolder);
        
        await _service.UndeployModAsync(mod1Folder, gameFolder);

        Assert.True(File.Exists(Path.Combine(gameFolder, "f2.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}

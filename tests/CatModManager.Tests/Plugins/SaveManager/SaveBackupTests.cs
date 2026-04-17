using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using NSubstitute;
using CatModManager.PluginSdk;
using CmmPlugin.SaveManager.Services;
using CmmPlugin.SaveManager.Models;

namespace CatModManager.Tests.Plugins.SaveManager;

public class SaveBackupTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IPluginLogger _log;

    public SaveBackupTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_SaveBackup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _log = Substitute.For<IPluginLogger>();
    }

    public void Dispose() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }

    [Fact]
    public async Task CreateBackupAsync_CreatesZipFile_And_PrunesOld()
    {
        string saves = Path.Combine(_tempDir, "Saves");
        Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, "save1.dat"), "data");

        var service = new SaveBackupService(_tempDir, _log);
        var def = new SaveGameDef { GameId = "TestGame" };

        // ACT: Create multiple backups
        for (int i = 0; i < 20; i++)
        {
            await service.CreateBackupAsync(def, saves, label: $"b{i}");
            await Task.Delay(10); // Ensure distinct timestamps/creation times
        }

        var backups = service.ListBackups("TestGame");
        
        // Assert pruning (max 15)
        Assert.InRange(backups.Count, 1, 15);
    }

    [Fact]
    public async Task RestoreBackupAsync_RestoresFiles_And_CreatesSafetyBackup()
    {
        string saves = Path.Combine(_tempDir, "Saves");
        Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, "current.dat"), "current");

        var service = new SaveBackupService(_tempDir, _log);
        var def = new SaveGameDef { GameId = "RestoreGame" };

        // Create a backup first
        string sourceSaves = Path.Combine(_tempDir, "SourceSaves");
        Directory.CreateDirectory(sourceSaves);
        File.WriteAllText(Path.Combine(sourceSaves, "old.dat"), "old");
        string? zip = await service.CreateBackupAsync(def, sourceSaves, "toBeRestored");

        var backup = service.ListBackups("RestoreGame").First();

        // ACT
        await service.RestoreBackupAsync(backup, saves);

        // ASSERT
        Assert.True(File.Exists(Path.Combine(saves, "old.dat")));
        Assert.False(File.Exists(Path.Combine(saves, "current.dat")));
        
        // Check for safety backup (_pre-restore.zip)
        var allFiles = Directory.GetFiles(service.BackupFolderFor("RestoreGame"));
        Assert.Contains(allFiles, f => f.Contains("_pre-restore.zip"));
    }
}

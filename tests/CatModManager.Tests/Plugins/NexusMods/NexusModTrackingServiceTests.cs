using System;
using System.IO;
using Xunit;
using CmmPlugin.NexusMods;

namespace CatModManager.Tests.Plugins.NexusMods;

public class NexusModTrackingServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly NexusDatabase _db;
    private readonly NexusModTrackingService _service;

    public NexusModTrackingServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_NexusTrack_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _db = new NexusDatabase(_tempDir);
        _service = new NexusModTrackingService(_db);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void Track_And_GetEntry_Works()
    {
        string path = "C:\\Mods\\MyMod";
        _service.Track(path, 123, 456, "1.0", "cyberpunk2077");

        var entry = _service.GetEntry(path);
        Assert.NotNull(entry);
        Assert.Equal(123, entry!.ModId);
        Assert.Equal(456, entry.FileId);
        Assert.Equal("1.0", entry.Version);
        Assert.Equal("cyberpunk2077", entry.GameDomain);
    }

    [Fact]
    public void Track_Updates_Existing_Entry()
    {
        string path = "C:\\Mods\\MyMod";
        _service.Track(path, 123, 456, "1.0", "cyberpunk2077");
        _service.Track(path, 123, 789, "1.1", "cyberpunk2077");

        var entry = _service.GetEntry(path);
        Assert.NotNull(entry);
        Assert.Equal(789, entry!.FileId);
        Assert.Equal("1.1", entry.Version);
    }

    [Fact]
    public void GetEntryBySourcePath_Returns_Correct_Entry()
    {
        string path = "C:\\Mods\\MyMod";
        string src = "C:\\Downloads\\mod.zip";
        _service.Track(path, 123, 456, "1.0", "cyberpunk2077", src);

        var entry = _service.GetEntryBySourcePath(src);
        Assert.NotNull(entry);
        Assert.Equal(path, entry!.ModFolderPath);
    }

    [Fact]
    public void IsTracked_Returns_Correct_Value()
    {
        string path = "C:\\Mods\\Tracked";
        Assert.False(_service.IsTracked(path));

        _service.Track(path, 1, 1, "1", "game");
        Assert.True(_service.IsTracked(path));
    }

    [Fact]
    public void GetEntryByModIdAndFileId_Returns_Correct_Entry()
    {
        _service.Track("path1", 10, 20, "1", "game");
        
        var entry = _service.GetEntryByModIdAndFileId(10, 20);
        Assert.NotNull(entry);
        Assert.Equal("path1", entry!.ModFolderPath);
    }
}

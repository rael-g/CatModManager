using System;
using System.IO;
using Xunit;
using CmmPlugin.NexusMods;

namespace CatModManager.Tests.Plugins.NexusMods;

public class NexusDatabaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly NexusDatabase _db;

    public NexusDatabaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_NexusDb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _db = new NexusDatabase(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            // Close connection before deleting (though NexusDatabase uses short-lived ones)
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void Settings_SetAndGet_Works()
    {
        _db.SetSetting("test_key", "test_value");
        string? value = _db.GetSetting("test_key");
        Assert.Equal("test_value", value);
    }

    [Fact]
    public void Settings_Overwrite_Works()
    {
        _db.SetSetting("key", "v1");
        _db.SetSetting("key", "v2");
        Assert.Equal("v2", _db.GetSetting("key"));
    }
}

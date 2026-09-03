using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;

namespace CatModManager.Tests.Core.Models;

public class CoreModelsAndSourcesTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ICatPathService _pathService;

    public CoreModelsAndSourcesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CoreTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _pathService = new CatPathService();
    }

    [Fact]
    public void CatPathService_Returns_Valid_Paths()
    {
        Assert.NotNull(_pathService.BaseDataPath);
        Assert.NotNull(_pathService.ProfilesPath);
        Assert.Contains(".toml", _pathService.GetProfilePath("test"));
    }

    [Fact]
    public void ConfigService_Load_Save_Basic()
    {
        // A temp directory, not _pathService: that one resolves to the real ~/.local/share/
        // catmodmanager, so this test used to open the user's own cmm.db and overwrite their
        // LastProfileName with "Test". Harmless-looking while it only wrote config rows, and not at
        // all harmless once AppDatabase started migrating the schema of whatever it is pointed at.
        var paths = new Support.MockCatPathService(_tempDir);

        var configService = new ConfigService(new AppDatabase(paths));
        configService.Current.LastProfileName = "Test";
        configService.Save();

        var config2 = new ConfigService(new AppDatabase(paths));
        Assert.Equal("Test", config2.Current.LastProfileName);
    }

    [Fact]
    public void Mod_Model_Property_Changes()
    {
        var mod = new Mod("Old", "Path", 0);
        bool fired = false;
        mod.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(mod.Name)) fired = true; };
        mod.Name = "New";
        Assert.True(fired);
    }

    [Fact]
    public void PhysicalFileSource_Basic()
    {
        string path = Path.Combine(_tempDir, "file.txt");
        File.WriteAllText(path, "content");
        var source = new PhysicalFileSource(path);
        
        Assert.Equal(7, source.Length);
        using var stream = source.OpenRead();
        Assert.NotNull(stream);
    }

    public void Dispose() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
}

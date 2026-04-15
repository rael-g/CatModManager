using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using NSubstitute;
using CatModManager.Core.Services;
using CatModManager.Core.Models;

namespace CatModManager.Tests.Core.Services;

/// <summary>
/// Combination of Unit and light Integration tests for Core services.
/// Uses NSubstitute for aggressive isolation where possible.
/// </summary>
public class CoreServicesTests : IDisposable
{
    private readonly string _tempDir;

    public CoreServicesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CoreTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void CatPathService_ShouldResolveCorrectPaths()
    {
        // UNIT TEST: Testing path resolution logic
        var pathService = new CatPathService(_tempDir);
        
        Assert.Equal(_tempDir, pathService.BaseDataPath);
        Assert.Contains("profiles", pathService.ProfilesPath);
        Assert.Equal(Path.Combine(pathService.ProfilesPath, "Test.toml"), pathService.GetProfilePath("Test"));
    }

    [Fact]
    public void TomlModParser_ShouldParseCorrectly()
    {
        // UNIT TEST: Testing TOML metadata parsing
        var parser = new TomlModParser();
        string modDir = Path.Combine(_tempDir, "TestMod");
        Directory.CreateDirectory(modDir);
        
        string tomlContent = "Name = \"Awesome Mod\"\nVersion = \"2.1\"\nCategory = \"Visuals\"";
        File.WriteAllText(Path.Combine(modDir, "mod_info.toml"), tomlContent);

        var mod = parser.ParseModInfo(modDir);

        Assert.NotNull(mod);
        Assert.Equal("Awesome Mod", mod.Name);
        Assert.Equal("2.1", mod.Version);
        Assert.Equal("Visuals", mod.Category);
    }

    [Fact]
    public void ConfigService_Persistence_Integration()
    {
        // INTEGRATION TEST: Testing SQLite persistence
        var mockPaths = Substitute.For<ICatPathService>();
        mockPaths.BaseDataPath.Returns(_tempDir);
        
        var db = new AppDatabase(mockPaths);
        var service = new ConfigService(db);

        service.Current.LastProfileName = "PersistedProfile";
        service.Save();

        // Clear pools to unlock file for next check
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var newService = new ConfigService(db);
        Assert.Equal("PersistedProfile", newService.Current.LastProfileName);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}

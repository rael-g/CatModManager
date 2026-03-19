using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;

namespace CatModManager.Tests;

public class ProfileAndScannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IModParser _parser;
    private readonly IFileService _fileService;

    public ProfileAndScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ScannerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _parser = new TomlModParser();
        _fileService = new PhysicalFileService();
    }

    [Fact]
    public async Task LocalModScanner_Detects_Folders()
    {
        var scanner = new LocalModScanner(_parser, _fileService);
        string modPath = Path.Combine(_tempDir, "TestMod");
        Directory.CreateDirectory(modPath);

        var mods = await scanner.ScanDirectoryAsync(_tempDir);
        Assert.Single(mods);
        Assert.Equal("TestMod", mods.First().Name);
    }

    [Fact]
    public async Task TomlProfileService_Save_And_Load()
    {
        var service = new TomlProfileService();
        string path = Path.Combine(_tempDir, "test.toml");
        var profile = new Profile { Name = "Test", Mods = new List<Mod>() };

        await service.SaveProfileAsync(profile, path);
        var loaded = await service.LoadProfileAsync(path);

        Assert.NotNull(loaded);
        Assert.Equal("Test", loaded.Name);
    }

    public void Dispose() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
}

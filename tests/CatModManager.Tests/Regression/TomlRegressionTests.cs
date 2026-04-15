using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using Nett;

namespace CatModManager.Tests.Regression;

/// <summary>
/// Regression tests for TOML serialization/deserialization bugs.
/// </summary>
public class TomlRegressionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TomlProfileService _service = new();

    public TomlRegressionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_TomlRegress_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── Mod round-trip ────────────────────────────────────────────────────────

    [Fact]
    public void Mod_TomlRoundTrip_DoesNotThrow_WhenLegacyFieldsPresent()
    {
        // A TOML produced by an older version of CMM will contain HasRootFolder and RootPath.
        // Nett must be able to deserialize it without throwing.
        const string toml = """
            [[Mods]]
            HasRootFolder = false
            Name = "TestMod"
            RootPath = "C:\\mods\\TestMod"
            Priority = 0
            IsEnabled = true
            IsArchive = false
            Category = "Uncategorized"
            Version = "1.0"
            IsSeparator = false
            """;

        var exception = Record.Exception(() => Toml.ReadString<Profile>(toml));
        Assert.Null(exception);
    }

    [Fact]
    public void Mod_TomlRoundTrip_PreservesAllFields()
    {
        var original = new Profile
        {
            Name           = "RegressionProfile",
            ModsFolderPath = @"C:\game\mods",
            BaseDataPath   = @"C:\game",
            Mods = new List<Mod>
            {
                new Mod("Alpha", @"C:\game\mods\Alpha", 1, false, "Gameplay", "2.0"),
                new Mod("Beta",  @"C:\game\mods\Beta",  0, true,  "Visuals",  "1.5")
            }
        };

        var toml    = Toml.WriteString(original);
        var loaded  = Toml.ReadString<Profile>(toml);

        Assert.NotNull(loaded);
        Assert.Equal("RegressionProfile", loaded.Name);
        Assert.Equal(@"C:\game\mods", loaded.ModsFolderPath);
        Assert.Equal(2, loaded.Mods.Count);
        Assert.Equal("Alpha",    loaded.Mods[0].Name);
        Assert.Equal("Gameplay", loaded.Mods[0].Category);
        Assert.Equal("Beta",     loaded.Mods[1].Name);
        Assert.True(loaded.Mods[1].IsArchive);
    }

    // ── TomlProfileService round-trip ─────────────────────────────────────────

    [Fact]
    public async Task TomlProfileService_SaveAndLoad_ReturnsNonNull_WithMods()
    {
        var profile = new Profile
        {
            Name           = "Lies of P",
            ModsFolderPath = @"C:\game\mods",
            BaseDataPath   = @"C:\game",
            Mods = new List<Mod>
            {
                new Mod("Kaine Outfit", @"C:\game\mods\Kaine", 0, true, "Characters", "1.2")
            }
        };

        string path = Path.Combine(_tempDir, "Lies of P.toml");
        await _service.SaveProfileAsync(profile, path);

        var loaded = await _service.LoadProfileAsync(path);

        Assert.NotNull(loaded);
        Assert.Equal("Lies of P", loaded!.Name);
        Assert.Single(loaded.Mods);
        Assert.Equal("Kaine Outfit", loaded.Mods[0].Name);
        Assert.Equal("Characters",   loaded.Mods[0].Category);
        Assert.True(loaded.Mods[0].IsArchive);
    }

    [Fact]
    public async Task TomlProfileService_Load_ReturnsNonNull_ForLegacyTomlWithHasRootFolder()
    {
        string legacyToml = """
            Name = "Lies of P"
            ModsFolderPath = "C:\\game\\mods"
            BaseDataPath = "C:\\game"
            GameExecutablePath = "C:\\game\\game.exe"
            DataSubFolder = "Data"
            GameSupportId = "generic"
            LaunchArguments = ""
            DownloadsFolderPath = ""

            [[Mods]]
            HasRootFolder = false
            Name = "Kaine Outfit"
            RootPath = "C:\\game\\mods\\Kaine"
            Priority = 0
            IsEnabled = true
            IsArchive = true
            Category = "Characters"
            Version = "1.2"
            IsSeparator = false
            """;

        string path = Path.Combine(_tempDir, "legacy.toml");
        await File.WriteAllTextAsync(path, legacyToml);

        var loaded = await _service.LoadProfileAsync(path);

        Assert.NotNull(loaded);
        Assert.Equal("Lies of P",   loaded!.Name);
        Assert.Equal("Kaine Outfit", loaded.Mods[0].Name);
    }
}

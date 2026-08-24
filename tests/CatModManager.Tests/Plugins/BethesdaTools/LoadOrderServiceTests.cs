using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using NSubstitute;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Services;
using CmmPlugin.BethesdaTools.Models;

namespace CatModManager.Tests.Plugins.BethesdaTools;

public class LoadOrderServiceTests
{
    private readonly IPluginLogger _log = Substitute.For<IPluginLogger>();
    private readonly IFileService _fileService = Substitute.For<IFileService>();
    private readonly LoadOrderService _service;

    public LoadOrderServiceTests()
    {
        _service = new LoadOrderService(_log, _fileService);
    }

    [Fact]
    public void Refresh_MergesDiscoveredAndOrderedPlugins()
    {
        // ARRANGE
        string dataDir = Path.Combine("Skyrim", "Data");
        string pluginsTxt = Path.Combine("AppData", "plugins.txt");

        _fileService.DirectoryExists(dataDir).Returns(true);
        _fileService.FileExists(pluginsTxt).Returns(true);

        // Discovered on disk
        _fileService.GetFiles(dataDir, "*").Returns(new[] {
            Path.Combine(dataDir, "Skyrim.esm"),
            Path.Combine(dataDir, "Update.esm"),
            Path.Combine(dataDir, "NewMod.esp")
        });

        // Existing order in plugins.txt (Skyrim.esm enabled, Update.esm disabled)
        _fileService.ReadAllLines(pluginsTxt).Returns(new[] {
            "*Skyrim.esm",
            "Update.esm"
        });

        // ACT
        _service.Refresh(dataDir, pluginsTxt, null);

        // ASSERT
        Assert.Equal(3, _service.Entries.Count);
        
        // Skyrim.esm should be first and enabled
        Assert.Equal("Skyrim.esm", _service.Entries[0].FileName);
        Assert.True(_service.Entries[0].IsEnabled);

        // Update.esm should be second and disabled
        Assert.Equal("Update.esm", _service.Entries[1].FileName);
        Assert.False(_service.Entries[1].IsEnabled);

        // NewMod.esp (not in plugins.txt) should be last and enabled by default
        Assert.Equal("NewMod.esp", _service.Entries[2].FileName);
        Assert.True(_service.Entries[2].IsEnabled);
    }

    [Fact]
    public void Refresh_ExcludesImplicitMasters()
    {
        // ARRANGE — the engine loads base game and official DLC plugins itself. Listing them in
        // plugins.txt is not how the format works and corrupts the load order.
        string dataDir = Path.Combine("Starfield", "Data");
        var starfield = new BethesdaGame("Starfield", UsesStarFormat: true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Starfield.esm", "Constellation.esm" });

        _fileService.DirectoryExists(dataDir).Returns(true);
        _fileService.GetFiles(dataDir, "*").Returns(new[] {
            Path.Combine(dataDir, "Starfield.esm"),
            Path.Combine(dataDir, "Constellation.esm"),
            Path.Combine(dataDir, "CoolMod.esp")
        });

        // ACT
        _service.Refresh(dataDir, null, null, starfield);

        // ASSERT
        Assert.Single(_service.Entries);
        Assert.Equal("CoolMod.esp", _service.Entries[0].FileName);
    }

    [Fact]
    public void Refresh_HoistsNewMastersAboveRegularPlugins()
    {
        // ARRANGE — newly discovered files are appended at the end, but the engine rejects a load
        // order where a master sorts after a regular plugin.
        string dataDir = Path.Combine("Skyrim", "Data");
        string pluginsTxt = Path.Combine("AppData", "plugins.txt");

        _fileService.DirectoryExists(dataDir).Returns(true);
        _fileService.FileExists(pluginsTxt).Returns(true);
        _fileService.GetFiles(dataDir, "*").Returns(new[] {
            Path.Combine(dataDir, "ExistingMod.esp"),
            Path.Combine(dataDir, "BrandNew.esm")
        });
        _fileService.ReadAllLines(pluginsTxt).Returns(new[] { "*ExistingMod.esp" });

        // ACT
        _service.Refresh(dataDir, pluginsTxt, null);

        // ASSERT
        Assert.Equal("BrandNew.esm", _service.Entries[0].FileName);
        Assert.Equal("ExistingMod.esp", _service.Entries[1].FileName);
    }

    [Fact]
    public void Refresh_FindsPluginsNestedUnderModDataFolder()
    {
        // ARRANGE — plenty of archives ship plugins under "Data/" and get installed that way.
        var mod = Substitute.For<IModInfo>();
        string modRoot = Path.Combine("Mods", "Nested");
        string modData = Path.Combine(modRoot, "Data");
        mod.IsEnabled.Returns(true);
        mod.ModRootPath.Returns(modRoot);

        _fileService.DirectoryExists(modRoot).Returns(true);
        _fileService.DirectoryExists(modData).Returns(true);
        _fileService.GetFiles(modRoot, "*").Returns(Array.Empty<string>());
        _fileService.GetFiles(modData, "*").Returns(new[] { Path.Combine(modData, "Nested.esp") });

        // ACT
        _service.Refresh(null, null, new[] { mod });

        // ASSERT
        Assert.Single(_service.Entries);
        Assert.Equal("Nested.esp", _service.Entries[0].FileName);
    }

    [Fact]
    public void Refresh_HandlesActiveMods()
    {
        // ARRANGE
        var mod1 = Substitute.For<IModInfo>();
        string modRoot = Path.Combine("Mods", "Mod1");
        mod1.IsEnabled.Returns(true);
        mod1.ModRootPath.Returns(modRoot);

        _fileService.DirectoryExists(modRoot).Returns(true);
        _fileService.GetFiles(modRoot, "*").Returns(new[] { Path.Combine(modRoot, "Mod1Plugin.esp") });

        // ACT
        _service.Refresh(null, null, new[] { mod1 });

        // ASSERT
        Assert.Single(_service.Entries);
        Assert.Equal("Mod1Plugin.esp", _service.Entries[0].FileName);
    }

    [Fact]
    public void Save_WritesCorrectFormat_Star()
    {
        // ARRANGE
        string pluginsTxt = "C:\\AppData\\plugins.txt";
        _service.Entries.Add(new EspEntry("A.esp", true, 0));
        _service.Entries.Add(new EspEntry("B.esp", false, 1));

        // ACT
        _service.Save(pluginsTxt, true);

        // ASSERT
        _fileService.Received().WriteAllLines(pluginsTxt, Arg.Is<string[]>(lines => 
            lines[0] == "*A.esp" && lines[1] == "B.esp"));
    }

    [Fact]
    public void Save_OmitsDisabledEntries_NoStar()
    {
        // ARRANGE — pre-Skyrim SE engines have no way to represent a disabled plugin:
        // plugins.txt lists the enabled ones and nothing else.
        string pluginsTxt = "C:\\AppData\\plugins.txt";
        _service.Entries.Add(new EspEntry("A.esp", true, 0));
        _service.Entries.Add(new EspEntry("B.esp", false, 1));
        _service.Entries.Add(new EspEntry("C.esp", true, 2));

        // ACT
        _service.Save(pluginsTxt, false);

        // ASSERT
        _fileService.Received().WriteAllLines(pluginsTxt, Arg.Is<string[]>(lines =>
            lines.Length == 2 && lines[0] == "A.esp" && lines[1] == "C.esp"));
    }

    [Fact]
    public void RecalculateOrder_UpdatesIndices()
    {
        // ARRANGE
        var e1 = new EspEntry("A.esp", true, 99);
        var e2 = new EspEntry("B.esp", true, 99);
        _service.Entries.Add(e1);
        _service.Entries.Add(e2);

        // ACT
        _service.RecalculateOrder();

        // ASSERT
        Assert.Equal(0, e1.LoadOrder);
        Assert.Equal(1, e2.LoadOrder);
    }
}

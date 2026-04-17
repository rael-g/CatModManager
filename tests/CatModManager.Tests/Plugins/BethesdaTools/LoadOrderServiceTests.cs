using System;
using System.Collections.Generic;
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
        string dataDir = "C:\\Skyrim\\Data";
        string pluginsTxt = "C:\\AppData\\plugins.txt";

        _fileService.DirectoryExists(dataDir).Returns(true);
        _fileService.FileExists(pluginsTxt).Returns(true);

        // Discovered on disk
        _fileService.GetFiles(dataDir, "*").Returns(new[] {
            "C:\\Skyrim\\Data\\Skyrim.esm",
            "C:\\Skyrim\\Data\\Update.esm",
            "C:\\Skyrim\\Data\\NewMod.esp"
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
    public void Refresh_HandlesActiveMods()
    {
        // ARRANGE
        var mod1 = Substitute.For<IModInfo>();
        mod1.IsEnabled.Returns(true);
        mod1.ModRootPath.Returns("C:\\Mods\\Mod1");

        _fileService.DirectoryExists(mod1.ModRootPath).Returns(true);
        _fileService.GetFiles(mod1.ModRootPath, "*").Returns(new[] { "C:\\Mods\\Mod1\\Mod1Plugin.esp" });

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
    public void Save_WritesCorrectFormat_NoStar()
    {
        // ARRANGE
        string pluginsTxt = "C:\\AppData\\plugins.txt";
        _service.Entries.Add(new EspEntry("A.esp", true, 0));
        _service.Entries.Add(new EspEntry("B.esp", false, 1));

        // ACT
        _service.Save(pluginsTxt, false);

        // ASSERT
        _fileService.Received().WriteAllLines(pluginsTxt, Arg.Is<string[]>(lines => 
            lines[0] == "A.esp" && lines[1] == "#B.esp"));
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

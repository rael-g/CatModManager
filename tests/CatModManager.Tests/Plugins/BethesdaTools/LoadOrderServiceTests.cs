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

    /// <summary>A managed mod whose root folder ships the given plugin files.</summary>
    private IModInfo Mod(string name, bool enabled, params string[] plugins)
    {
        string root = Path.Combine("Mods", name);
        var mod = Substitute.For<IModInfo>();
        mod.ModRootPath.Returns(root);
        mod.IsEnabled.Returns(enabled);
        _fileService.DirectoryExists(root).Returns(true);
        _fileService.GetFiles(root, "*").Returns(plugins.Select(p => Path.Combine(root, p)).ToArray());
        return mod;
    }

    /// <summary>The game's own Data folder, holding only files the engine ships.</summary>
    private string GameData(params string[] plugins)
    {
        string dir = Path.Combine("Game", "Data");
        _fileService.DirectoryExists(dir).Returns(true);
        _fileService.GetFiles(dir, "*").Returns(plugins.Select(p => Path.Combine(dir, p)).ToArray());
        return dir;
    }

    [Fact]
    public void Refresh_MergesDiscoveredAndOrderedPlugins()
    {
        // ARRANGE
        string pluginsTxt = Path.Combine("AppData", "plugins.txt");
        _fileService.FileExists(pluginsTxt).Returns(true);

        var mod = Mod("Pack", enabled: true, "AlphaMod.esm", "BetaMod.esm", "NewMod.esp");

        // Existing order in plugins.txt (AlphaMod enabled, BetaMod disabled)
        _fileService.ReadAllLines(pluginsTxt).Returns(new[] {
            "*AlphaMod.esm",
            "BetaMod.esm"
        });

        // ACT
        _service.Refresh(null, pluginsTxt, new[] { mod });

        // ASSERT
        Assert.Equal(3, _service.Entries.Count);

        Assert.Equal("AlphaMod.esm", _service.Entries[0].FileName);
        Assert.True(_service.Entries[0].IsEnabled);

        Assert.Equal("BetaMod.esm", _service.Entries[1].FileName);
        Assert.False(_service.Entries[1].IsEnabled);

        // NewMod.esp (not in plugins.txt) should be last and enabled by default
        Assert.Equal("NewMod.esp", _service.Entries[2].FileName);
        Assert.True(_service.Entries[2].IsEnabled);
    }

    [Fact]
    public void Refresh_TreatsEveryPluginShippedWithTheGameAsOwnedByTheEngine()
    {
        // ARRANGE — the real Starfield install. The hardcoded master list knew about the first
        // four, but the game had since shipped four more official .esm files, and those showed up
        // in the PLUGINS tab as toggleable rows that would have been written into plugins.txt.
        string dataDir = GameData(
            "Starfield.esm", "Constellation.esm", "OldMars.esm", "BlueprintShips-Starfield.esm",
            "BlueprintShips-SFBGS050.esm", "SFBGS00D.esm", "SFBGS047.esm", "SFBGS050.esm");

        var starfield = new BethesdaGame("Starfield", UsesStarFormat: true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Starfield.esm", "Constellation.esm" });   // deliberately stale

        // ACT
        _service.Refresh(dataDir, null, null, starfield);

        // ASSERT — nothing to manage: every file in Data belongs to the game.
        Assert.Empty(_service.Entries);
    }

    [Fact]
    public void Refresh_KeepsModPluginsThatTheVfsMountedIntoTheDataFolder()
    {
        // ARRANGE — once mods are mounted, their plugins sit in Data next to the game's own.
        // Only the ones no managed mod provides may be treated as the game's.
        string dataDir = GameData("Starfield.esm", "SFBGS050.esm", "CoolMod.esp");
        var mod = Mod("Cool", enabled: true, "CoolMod.esp");

        // ACT
        _service.Refresh(dataDir, null, new[] { mod });

        // ASSERT
        Assert.Single(_service.Entries);
        Assert.Equal("CoolMod.esp", _service.Entries[0].FileName);
    }

    [Fact]
    public void Refresh_StillDropsKnownMastersWhenTheDataFolderIsUnreadable()
    {
        // ARRANGE — no Data folder to derive from (game not installed yet, or path misconfigured).
        // The hardcoded list is the floor that keeps base masters out of plugins.txt.
        var starfield = new BethesdaGame("Starfield", UsesStarFormat: true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Starfield.esm" });
        var mod = Mod("Weird", enabled: true, "Starfield.esm", "CoolMod.esp");

        // ACT
        _service.Refresh(null, null, new[] { mod }, starfield);

        // ASSERT
        Assert.Single(_service.Entries);
        Assert.Equal("CoolMod.esp", _service.Entries[0].FileName);
    }

    [Fact]
    public void Refresh_HoistsNewMastersAboveRegularPlugins()
    {
        // ARRANGE — newly discovered files are appended at the end, but the engine rejects a load
        // order where a master sorts after a regular plugin.
        string pluginsTxt = Path.Combine("AppData", "plugins.txt");
        _fileService.FileExists(pluginsTxt).Returns(true);
        _fileService.ReadAllLines(pluginsTxt).Returns(new[] { "*ExistingMod.esp" });

        var mod = Mod("Pack", enabled: true, "ExistingMod.esp", "BrandNew.esm");

        // ACT
        _service.Refresh(null, pluginsTxt, new[] { mod });

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

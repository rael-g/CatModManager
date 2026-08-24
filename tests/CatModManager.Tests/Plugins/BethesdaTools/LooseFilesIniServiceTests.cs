using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using NSubstitute;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Models;
using CmmPlugin.BethesdaTools.Services;

namespace CatModManager.Tests.Plugins.BethesdaTools;

/// <summary>
/// Starfield and Fallout 4 read assets only from their packed .ba2 archives unless the [Archive]
/// section is overridden, so without this file every mod CMM mounts is silently ignored.
/// </summary>
public class LooseFilesIniServiceTests
{
    private readonly IPluginLogger _log = Substitute.For<IPluginLogger>();
    private readonly IFileService _fs = Substitute.For<IFileService>();
    private readonly LooseFilesIniService _service;

    private static readonly BethesdaGame Starfield =
        new("Starfield", UsesStarFormat: true, CustomIniFile: "StarfieldCustom.ini");

    private const string MyGames = "/prefix/Documents/My Games/Starfield";
    private const string IniPath = MyGames + "/StarfieldCustom.ini";

    public LooseFilesIniServiceTests() => _service = new LooseFilesIniService(_fs, _log);

    private string[]? CapturedWrite()
    {
        var calls = _fs.ReceivedCalls();
        foreach (var c in calls)
            if (c.GetMethodInfo().Name == nameof(IFileService.WriteAllLines))
                return (string[])c.GetArguments()[1]!;
        return null;
    }

    [Fact]
    public void Apply_CreatesIniWithArchiveSection_WhenMissing()
    {
        _fs.FileExists(IniPath).Returns(false);

        Assert.True(_service.Apply(Starfield, MyGames));

        var written = CapturedWrite();
        Assert.NotNull(written);
        Assert.Contains("[Archive]", written);
        Assert.Contains("bInvalidateOlderFiles=1", written);
        Assert.Contains("sResourceDataDirsFinal=", written);
    }

    [Fact]
    public void Apply_PreservesUnrelatedSections()
    {
        // Users keep display/gameplay tweaks in this file; clobbering them would be destructive.
        _fs.FileExists(IniPath).Returns(true);
        _fs.ReadAllLines(IniPath).Returns(new[]
        {
            "[Display]",
            "fGamma=1.2",
            "[Archive]",
            "bInvalidateOlderFiles=0",
        });

        _service.Apply(Starfield, MyGames);

        var written = CapturedWrite();
        Assert.NotNull(written);
        Assert.Contains("[Display]", written);
        Assert.Contains("fGamma=1.2", written);
        Assert.Contains("bInvalidateOlderFiles=1", written);
        Assert.DoesNotContain("bInvalidateOlderFiles=0", written);
    }

    [Fact]
    public void Apply_DoesNotRewriteFile_WhenAlreadyCorrect()
    {
        _fs.FileExists(IniPath).Returns(true);
        _fs.ReadAllLines(IniPath).Returns(new[]
        {
            "[Archive]",
            "bInvalidateOlderFiles=1",
            "sResourceDataDirsFinal=",
        });

        Assert.True(_service.Apply(Starfield, MyGames));

        _fs.DidNotReceive().WriteAllLines(Arg.Any<string>(), Arg.Any<string[]>());
    }

    [Fact]
    public void Apply_OnlyTouchesKeysInsideArchiveSection()
    {
        // A same-named key in a later section must not be mistaken for the one we manage.
        _fs.FileExists(IniPath).Returns(true);
        _fs.ReadAllLines(IniPath).Returns(new[]
        {
            "[Archive]",
            "bInvalidateOlderFiles=1",
            "sResourceDataDirsFinal=",
            "[General]",
            "bInvalidateOlderFiles=0",
        });

        _service.Apply(Starfield, MyGames);

        // Already correct within [Archive] — nothing to do, and [General] is left alone.
        _fs.DidNotReceive().WriteAllLines(Arg.Any<string>(), Arg.Any<string[]>());
    }

    [Fact]
    public void Apply_ReportsFailure_WhenMyGamesFolderUnknown()
    {
        Assert.False(_service.Apply(Starfield, null));
        _fs.DidNotReceive().WriteAllLines(Arg.Any<string>(), Arg.Any<string[]>());
    }

    [Fact]
    public void Apply_IsNoOp_ForEnginesThatLoadLooseFilesNatively()
    {
        var skyrim = new BethesdaGame("Skyrim Special Edition", UsesStarFormat: true);

        Assert.True(_service.Apply(skyrim, MyGames));
        _fs.DidNotReceive().WriteAllLines(Arg.Any<string>(), Arg.Any<string[]>());
    }
}

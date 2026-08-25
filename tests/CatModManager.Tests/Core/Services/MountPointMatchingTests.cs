using System.Collections.Generic;
using Xunit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Ui.ViewModels;

namespace CatModManager.Tests.Core.Services;

/// <summary>
/// A mod is assigned to a mount point by id, with null meaning "use the default one". These tests
/// pin that contract down, because getting it wrong drops the mod from every mount point silently:
/// the install succeeds, the mod shows as enabled, and nothing ever reaches the game folder.
/// </summary>
public class MountPointMatchingTests
{
    private static readonly MountPointDef Data = new("data", "Data", "Data", isGameDefined: true);
    private static readonly MountPointDef Root = new("root", "Game Root", "", isGameDefined: true);

    private static Mod ModWith(string? mountPointId) =>
        new("TestMod", "/mods/TestMod", 1) { MountPointId = mountPointId };

    [Fact]
    public void NullMountPointId_GoesToTheDefaultMountPoint()
    {
        var mod = ModWith(null);

        Assert.True(VfsOrchestrationService.MountPointMatches(mod, Data, Data));
        Assert.False(VfsOrchestrationService.MountPointMatches(mod, Root, Data));
    }

    [Fact]
    public void ExplicitMountPointId_GoesToThatMountPointOnly()
    {
        var mod = ModWith("root");

        Assert.True(VfsOrchestrationService.MountPointMatches(mod, Root, Data));
        Assert.False(VfsOrchestrationService.MountPointMatches(mod, Data, Data));
    }

    [Fact]
    public void MountPointId_IsMatchedCaseInsensitively()
    {
        var mod = ModWith("ROOT");

        Assert.True(VfsOrchestrationService.MountPointMatches(mod, Root, Data));
    }

    [Fact]
    public void UnknownMountPointId_MatchesNothing_NotEvenTheDefault()
    {
        // Regression: the installer used to store the literal string "Default" when it had no mount
        // point to assign. No game defines an id called "Default" (KOTOR uses "override",
        // Skyrim/Starfield use "data"/"root"), so the mod matched no mount point at all and was
        // silently never mounted. The installer now stores null instead — this test documents why
        // that mattered by showing an unknown id really does fall through everything.
        var mod = ModWith("Default");

        Assert.False(VfsOrchestrationService.MountPointMatches(mod, Data, Data));
        Assert.False(VfsOrchestrationService.MountPointMatches(mod, Root, Data));
    }
}

/// <summary>
/// Profiles saved by older builds still hold mods pointing at mount point ids that no longer exist,
/// so fixing the installer alone would leave those mods permanently unmounted.
/// </summary>
public class MountPointIdMigrationTests
{
    private static readonly IReadOnlyList<MountPointDef> Points = new[]
    {
        new MountPointDef("data", "Data", "Data", isGameDefined: true),
        new MountPointDef("root", "Game Root", "", isGameDefined: true),
    };

    private static Mod ModWith(string? id) => new("M", "/mods/M", 1) { MountPointId = id };

    [Fact]
    public void LiteralDefault_IsResetToNull()
    {
        var mod = ModWith("Default");

        ProfileCoordinator.MigrateOrphanedMountPointIds([mod], Points);

        Assert.Null(mod.MountPointId);
    }

    [Fact]
    public void ValidIds_AreLeftAlone()
    {
        var data = ModWith("data");
        var root = ModWith("root");

        ProfileCoordinator.MigrateOrphanedMountPointIds([data, root], Points);

        Assert.Equal("data", data.MountPointId);
        Assert.Equal("root", root.MountPointId);
    }

    [Fact]
    public void ValidIds_AreMatchedCaseInsensitively()
    {
        var mod = ModWith("DATA");

        ProfileCoordinator.MigrateOrphanedMountPointIds([mod], Points);

        Assert.Equal("DATA", mod.MountPointId);
    }

    [Fact]
    public void AlreadyNull_StaysNull()
    {
        var mod = ModWith(null);

        ProfileCoordinator.MigrateOrphanedMountPointIds([mod], Points);

        Assert.Null(mod.MountPointId);
    }
}

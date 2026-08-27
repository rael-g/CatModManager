using System.Collections.Generic;
using System.Linq;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using Xunit;

namespace CatModManager.Tests.Core.Services;

/// <summary>
/// Refresh reconciles the profile against the folder. The profile owns the user's decisions and the
/// folder owns what exists, so the whole point is that only the difference moves.
/// </summary>
public class ModFolderReconcilerTests
{
    private static Mod At(string name, int priority = 0) =>
        new(name, $"/mods/{name}", priority);

    [Fact]
    public void AModPresentInBothIsTheSameInstanceWithItsSettingsIntact()
    {
        var kept = At("Alpha", priority: 7);
        kept.IsEnabled    = true;
        kept.Category     = "Weapons";
        kept.MountPointId = "data";

        var result = ModFolderReconciler.Reconcile([kept], [At("Alpha")]);

        // Reference equality, not just "a mod named Alpha": rebuilding it from disk is exactly the
        // bug this guards against, and a fresh Mod would carry the scanner's defaults instead.
        Assert.Same(kept, Assert.Single(result.Mods));
        Assert.True(kept.IsEnabled);
        Assert.Equal("Weapons", kept.Category);
        Assert.Equal("data", kept.MountPointId);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void AModOnDiskButNotInTheProfileIsAppendedDisabled()
    {
        var existing = new List<Mod> { At("Alpha"), At("Beta") };

        var result = ModFolderReconciler.Reconcile(existing, [At("Beta"), At("Alpha"), At("Gamma")]);

        var added = Assert.Single(result.Added);
        Assert.Equal("Gamma", added.Name);
        Assert.False(added.IsEnabled);

        // Last means lowest priority, so it cannot take over a file from a mod already installed.
        Assert.Equal(["Alpha", "Beta", "Gamma"], result.Mods.Select(m => m.Name));
    }

    [Fact]
    public void AModWhoseFolderIsGoneIsDropped()
    {
        var existing = new List<Mod> { At("Alpha"), At("Beta"), At("Gamma") };

        var result = ModFolderReconciler.Reconcile(existing, [At("Alpha"), At("Gamma")]);

        Assert.Equal("Beta", Assert.Single(result.Removed).Name);
        Assert.Equal(["Alpha", "Gamma"], result.Mods.Select(m => m.Name));
    }

    [Fact]
    public void TheExistingOrderSurvivesAdditionsAndRemovals()
    {
        // The cheap way to pass the tests above is to return the scanned list, which would silently
        // reset the load order to whatever order the filesystem happened to enumerate.
        var existing = new List<Mod> { At("Zulu"), At("Alpha"), At("Mike") };

        var result = ModFolderReconciler.Reconcile(existing, [At("Alpha"), At("Mike"), At("Zulu")]);

        Assert.Equal(["Zulu", "Alpha", "Mike"], result.Mods.Select(m => m.Name));
    }

    [Fact]
    public void AModStillInstallingIsKeptEvenThoughItsFolderIsNotThereYet()
    {
        var installing = At("Delta");
        installing.IsInstalling = true;

        var result = ModFolderReconciler.Reconcile([At("Alpha"), installing], [At("Alpha")]);

        Assert.Empty(result.Removed);
        Assert.Contains(installing, result.Mods);
    }

    [Fact]
    public void PathsDifferingOnlyInCaseAreDifferentMods()
    {
        // Linux. /mods/Alpha and /mods/alpha are two folders, and folding them would make Refresh
        // delete one of the two and call the other a match.
        var existing = new List<Mod> { new("Alpha", "/mods/Alpha", 0) };

        var result = ModFolderReconciler.Reconcile(existing, [new Mod("alpha", "/mods/alpha", 0)]);

        Assert.Single(result.Added);
        Assert.Single(result.Removed);
    }

    [Fact]
    public void TrailingSeparatorsDoNotMakeAModLookNew()
    {
        var existing = new List<Mod> { new("Alpha", "/mods/Alpha/", 0) };

        var result = ModFolderReconciler.Reconcile(existing, [new Mod("Alpha", "/mods/Alpha", 0)]);

        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
    }
}

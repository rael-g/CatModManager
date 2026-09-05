using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.PluginSdk;

namespace CatModManager.Tests.Core.Services;

public class SimpleConflictResolverTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ILogService _logService;
    private readonly IArchiveExtractor _extractor;

    public SimpleConflictResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ResolverTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _logService = new LogService("");
        _extractor = new SevenZipArchiveExtractor();
    }

    [Fact]
    public void ResolveConflicts_Mods_Override_Base()
    {
        var resolver = new SimpleConflictResolver(_logService, _extractor);
        string baseDir = Path.Combine(_tempDir, "Base");
        string modDir = Path.Combine(_tempDir, "Mod1");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(modDir);

        File.WriteAllText(Path.Combine(baseDir, "file.txt"), "base");
        File.WriteAllText(Path.Combine(modDir, "file.txt"), "mod");

        var mods = new List<Mod> { new Mod("Mod1", modDir, 1) };
        var result = resolver.ResolveConflicts(mods, baseDir);

        Assert.True(result.ContainsKey("file.txt"));
        // O PhysicalFileSource não guarda o conteúdo, mas o path deve ser do mod
        var source = result["file.txt"] as PhysicalFileSource;
        Assert.Contains("Mod1", source!.FilePath);
    }

    /// <summary>Creates a mod folder holding the given relative paths, each an empty file.</summary>
    private Mod ModWith(string name, int priority, params string[] relativePaths)
    {
        string root = Path.Combine(_tempDir, name);
        foreach (var rel in relativePaths)
        {
            string full = Path.Combine(root, rel.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "");
        }
        Directory.CreateDirectory(root);
        return new Mod(name, root, priority);
    }

    private List<ModConflictInfo> ConflictsOf(IReadOnlyList<ConflictReport> reports, string modName) =>
        reports.Single(r => r.ModName == modName).Conflicts;

    /// <summary>
    /// The core claim, and the one the panel is built on: the same path in two mods is a conflict,
    /// and priority decides it the same way <c>ResolveConflicts</c> does — higher priority wins.
    /// Both sides have to hear about it, from their own point of view.
    /// </summary>
    [Fact]
    public void TheSameFileInTwoModsIsReportedToBothSides()
    {
        var resolver = new SimpleConflictResolver(_logService, _extractor);

        var mods = new List<Mod>
        {
            ModWith("Loser",  1, @"meshes\armor\steel.nif"),
            ModWith("Winner", 2, @"meshes\armor\steel.nif"),
        };

        var reports = resolver.GetConflictReport(mods);

        var loses = Assert.Single(ConflictsOf(reports, "Loser"));
        Assert.Equal(ConflictType.Loses, loses.Type);
        Assert.Equal("Winner", loses.OtherModName);
        Assert.Equal(@"meshes\armor\steel.nif", loses.FilePath);

        var wins = Assert.Single(ConflictsOf(reports, "Winner"));
        Assert.Equal(ConflictType.Wins, wins.Type);
        Assert.Equal("Loser", wins.OtherModName);
    }

    /// <summary>
    /// A mod covering a base game file is the entire point of a mod. If that counted, nearly every
    /// mod would show as conflicting and the panel would be noise.
    /// </summary>
    [Fact]
    public void CoveringABaseGameFileIsNotAConflict()
    {
        var resolver = new SimpleConflictResolver(_logService, _extractor);

        string baseDir = Path.Combine(_tempDir, "Base");
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(Path.Combine(baseDir, "file.txt"), "base");

        var mods = new List<Mod> { ModWith("Solo", 1, "file.txt") };

        Assert.Empty(ConflictsOf(resolver.GetConflictReport(mods), "Solo"));
    }

    /// <summary>
    /// Disabled and broken mods do not reach the mount, so reporting them would describe a conflict
    /// that will not happen — and would hide that the remaining two no longer fight at all.
    /// </summary>
    [Fact]
    public void ModsThatWillNotBeMountedDoNotConflict()
    {
        var resolver = new SimpleConflictResolver(_logService, _extractor);

        var off    = ModWith("Off",    1, "shared.txt");
        var broken = ModWith("Broken", 2, "shared.txt");
        var live   = ModWith("Live",   3, "shared.txt");
        off.IsEnabled = false;
        broken.IsBroken = true;

        var reports = resolver.GetConflictReport(new List<Mod> { off, broken, live });

        Assert.Empty(ConflictsOf(reports, "Live"));
        Assert.DoesNotContain(reports, r => r.ModName == "Off" || r.ModName == "Broken");
    }

    /// <summary>
    /// With three mods on one path there is exactly one winner, and everyone else loses to it —
    /// not a chain of pairs.
    ///
    /// The middle mod is the case that decides this. It outranks the bottom one, but its file is
    /// not deployed either, so telling it "you override Low" would be false in the only sense the
    /// panel exists to convey. The mount keeps one source per path; the report says the same.
    /// </summary>
    [Fact]
    public void OnlyTheTopModWinsWhenThreeClaimOnePath()
    {
        var resolver = new SimpleConflictResolver(_logService, _extractor);

        var mods = new List<Mod>
        {
            ModWith("Low",  1, "shared.txt"),
            ModWith("Mid",  2, "shared.txt"),
            ModWith("High", 3, "shared.txt"),
        };

        var reports = resolver.GetConflictReport(mods);

        Assert.Equal(2, ConflictsOf(reports, "High").Count);
        Assert.All(ConflictsOf(reports, "High"), c => Assert.Equal(ConflictType.Wins, c.Type));

        // The middle mod loses to High and claims no win over Low, whom it does outrank.
        var mid = Assert.Single(ConflictsOf(reports, "Mid"));
        Assert.Equal(ConflictType.Loses, mid.Type);
        Assert.Equal("High", mid.OtherModName);

        var low = Assert.Single(ConflictsOf(reports, "Low"));
        Assert.Equal(ConflictType.Loses, low.Type);
        Assert.Equal("High", low.OtherModName);
    }

    /// <summary>
    /// The report and the mount have to agree on what "the same file" means. They key paths
    /// case-insensitively, so a mod shipping <c>Meshes/</c> against another shipping <c>meshes\</c>
    /// is one file, not two — otherwise the panel would swear there is no conflict while the mount
    /// silently picks a winner.
    /// </summary>
    [Fact]
    public void CasingAndSeparatorsDoNotHideAConflict()
    {
        var resolver = new SimpleConflictResolver(_logService, _extractor);

        var mods = new List<Mod>
        {
            ModWith("Lower", 1, @"meshes/armor/steel.nif"),
            ModWith("Upper", 2, @"Meshes\Armor\Steel.nif"),
        };

        Assert.Single(ConflictsOf(resolver.GetConflictReport(mods), "Lower"));
    }

    public void Dispose() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
}

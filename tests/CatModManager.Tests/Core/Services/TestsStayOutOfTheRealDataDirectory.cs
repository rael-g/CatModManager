using System;
using System.IO;
using CatModManager.Core.Services;
using CatModManager.Tests.Support;
using Xunit;

namespace CatModManager.Tests.Core.Services;

/// <summary>
/// The suite must never touch the developer's own data directory. It did: one test constructed the
/// real <see cref="CatPathService"/> and overwrote their LastProfileName, the UI tests boot the
/// application's real DI container, and <c>LogService</c> appended to their real cmm.log.
///
/// These assertions are about the mechanism rather than any one call site, because patching call
/// sites is what failed the first time — a new test that news up a service is all it takes to
/// reopen the hole, and nothing in a review reliably catches that.
/// </summary>
public class TestsStayOutOfTheRealDataDirectory
{
    /// <summary>The location a normal installation uses, computed without the override.</summary>
    private static string RealDataHome => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "catmodmanager");

    [Fact]
    public void TheDefaultPathServiceResolvesToTheSandbox()
    {
        Assert.Equal(TestDataHome.Path, new CatPathService().BaseDataPath);
    }

    /// <summary>
    /// The path every service derives its own files from — including AppDatabase, which now
    /// migrates the schema of whatever database it is pointed at.
    /// </summary>
    [Fact]
    public void NoDataDirectoryResolvesInsideTheRealOne()
    {
        Assert.False(CatPathService.ResolveDataHome().StartsWith(RealDataHome, StringComparison.Ordinal),
                     $"Tests are resolving into the real data directory ({RealDataHome}).");
    }

    /// <summary>
    /// LogService is constructed before there is a path service to inject, so it resolved the data
    /// directory on its own and kept writing to the real log while everything else was sandboxed.
    /// </summary>
    [Fact]
    public void TheDefaultLogGoesToTheSandbox()
    {
        new LogService().Log("guard");

        Assert.True(File.Exists(Path.Combine(TestDataHome.Path, "logs", "cmm.log")),
                    "The default log did not land in the sandbox.");
    }

    /// <summary>
    /// The database is derived from the path service rather than hardcoded, so this passes for the
    /// right reason — but it is the file with the most to lose, and worth stating outright.
    /// </summary>
    [Fact]
    public void TheDatabaseIsCreatedInTheSandbox()
    {
        var paths = new CatPathService();
        _ = new AppDatabase(paths);

        Assert.NotEqual(RealDataHome, paths.BaseDataPath);
        Assert.True(File.Exists(Path.Combine(TestDataHome.Path, "cmm.db")));
    }
}

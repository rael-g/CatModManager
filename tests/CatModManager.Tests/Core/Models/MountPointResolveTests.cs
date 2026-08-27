using System;
using System.IO;
using CatModManager.Core.Models;
using Xunit;

namespace CatModManager.Tests.Core.Models;

/// <summary>
/// One resolver, five former call sites. These pin the edge cases those copies disagreed about, so
/// consolidating them cannot quietly change where mods get deployed.
/// </summary>
public class MountPointResolveTests
{
    [Fact]
    public void ARelativePathIsResolvedAgainstTheGameFolder()
        => Assert.Equal(Path.Combine("/games/starfield", "Data"),
                        MountPointDef.Resolve("Data", "/games/starfield"));

    [Fact]
    public void AnAbsolutePathIsUsedAsIs()
        => Assert.Equal("/somewhere/else", MountPointDef.Resolve("/somewhere/else", "/games/starfield"));

    [Fact]
    public void AnEmptyPathMeansTheGameFolderItself()
    {
        Assert.Equal("/games/starfield", MountPointDef.Resolve("", "/games/starfield"));
        Assert.Equal("/games/starfield", MountPointDef.Resolve(null, "/games/starfield"));
    }

    [Fact]
    public void EnvironmentVariablesAreExpanded()
    {
        // The conflict resolver's copy skipped this step, so a mount point written with a variable
        // deployed to a literal folder named after the variable.
        Environment.SetEnvironmentVariable("CMM_TEST_ROOT", "/expanded");
        try
        {
            Assert.Equal("/expanded", MountPointDef.Resolve("%CMM_TEST_ROOT%", "/games/starfield"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CMM_TEST_ROOT", null);
        }
    }

    [Fact]
    public void ARelativePathWithNoGameFolderIsLeftAlone()
    {
        // Deliberately not rooted at the process's working directory, which is wherever the app
        // happened to be launched from and has nothing to do with the game.
        Assert.Equal("Data", MountPointDef.Resolve("Data", null));
        Assert.Equal("Data", MountPointDef.Resolve("Data", ""));
    }

    [Fact]
    public void TheInstanceMethodResolvesItsOwnPath()
        => Assert.Equal(Path.Combine("/games/starfield", "Data"),
                        new MountPointDef("data", "Data", "Data").ResolveAbsolute("/games/starfield"));
}

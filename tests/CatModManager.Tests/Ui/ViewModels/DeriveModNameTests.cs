using System.IO;
using CatModManager.Ui.ViewModels;
using Xunit;

namespace CatModManager.Tests.Ui.ViewModels;

/// <summary>
/// Where a mod's name comes from. Adding one from a folder produced a blank name, because the
/// folder picker hands back a trailing separator and the file-oriented helper returns empty for it.
/// </summary>
public class DeriveModNameTests
{
    [Fact]
    public void AFolderPathWithATrailingSeparatorStillHasAName()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cmm-name-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(Path.GetFileName(dir),
                         ModInstallationCoordinator.DeriveModName(dir + Path.DirectorySeparatorChar));
        }
        finally { Directory.Delete(dir); }
    }

    [Fact]
    public void AFolderKeepsTheDotsInItsName()
    {
        // "Better Combat v1.2" is not an extension. The old code left "Better Combat v1".
        string dir = Path.Combine(Path.GetTempPath(), "cmm-name-test-v1.2." + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(Path.GetFileName(dir), ModInstallationCoordinator.DeriveModName(dir));
        }
        finally { Directory.Delete(dir); }
    }

    [Fact]
    public void AnArchiveLosesItsExtension()
        => Assert.Equal("SkyUI", ModInstallationCoordinator.DeriveModName("/downloads/SkyUI.7z"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    public void SomethingUnusableFallsBackToAPlaceholderRatherThanABlank(string path)
        => Assert.Equal("Mod", ModInstallationCoordinator.DeriveModName(path));
}

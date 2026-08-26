using CmmPlugin.NexusMods;
using Xunit;

namespace CatModManager.Tests.Plugins.NexusMods;

/// <summary>
/// The saved archive's name becomes the installed mod's folder name, so an unreadable download name
/// leaves an unidentifiable mods folder behind. Nexus serves an opaque UUID path for some files,
/// which is how folders named "150cdeff-9d30-4a7f-95c3-22918ef8d281" got there.
/// </summary>
public class DownloadFileNamingTests
{
    private static DownloadEntry Entry(string modName, int modId = 3444, int fileId = 99, string version = "1.3") =>
        new() { ModName = modName, ModId = modId, FileId = fileId, Version = version };

    [Fact]
    public void UsesApiMetadata_InsteadOfAnOpaqueCdnName()
    {
        string name = NexusDownloadService.BuildFileName(
            Entry("StarUI HUD"),
            "https://cdn.nexusmods.com/150cdeff-9d30-4a7f-95c3-22918ef8d281.7z");

        Assert.Equal("StarUI HUD-3444-1.3-99.7z", name);
    }

    [Fact]
    public void KeepsTheArchiveExtensionFromTheUrl()
    {
        Assert.EndsWith(".rar", NexusDownloadService.BuildFileName(Entry("Some Mod"), "https://cdn/x/file.rar"));
    }

    [Fact]
    public void FallsBackToZip_WhenTheUrlCarriesNoExtension()
    {
        Assert.EndsWith(".zip", NexusDownloadService.BuildFileName(Entry("Some Mod"), "https://cdn/x/abcdef"));
    }

    [Fact]
    public void StripsCharactersTheFilesystemRejects()
    {
        string name = NexusDownloadService.BuildFileName(Entry("Bad/Name: v2"), "https://cdn/x/f.7z");

        Assert.DoesNotContain('/', name);
        Assert.StartsWith("Bad_Name", name);
    }

    [Fact]
    public void FallsBackToTheUrlName_WhenTheApiGaveNoModName()
    {
        // Better an opaque-but-real name than an invented one that collides with everything.
        string name = NexusDownloadService.BuildFileName(
            new DownloadEntry { ModName = "", ModId = 0, FileId = 0, Version = "" },
            "https://cdn.nexusmods.com/original-name.7z");

        Assert.Equal("original-name.7z", name);
    }

    [Fact]
    public void SameFileTwice_ProducesTheSamePath_SoRedownloadOverwrites()
    {
        var a = NexusDownloadService.BuildFileName(Entry("Mod"), "https://cdn/x/aaa.7z");
        var b = NexusDownloadService.BuildFileName(Entry("Mod"), "https://cdn/x/bbb.7z");

        Assert.Equal(a, b);
    }

    [Fact]
    public void VariantsOfOneModAtTheSameVersion_DoNotCollide()
    {
        // Two files of one mod can share a version, so the name alone is not unique.
        var a = NexusDownloadService.BuildFileName(Entry("Mod", fileId: 1), "https://cdn/x/a.7z");
        var b = NexusDownloadService.BuildFileName(Entry("Mod", fileId: 2), "https://cdn/x/a.7z");

        Assert.NotEqual(a, b);
    }
}

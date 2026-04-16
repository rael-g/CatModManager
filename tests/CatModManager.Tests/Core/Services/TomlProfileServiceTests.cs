using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using NSubstitute;
using CatModManager.Core.Models;
using CatModManager.Core.Services;

namespace CatModManager.Tests.Core.Services;

public class TomlProfileServiceTests
{
    private readonly IFileService _fileService;
    private readonly TomlProfileService _service;

    public TomlProfileServiceTests()
    {
        _fileService = Substitute.For<IFileService>();
        _service = new TomlProfileService(_fileService);
    }

    [Fact]
    public async Task SaveProfileAsync_WritesCorrectToml()
    {
        var profile = new Profile { Name = "TestProfile" };
        string path = "profile.toml";

        await _service.SaveProfileAsync(profile, path);

        _fileService.Received(1).WriteAllText(path, Arg.Is<string>(s => s.Contains("Name = \"TestProfile\"")));
    }

    [Fact]
    public async Task LoadProfileAsync_CleansLegacyCancelCommand()
    {
        string path = "legacy.toml";
        string legacyToml = @"
Name = ""Legacy""
CancelInstallCommand = ""Something""
[[Mods]]
Name = ""Mod1""";

        _fileService.FileExists(path).Returns(true);
        _fileService.ReadAllText(path).Returns(legacyToml);

        var profile = await _service.LoadProfileAsync(path);

        Assert.NotNull(profile);
        Assert.Equal("Legacy", profile.Name);
        Assert.Single(profile.Mods);
    }

    [Fact]
    public async Task ListProfilesAsync_ReturnsNamesWithoutExtension()
    {
        string dir = "profiles";
        _fileService.DirectoryExists(dir).Returns(true);
        _fileService.GetFiles(dir, "*.toml").Returns(new[] { "profiles/A.toml", "profiles/B.toml" });

        var results = (await _service.ListProfilesAsync(dir)).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains("A", results);
        Assert.Contains("B", results);
    }
}

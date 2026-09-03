using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Tests.Support;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CatModManager.Tests.Core.Services;

/// <summary>
/// The one-shot carry-over of the .toml profiles an existing installation already has. Every user
/// upgrading runs this exactly once, and there is no second chance if it drops a profile.
/// </summary>
public class ProfileImporterTests : IDisposable
{
    private readonly string               _dir;
    private readonly MockCatPathService   _paths;
    private readonly SqliteProfileService _profiles;
    private readonly SqliteGameService    _games;
    private readonly TomlProfileService   _toml = new(new CatModManager.PluginSdk.PhysicalFileService());

    public ProfileImporterTests()
    {
        _dir   = Path.Combine(Path.GetTempPath(), "CMM_Import_" + Guid.NewGuid().ToString("N"));
        _paths = new MockCatPathService(_dir);
        Directory.CreateDirectory(_paths.ProfilesPath);
        var db = new AppDatabase(_paths);
        _profiles = new SqliteProfileService(db);
        _games    = new SqliteGameService(db);
    }

    private ProfileImporter NewImporter()
        => new(_profiles, _games, _toml, _paths, new MockLogService());

    private Task WriteTomlAsync(string name, LegacyTomlProfile profile)
        => _toml.SaveProfileAsync(profile, Path.Combine(_paths.ProfilesPath, name + ".toml"));

    private async Task<string[]> StoredNames()
        => (await _profiles.ListAllProfilesAsync()).Select(p => p.Name).OrderBy(n => n).ToArray();

    [Fact]
    public async Task TheExistingTomlProfilesEndUpInTheDatabase()
    {
        var profile = new LegacyTomlProfile
        {
            Name           = "Starfield",
            BaseDataPath   = "/games/Starfield",
            ModsFolderPath = "/games/Starfield/cmm/mods",
        };
        profile.Mods.Add(new Mod("FasterMining", "/mods/FasterMining", 0));
        await WriteTomlAsync("Starfield", profile);
        await WriteTomlAsync("Skyrim", new LegacyTomlProfile { Name = "Skyrim" });

        await NewImporter().ImportIfEmptyAsync();

        Assert.Equal(new[] { "Skyrim", "Starfield" }, await StoredNames());

        // The paths came in as a game of their own, which is where they live now.
        var game = await _games.FindByBasePathAsync("/games/Starfield");
        Assert.Equal("/games/Starfield/cmm/mods", game!.ModsFolderPath);

        var summary = (await _profiles.ListProfilesAsync(game.Id)).Single();
        var imported = await _profiles.LoadProfileAsync(summary.Id);
        Assert.Equal("FasterMining", Assert.Single(imported!.Mods).Name);
    }

    /// <summary>
    /// The user's only copy of their profiles predates this change. Deleting the files in the same
    /// release that debuts the code replacing them leaves nothing to fall back to.
    /// </summary>
    [Fact]
    public async Task TheTomlFilesAreLeftAlone()
    {
        await WriteTomlAsync("Starfield", new LegacyTomlProfile { Name = "Starfield" });

        await NewImporter().ImportIfEmptyAsync();

        Assert.True(File.Exists(Path.Combine(_paths.ProfilesPath, "Starfield.toml")));
    }

    /// <summary>
    /// Startup runs the importer unconditionally, so the second launch must not resurrect a profile
    /// the user deleted, nor overwrite the edits they made since the first one.
    /// </summary>
    [Fact]
    public async Task ASecondRunImportsNothing()
    {
        await WriteTomlAsync("Starfield", new LegacyTomlProfile { Name = "Starfield" });
        await NewImporter().ImportIfEmptyAsync();

        var imported = (await _profiles.ListAllProfilesAsync()).Single();
        await _profiles.DeleteProfileAsync(imported.Id);
        await _profiles.SaveProfileAsync(new Profile { Name = "Made in the app" });

        await NewImporter().ImportIfEmptyAsync();

        Assert.Equal(new[] { "Made in the app" }, await StoredNames());
    }

    /// <summary>
    /// The file name is what the UI listed and what LastProfileName refers to. A Name field that
    /// drifted from it would otherwise import the profile under a name nothing points at.
    /// </summary>
    [Fact]
    public async Task TheFileNameWinsOverTheNameInsideTheFile()
    {
        await WriteTomlAsync("What the user sees",
                             new LegacyTomlProfile { Name = "something else entirely" });

        await NewImporter().ImportIfEmptyAsync();

        Assert.Equal(new[] { "What the user sees" }, await StoredNames());
    }

    [Fact]
    public async Task AFreshInstallWithNoFilesImportsNothing()
    {
        await NewImporter().ImportIfEmptyAsync();

        Assert.Empty(await _profiles.ListAllProfilesAsync());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }
}

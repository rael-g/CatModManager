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
/// Uninstalling deletes the mod's folder from disk, so it is a change to the game's inventory and
/// not an opinion of one profile. Routing it through a profile save could not express that: the
/// prune there spares any row another profile still refers to, which is right for a mod merely
/// dropped from a list and wrong for one whose files are gone. The row survived, and the next load
/// handed it back to the profile it had just been deleted from.
/// </summary>
public class UninstallModTests : IDisposable
{
    private readonly string               _dir;
    private readonly SqliteProfileService _service;
    private readonly long                 _game;

    public UninstallModTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "CMM_Uninstall_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        var db = new AppDatabase(new MockCatPathService(_dir));
        _service = new SqliteProfileService(db);

        _game = new SqliteGameService(db).SaveGameAsync(new Game
        {
            DisplayName  = "Starfield",
            BaseDataPath = "/games/Starfield",
        }).GetAwaiter().GetResult();
    }

    private Task<long> SaveProfile(string name, params string[] mods) =>
        _service.SaveProfileAsync(new Profile
        {
            Name   = name,
            GameId = _game,
            Mods   = mods.Select((m, i) => new Mod(m, "/mods/" + m, i) { IsEnabled = true }).ToList()
        });

    /// <summary>
    /// The bug as reported: delete a mod with two profiles around, reopen, and it is back in both.
    /// </summary>
    [Fact]
    public async Task AnUninstalledModDoesNotComeBackOnTheNextLoad()
    {
        long alpha = await SaveProfile("Alpha", "FasterMining", "SFSE");
        await SaveProfile("Beta", "FasterMining", "SFSE");

        await _service.UninstallModAsync(_game, "/mods/SFSE");

        var reloaded = await _service.LoadProfileAsync(alpha);
        Assert.DoesNotContain(reloaded!.Mods, m => m.Name == "SFSE");
    }

    /// <summary>
    /// The other half, and the reason a profile save could never do this: the mod has to leave the
    /// profile the user was not looking at too, because its files are gone for that one as well.
    /// </summary>
    [Fact]
    public async Task ItLeavesTheOtherProfileToo()
    {
        await SaveProfile("Alpha", "FasterMining", "SFSE");
        long beta = await SaveProfile("Beta", "FasterMining", "SFSE");

        await _service.UninstallModAsync(_game, "/mods/SFSE");

        var reloaded = await _service.LoadProfileAsync(beta);
        Assert.DoesNotContain(reloaded!.Mods, m => m.Name == "SFSE");
    }

    [Fact]
    public async Task TheModsAroundItAreLeftAlone()
    {
        long alpha = await SaveProfile("Alpha", "FasterMining", "SFSE", "StarUI");

        await _service.UninstallModAsync(_game, "/mods/SFSE");

        var reloaded = await _service.LoadProfileAsync(alpha);
        Assert.Equal(new[] { "FasterMining", "StarUI" }, reloaded!.Mods.Select(m => m.Name));
        Assert.All(reloaded.Mods, m => Assert.True(m.IsEnabled));
    }

    /// <summary>Two games can hold folders of the same name; one uninstall must not reach both.</summary>
    [Fact]
    public async Task AnotherGamesCopyOfTheSamePathIsUntouched()
    {
        var db      = new AppDatabase(new MockCatPathService(_dir));
        long other  = await new SqliteGameService(db).SaveGameAsync(new Game
        {
            DisplayName = "Fallout", BaseDataPath = "/games/Fallout"
        });

        long theirs = await _service.SaveProfileAsync(new Profile
        {
            Name = "Fallout", GameId = other,
            Mods = { new Mod("SFSE", "/mods/SFSE", 0) { IsEnabled = true } }
        });

        await SaveProfile("Alpha", "SFSE");
        await _service.UninstallModAsync(_game, "/mods/SFSE");

        var reloaded = await _service.LoadProfileAsync(theirs);
        Assert.Contains(reloaded!.Mods, m => m.Name == "SFSE");
    }

    /// <summary>
    /// The mechanism the bug rode on, pinned so it cannot be mistaken for a fix later: simply saving
    /// the profile without the mod does not remove it. The prune spares the row because Beta still
    /// refers to it, and the tail of the next load hands it straight back. This is correct for a save
    /// — which is exactly why uninstall had to stop being one.
    /// </summary>
    [Fact]
    public async Task SavingTheProfileWithoutTheModIsNotEnoughToRemoveIt()
    {
        long alpha = await SaveProfile("Alpha", "FasterMining", "SFSE");
        await SaveProfile("Beta", "FasterMining", "SFSE");

        // What RemoveMod used to do on its own: drop it from this profile's list and save.
        await _service.SaveProfileAsync(new Profile
        {
            Id = alpha, Name = "Alpha", GameId = _game,
            Mods = { new Mod("FasterMining", "/mods/FasterMining", 0) { IsEnabled = true } }
        });

        var reloaded = await _service.LoadProfileAsync(alpha);
        Assert.Contains(reloaded!.Mods, m => m.Name == "SFSE");
    }

    /// <summary>Uninstalling something already gone is what a retry looks like, not an error.</summary>
    [Fact]
    public async Task UninstallingAModThatIsNotThereIsNotAnError()
    {
        long alpha = await SaveProfile("Alpha", "FasterMining");

        await _service.UninstallModAsync(_game, "/mods/NeverInstalled");

        var reloaded = await _service.LoadProfileAsync(alpha);
        Assert.Single(reloaded!.Mods);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}

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
/// Profiles in cmm.db, in place of the per-profile TOML file.
/// </summary>
public class SqliteProfileServiceTests : IDisposable
{
    private readonly string               _dir;
    private readonly SqliteProfileService _service;
    private readonly SqliteGameService    _games;
    private readonly long                 _starfield;

    public SqliteProfileServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "CMM_Profiles_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        var db = new AppDatabase(new MockCatPathService(_dir));
        _service = new SqliteProfileService(db);
        _games   = new SqliteGameService(db);

        _starfield = NewGame("/games/Starfield", "starfield").GetAwaiter().GetResult();
    }

    private Task<long> NewGame(string basePath, string supportId = "generic")
        => _games.SaveGameAsync(new Game
        {
            DisplayName         = Path.GetFileName(basePath),
            BaseDataPath        = basePath,
            ModsFolderPath      = basePath + "/cmm/mods",
            DownloadsFolderPath = basePath + "/cmm/downloads",
            GameExecutablePath  = basePath + "/game.exe",
            GameSupportId       = supportId,
            LaunchArguments     = "-windowed",
            UserMountPoints     = { new MountPointDef("data", "Data", "Data") },
        });

    private Profile Sample(string name = "Starfield", long? gameId = null) => new()
    {
        Name   = name,
        GameId = gameId ?? _starfield,
        Mods =
        {
            new Mod("FasterMining", "/mods/FasterMining", 0) { Category = "Gameplay", Version = "2.1" },
            new Mod("SFSE", "/mods/SFSE", 1) { IsEnabled = false, MountPointId = "root" },
            new Mod { Name = "── UI ──", Priority = 2, IsSeparator = true },
        },
        ExternalTools = { new ExternalTool { Name = "xEdit", ExecutablePath = "wine",
                                             Arguments = "xEdit.exe", MountBeforeLaunch = true } },
    };

    [Fact]
    public async Task ASavedProfileComesBackWhole()
    {
        long id = await _service.SaveProfileAsync(Sample());

        var loaded = await _service.LoadProfileAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal("Starfield", loaded!.Name);
        Assert.Equal(_starfield, loaded.GameId);

        Assert.Equal(new[] { "FasterMining", "SFSE", "── UI ──" }, loaded.Mods.Select(m => m.Name));
        Assert.False(loaded.Mods[1].IsEnabled);
        Assert.Equal("root", loaded.Mods[1].MountPointId);
        Assert.Null(loaded.Mods[0].MountPointId);
        Assert.True(loaded.Mods[2].IsSeparator);
        Assert.Equal("Gameplay", loaded.Mods[0].Category);

        Assert.Equal("xEdit", Assert.Single(loaded.ExternalTools).Name);
        Assert.True(loaded.ExternalTools[0].MountBeforeLaunch);
    }

    [Fact]
    public async Task AnUnknownProfileIsNull()
    {
        // Not an empty Profile named after the request. The TOML service returned one of those on a
        // parse failure, and the next autosave wrote it over the real thing.
        Assert.Null(await _service.LoadProfileAsync(4242));
    }

    /// <summary>
    /// Save is delete-then-insert, so the risk is the delete half: a shorter list has to shrink.
    /// </summary>
    [Fact]
    public async Task SavingAgainReplacesTheListRatherThanAppendingToIt()
    {
        long id = await _service.SaveProfileAsync(Sample());

        var second = Sample();
        second.Id = id;
        second.Mods.RemoveAt(2);
        second.ExternalTools.Clear();
        await _service.SaveProfileAsync(second);

        var loaded = await _service.LoadProfileAsync(id);
        Assert.Equal(2, loaded!.Mods.Count);
        Assert.Empty(loaded.ExternalTools);
    }

    /// <summary>
    /// The plan keyed profile_mods on (profile_name, priority). Priority is a plain settable
    /// property and nothing renumbers it, so a profile with ties would have lost a mod on save.
    /// </summary>
    [Fact]
    public async Task TwoModsWithTheSamePrioritySurvive()
    {
        var profile = new Profile { Name = "Tied", GameId = _starfield };
        profile.Mods.Add(new Mod("A", "/a", 0));
        profile.Mods.Add(new Mod("B", "/b", 0));

        long id = await _service.SaveProfileAsync(profile);

        var loaded = await _service.LoadProfileAsync(id);
        Assert.Equal(new[] { "A", "B" }, loaded!.Mods.Select(m => m.Name));
    }

    [Fact]
    public async Task ListingReturnsNamesInOrder()
    {
        foreach (var name in new[] { "Zeta", "Alpha", "Mid" })
            await _service.SaveProfileAsync(new Profile { Name = name, GameId = _starfield });

        Assert.Equal(new[] { "Alpha", "Mid", "Zeta" },
                     (await _service.ListProfilesAsync(_starfield)).Select(p => p.Name));
    }

    /// <summary>
    /// The list is per game. Listing everything is the importer's question — "has anything ever been
    /// stored" — and not a question the window asks.
    /// </summary>
    [Fact]
    public async Task ListingIsScopedToOneGame()
    {
        long skyrim = await NewGame("/games/Skyrim");

        await _service.SaveProfileAsync(new Profile { Name = "Default", GameId = _starfield });
        await _service.SaveProfileAsync(new Profile { Name = "Default", GameId = skyrim });
        await _service.SaveProfileAsync(new Profile { Name = "Parked" });

        Assert.Equal("Default", Assert.Single(await _service.ListProfilesAsync(_starfield)).Name);
        Assert.Equal("Parked",  Assert.Single(await _service.ListProfilesAsync(null)).Name);
        Assert.Equal(3, (await _service.ListAllProfilesAsync()).Count);
    }

    /// <summary>
    /// The reason the profile is keyed on a row rather than on its name. With a game-first flow every
    /// game wants a profile called "Default", and under the old schema the second one to be saved
    /// overwrote the first.
    /// </summary>
    [Fact]
    public async Task TwoGamesCanEachHaveAProfileOfTheSameName()
    {
        long skyrim = await NewGame("/games/Skyrim");

        long a = await _service.SaveProfileAsync(new Profile { Name = "Default", GameId = _starfield });
        long b = await _service.SaveProfileAsync(new Profile { Name = "Default", GameId = skyrim });

        Assert.NotEqual(a, b);
        Assert.Equal(_starfield, (await _service.LoadProfileAsync(a))!.GameId);
        Assert.Equal(skyrim,     (await _service.LoadProfileAsync(b))!.GameId);
    }

    [Fact]
    public async Task DeletingTakesTheModsWithIt()
    {
        long id = await _service.SaveProfileAsync(Sample());
        await _service.DeleteProfileAsync(id);

        Assert.Null(await _service.LoadProfileAsync(id));
        Assert.Empty(await _service.ListProfilesAsync(_starfield));
        Assert.Equal(0, CountRows("profile_entries"));
        Assert.Equal(0, CountRows("profile_tools"));

        // The game and its inventory outlive the profile on purpose: the mods are still installed on
        // disk, and another profile of the same game may well be using them. Deleting a profile is
        // not uninstalling a game.
        Assert.Equal(1, CountRows("games"));
        Assert.Equal(2, CountRows("game_mods"));
    }

    [Fact]
    public async Task DeletingSomethingThatIsNotThereIsNotAnError()
    {
        await _service.DeleteProfileAsync(9999);
    }

    /// <summary>
    /// Rename used to be "save under the new name, then delete the old file" — two steps, and the
    /// profile is duplicated or lost if only one of them lands. It is one update now, because the
    /// child rows point at the id and never saw the name.
    /// </summary>
    [Fact]
    public async Task RenamingCarriesTheModsAndLeavesNothingBehind()
    {
        long id = await _service.SaveProfileAsync(Sample());

        await _service.RenameProfileAsync(id, "Starfield 2");

        Assert.Equal(new[] { "Starfield 2" },
                     (await _service.ListProfilesAsync(_starfield)).Select(p => p.Name));
        var loaded = await _service.LoadProfileAsync(id);
        Assert.Equal(3, loaded!.Mods.Count);
        Assert.Equal("xEdit", Assert.Single(loaded.ExternalTools).Name);
    }

    // ── Game / profile split ──────────────────────────────────────────────────

    /// <summary>
    /// The whole point of the split. A mod installed while one profile was open used to be invisible
    /// to the other, which is what made switching profiles look like it had thrown the mods away.
    /// </summary>
    [Fact]
    public async Task AModInstalledUnderOneProfileShowsUpInTheOtherProfileOfTheSameGame()
    {
        long vanillaId = await _service.SaveProfileAsync(Sample("Vanilla"));
        long heavyId   = await _service.SaveProfileAsync(Sample("Heavy"));

        var heavy = (await _service.LoadProfileAsync(heavyId))!;
        heavy.Mods.Add(new Mod("NewMod", "/mods/NewMod", 9));
        await _service.SaveProfileAsync(heavy);

        var vanilla = (await _service.LoadProfileAsync(vanillaId))!;
        var carried = Assert.Single(vanilla.Mods.Where(m => m.Name == "NewMod"));

        // Unticked: appearing in a profile the user was not looking at is fine, changing how their
        // game runs without being asked is not.
        Assert.False(carried.IsEnabled);
    }

    /// <summary>Two profiles of one game are two views of one installation.</summary>
    [Fact]
    public async Task ProfilesOfTheSameGameShareOneGameRowAndOneInventory()
    {
        await _service.SaveProfileAsync(Sample("Vanilla"));
        await _service.SaveProfileAsync(Sample("Heavy"));

        Assert.Equal(1, CountRows("games"));
        Assert.Equal(2, CountRows("game_mods"));
    }

    [Fact]
    public async Task ProfilesOfDifferentGamesDoNotShare()
    {
        long skyrim = await NewGame("/games/Skyrim");

        long starfieldId = await _service.SaveProfileAsync(Sample("Starfield"));
        await _service.SaveProfileAsync(Sample("Skyrim", skyrim));

        Assert.Equal(2, CountRows("games"));
        Assert.Equal(4, CountRows("game_mods"));

        Assert.Equal(3, (await _service.LoadProfileAsync(starfieldId))!.Mods.Count);
    }

    /// <summary>
    /// Enabling and reordering are the profile's, not the game's — that is the half of the old
    /// Mod row that did not move.
    /// </summary>
    [Fact]
    public async Task EachProfileKeepsItsOwnSelectionAndOrder()
    {
        long vanillaId = await _service.SaveProfileAsync(Sample("Vanilla"));
        long heavyId   = await _service.SaveProfileAsync(Sample("Heavy"));

        var vanilla = (await _service.LoadProfileAsync(vanillaId))!;
        foreach (var mod in vanilla.Mods) mod.IsEnabled = false;
        await _service.SaveProfileAsync(vanilla);

        Assert.All((await _service.LoadProfileAsync(vanillaId))!.Mods, m => Assert.False(m.IsEnabled));
        Assert.True((await _service.LoadProfileAsync(heavyId))!.Mods[0].IsEnabled);
    }

    /// <summary>
    /// A profile that belongs to no game is parked, not broken. The migration notes name two real
    /// ones on the developer's machine — Modded and NewProfile.
    /// </summary>
    [Fact]
    public async Task AProfileWithNoGameStillSavesAndLoads()
    {
        long id = await _service.SaveProfileAsync(new Profile { Name = "Modded" });

        var loaded = await _service.LoadProfileAsync(id);
        Assert.NotNull(loaded);
        Assert.Null(loaded!.GameId);
        Assert.Empty(loaded.Mods);
    }

    /// <summary>
    /// Separators are the user's own labels: no files, no game, but a place in the list. They are
    /// the reason profile_entries.game_mod_id is nullable.
    /// </summary>
    [Fact]
    public async Task SeparatorsStayWithTheProfileAndNotTheInventory()
    {
        long vanillaId = await _service.SaveProfileAsync(Sample("Vanilla"));
        await _service.SaveProfileAsync(Sample("Heavy"));

        Assert.Equal(2, CountRows("game_mods"));

        var vanilla = (await _service.LoadProfileAsync(vanillaId))!;
        Assert.Equal("── UI ──", vanilla.Mods[2].Name);
        Assert.True(vanilla.Mods[2].IsSeparator);
    }

    /// <summary>
    /// Caught on the developer's real profiles, not by reasoning. NewProfile and Starfield shared a
    /// game and had different mods; saving one deleted the other's from the inventory, and the
    /// cascade silently emptied that profile.
    /// </summary>
    [Fact]
    public async Task SavingOneProfileDoesNotDeleteAnotherProfilesMods()
    {
        var mine = Sample("Mine");
        mine.Mods.Clear();
        mine.Mods.Add(new Mod("OnlyMine", "/mods/OnlyMine", 0));
        long mineId = await _service.SaveProfileAsync(mine);

        // Same game, a completely different list — which is what an import produces, because each
        // profile is written from its own file rather than from the shared inventory.
        var theirs = Sample("Theirs");
        theirs.Mods.Clear();
        theirs.Mods.Add(new Mod("OnlyTheirs", "/mods/OnlyTheirs", 0));
        await _service.SaveProfileAsync(theirs);

        var reloaded = (await _service.LoadProfileAsync(mineId))!;
        var kept = Assert.Single(reloaded.Mods.Where(m => m.Name == "OnlyMine"));
        Assert.True(kept.IsEnabled, "The profile's own mod came back unticked.");
    }

    /// <summary>
    /// The other half of the rule: removing a mod really does uninstall it, and once nothing refers
    /// to it the inventory row has to go, or the list keeps showing a folder that is not there.
    /// </summary>
    [Fact]
    public async Task AModNoProfileListsAnyMoreLeavesTheInventory()
    {
        long id = await _service.SaveProfileAsync(Sample());

        var trimmed = (await _service.LoadProfileAsync(id))!;
        trimmed.Mods.RemoveAll(m => m.Name == "SFSE");
        await _service.SaveProfileAsync(trimmed);

        Assert.Equal(1, CountRows("game_mods"));
        Assert.DoesNotContain("SFSE", (await _service.LoadProfileAsync(id))!.Mods.Select(m => m.Name));
    }

    private long CountRows(string table)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(_dir, "cmm.db")}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }
}

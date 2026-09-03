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
/// Games as rows of their own, which is what the game-first window is built on.
/// </summary>
public class SqliteGameServiceTests : IDisposable
{
    private readonly string               _dir;
    private readonly SqliteGameService    _games;
    private readonly SqliteProfileService _profiles;

    public SqliteGameServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "CMM_Games_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        var db = new AppDatabase(new MockCatPathService(_dir));
        _games    = new SqliteGameService(db);
        _profiles = new SqliteProfileService(db);
    }

    private static Game Sample(string name = "Starfield", string? basePath = null) => new()
    {
        DisplayName         = name,
        BaseDataPath        = basePath ?? "/games/" + name,
        ModsFolderPath      = (basePath ?? "/games/" + name) + "/cmm/mods",
        DownloadsFolderPath = (basePath ?? "/games/" + name) + "/cmm/downloads",
        GameExecutablePath  = (basePath ?? "/games/" + name) + "/game.exe",
        GameSupportId       = "starfield",
    };

    [Fact]
    public async Task ASavedGameComesBackWhole()
    {
        long id = await _games.SaveGameAsync(Sample());

        var loaded = await _games.LoadGameAsync(id);

        Assert.Equal("Starfield", loaded!.DisplayName);
        Assert.Equal("/games/Starfield", loaded.BaseDataPath);
        Assert.Equal("/games/Starfield/cmm/mods", loaded.ModsFolderPath);
        Assert.Equal("/games/Starfield/cmm/downloads", loaded.DownloadsFolderPath);
        Assert.Equal("/games/Starfield/game.exe", loaded.GameExecutablePath);
        Assert.Equal("starfield", loaded.GameSupportId);
    }

    [Fact]
    public async Task SavingAnExistingGameUpdatesItRatherThanAddingASecond()
    {
        long id = await _games.SaveGameAsync(Sample());

        var edited = await _games.LoadGameAsync(id);
        edited!.ModsFolderPath = "/elsewhere/mods";
        await _games.SaveGameAsync(edited);

        var game = Assert.Single(await _games.ListGamesAsync());
        Assert.Equal("/elsewhere/mods", game.ModsFolderPath);
    }

    /// <summary>
    /// What stops "Add Game…" from adding an installation the user already manages twice. Two games
    /// over one folder would be two inventories over one mods folder.
    /// </summary>
    [Fact]
    public async Task AGameIsFoundByItsFolder()
    {
        long id = await _games.SaveGameAsync(Sample());

        Assert.Equal(id, (await _games.FindByBasePathAsync("/games/Starfield"))!.Id);
        Assert.Null(await _games.FindByBasePathAsync("/games/SomethingElse"));

        // A game with no folder yet identifies nothing, and must not match every other one like it.
        await _games.SaveGameAsync(new Game { DisplayName = "Half configured" });
        Assert.Null(await _games.FindByBasePathAsync(""));
    }

    /// <summary>
    /// Removing a game takes its profiles and its record of installed mods, and nothing else. The
    /// mods stay on disk — adding the game back is meant to find them again.
    /// </summary>
    [Fact]
    public async Task DeletingAGameTakesItsProfilesAndInventoryOnly()
    {
        long starfield = await _games.SaveGameAsync(Sample());
        long skyrim    = await _games.SaveGameAsync(Sample("Skyrim"));

        var doomed = new Profile { Name = "Default", GameId = starfield };
        doomed.Mods.Add(new Mod("SFSE", "/mods/SFSE", 0));
        await _profiles.SaveProfileAsync(doomed);

        var keeper = new Profile { Name = "Default", GameId = skyrim };
        keeper.Mods.Add(new Mod("SKSE", "/mods/SKSE", 0));
        long keeperId = await _profiles.SaveProfileAsync(keeper);

        await _games.DeleteGameAsync(starfield);

        Assert.Empty(await _profiles.ListProfilesAsync(starfield));
        Assert.Equal(1, CountRows("game_mods"));

        var survivor = await _profiles.LoadProfileAsync(keeperId);
        Assert.Equal("SKSE", Assert.Single(survivor!.Mods).Name);
    }

    [Fact]
    public async Task GamesAreListedByName()
    {
        foreach (var name in new[] { "Zeta", "Alpha", "Mid" })
            await _games.SaveGameAsync(Sample(name));

        Assert.Equal(new[] { "Alpha", "Mid", "Zeta" },
                     (await _games.ListGamesAsync()).Select(g => g.DisplayName));
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

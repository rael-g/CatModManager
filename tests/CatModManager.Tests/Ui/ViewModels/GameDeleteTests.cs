using System;
using System.IO;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Tests.Support;
using CatModManager.Ui.ViewModels;
using Xunit;

namespace CatModManager.Tests.Ui.ViewModels;

/// <summary>
/// Removing a game can take its mods and downloads with it, and that is the one branch in this
/// application that deletes user data on purpose. These tests pin the fork: what happens when the
/// user does not ask for it, what happens when they do, and the one case where the answer is "no"
/// even though they said yes.
/// </summary>
public class GameDeleteTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "cmm-delete-" + Guid.NewGuid().ToString("N"));

    private readonly FakeGameService    _games    = new();
    private readonly FakeProfileService _profiles = new();
    private readonly MockLogService     _log      = new();

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "something.txt"), "x");
        return path;
    }

    private async Task<(GameManagerViewModel Vm, Game Game)> SetUpAsync(
        GameDeleteChoice choice, string? baseDataPath = null)
    {
        var game = new Game
        {
            DisplayName         = "Skyrim",
            BaseDataPath        = baseDataPath ?? Folder("game"),
            ModsFolderPath      = Folder("mods"),
            DownloadsFolderPath = Folder("downloads")
        };
        await _games.SaveGameAsync(game);

        var vm = new GameManagerViewModel(_games, _profiles, _log);
        await vm.RefreshListAsync(game.Id);
        vm.ConfirmDelete = (_, _) => Task.FromResult(choice);

        return (vm, game);
    }

    [Fact]
    public async Task RecordOnlyLeavesEveryFolderOnDisk()
    {
        var (vm, game) = await SetUpAsync(GameDeleteChoice.RecordOnly);

        await vm.DeleteGame();

        Assert.True(Directory.Exists(game.ModsFolderPath));
        Assert.True(Directory.Exists(game.DownloadsFolderPath));
        Assert.Null(await _games.LoadGameAsync(game.Id));
    }

    [Fact]
    public async Task CancellingLeavesTheGameItself()
    {
        var (vm, game) = await SetUpAsync(GameDeleteChoice.Cancel);

        await vm.DeleteGame();

        Assert.NotNull(await _games.LoadGameAsync(game.Id));
        Assert.True(Directory.Exists(game.ModsFolderPath));
    }

    [Fact]
    public async Task WithFilesTakesModsAndDownloads()
    {
        var (vm, game) = await SetUpAsync(GameDeleteChoice.WithFiles);

        await vm.DeleteGame();

        Assert.False(Directory.Exists(game.ModsFolderPath));
        Assert.False(Directory.Exists(game.DownloadsFolderPath));

        // The installation is never part of the deal, whatever the user ticked.
        Assert.True(Directory.Exists(game.BaseDataPath));
    }

    /// <summary>
    /// Nothing stops the mods folder from being set to the game folder itself — or to a parent of
    /// it. A recursive delete there would erase the installation the user was only trying to stop
    /// managing, so the guard wins over the checkbox.
    /// </summary>
    [Fact]
    public async Task AFolderHoldingTheGameIsKeptEvenWhenTheUserAskedForFiles()
    {
        var shared = Folder("shared");
        var (vm, game) = await SetUpAsync(GameDeleteChoice.WithFiles, baseDataPath: shared);

        game.ModsFolderPath = shared;
        await _games.SaveGameAsync(game);
        await vm.RefreshListAsync(game.Id);

        await vm.DeleteGame();

        Assert.True(Directory.Exists(shared));
        Assert.False(Directory.Exists(game.DownloadsFolderPath));
    }
}

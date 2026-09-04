using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatModManager.Core.Models;
using CatModManager.Core.Services;

namespace CatModManager.Ui.ViewModels;

/// <summary>
/// What the user asked for when removing a game. Three outcomes rather than a bool because
/// "confirmed" is not one answer here: forgetting the installation and erasing its mods and
/// downloads are different acts, and only one of them is reversible.
/// </summary>
public enum GameDeleteChoice
{
    Cancel,
    RecordOnly,
    WithFiles
}

/// <summary>
/// How the user chose to point at the game they are adding.
///
/// <see cref="Folder"/> is not a lesser <see cref="Executable"/>: for an emulated game there is no
/// executable to pick, because the emulator is not the game — the ROM folder is.
/// </summary>
public enum GameAddMethod
{
    Detect,
    Executable,
    Folder
}

/// <summary>
/// The list of installations and which one is open.
///
/// CMM used to be profile-first: you made a profile and told it which game it was for. Every other
/// manager works the other way round — you pick the game, that gives you a profile to start from,
/// and further profiles are arrangements of that same game. This view model is the game half of
/// that, and is deliberately shaped like <see cref="ProfileManagerViewModel"/>: a list, a current
/// item, and callbacks the window fills in for anything that needs a dialog.
/// </summary>
public partial class GameManagerViewModel : ViewModelBase
{
    private readonly IGameService    _gameService;
    private readonly IProfileService _profileService;
    private readonly ILogService     _logService;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _suppressActivation;

    /// <summary>
    /// Stands in for "profiles that never had a game folder". They are not an installation, so they
    /// have no row in games — but they are still the user's profiles, and a game-first list that
    /// omitted them would be a list the user cannot reach their own work from.
    ///
    /// Negative rather than zero. Zero already means "not saved yet", and a first start — where the
    /// remembered game id is zero because nothing has ever been remembered — matched this entry and
    /// opened the parked profiles instead of the user's actual game.
    /// </summary>
    public static Game Parked => new() { Id = -1, DisplayName = "(no game)" };

    public ObservableCollection<Game> AvailableGames { get; } = new();

    [ObservableProperty] private Game? _currentGame;

    /// <summary>Raised after the current game changes. The window applies it and reloads profiles.</summary>
    public event Func<Game?, Task>? GameActivated;

    /// <summary>
    /// Set by the view: runs the dialog for the requested way of adding a game and returns it
    /// unsaved, or null if the user backed out.
    ///
    /// Which dialog is the caller's choice now. It used to be a chain — scan the stores, and if the
    /// user closed that, fall back to the file picker — so backing out of the scan silently became
    /// a request to pick an executable, and there was no way to ask for the picker directly.
    /// </summary>
    public Func<GameAddMethod, Task<Game?>>? RequestNewGame;

    /// <summary>Set by the view to confirm deleting a game, given its name and profile count.</summary>
    public Func<Game, int, Task<GameDeleteChoice>>? ConfirmDelete;

    /// <summary>
    /// Set by the view: runs after a game has been added and opened. The folders were guessed from
    /// the executable, and this is the moment to let the user look at them.
    /// </summary>
    public Func<Task>? GameAdded;

    /// <summary>Guard, same as the profile one: a game cannot be swapped out from under a mount.</summary>
    public Func<bool>? IsVfsMounted;

    public GameManagerViewModel(IGameService gameService, IProfileService profileService,
                                ILogService logService)
    {
        _gameService    = gameService;
        _profileService = profileService;
        _logService     = logService;
    }

    partial void OnCurrentGameChanged(Game? value)
    {
        if (_suppressActivation) return;
        _ = GameActivated?.Invoke(value);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a game and gives it a profile to start from.
    ///
    /// The profile is named after the game because that is the one name guaranteed to mean something
    /// to the user at this point — "Default" says nothing about which game it belongs to when the
    /// window lists profiles per game anyway.
    ///
    /// A folder already managed is adopted rather than added twice: that is a user pointing at the
    /// same installation again, and two games over one folder would mean two inventories over one
    /// mods folder.
    /// </summary>
    [RelayCommand] public Task AddDetectedGame()      => AddGame(GameAddMethod.Detect);
    [RelayCommand] public Task AddGameFromExecutable() => AddGame(GameAddMethod.Executable);
    [RelayCommand] public Task AddGameFromFolder()     => AddGame(GameAddMethod.Folder);

    public async Task AddGame(GameAddMethod method)
    {
        if (RequestNewGame == null) return;

        var proposed = await RequestNewGame.Invoke(method);
        if (proposed == null) return;

        var existing = await _gameService.FindByBasePathAsync(proposed.BaseDataPath);
        if (existing != null)
        {
            _logService.Log($"'{existing.DisplayName}' is already managed — opening it instead.");
            await RefreshListAsync(existing.Id);
            return;
        }

        await _gameService.SaveGameAsync(proposed);
        await _profileService.SaveProfileAsync(new Profile
        {
            Name   = proposed.DisplayName,
            GameId = proposed.Id,
        });

        _logService.Log($"Game '{proposed.DisplayName}' added.");
        await RefreshListAsync(proposed.Id);

        // Detection already knows the executable, the folder and usually the game mode, so opening
        // the settings dialog on top of it would be asking the user to confirm what they just
        // picked. The other two routes guessed everything from one path, and that guess is exactly
        // what deserves a look before it is used.
        if (method != GameAddMethod.Detect && GameAdded != null) await GameAdded.Invoke();
    }

    /// <summary>Picks a game from the menu. Same thing the selector in the command bar does.</summary>
    [RelayCommand]
    public void SelectGame(Game? game)
    {
        if (game != null) CurrentGame = game;
    }

    [RelayCommand]
    public async Task DeleteGame()
    {
        if (CurrentGame is not { Id: > 0 } game) return;

        if (IsVfsMounted?.Invoke() == true)
        {
            _logService.Log("ERROR: Cannot delete a game while Safe Swap is active. Unmount first.");
            return;
        }

        var profiles = await _profileService.ListProfilesAsync(game.Id);

        var choice = ConfirmDelete == null
            ? GameDeleteChoice.RecordOnly
            : await ConfirmDelete.Invoke(game, profiles.Count);

        if (choice == GameDeleteChoice.Cancel) return;

        await _gameService.DeleteGameAsync(game.Id);

        if (choice == GameDeleteChoice.WithFiles)
        {
            // Deleted even when another game points at the same folders. Sharing them is a thing the
            // user set up on purpose, so they are the one who knows whether the files are still
            // wanted — a manager that refuses here is just a manager that cannot finish the job.
            DeleteFolder(game.ModsFolderPath,      "mods",      game);
            DeleteFolder(game.DownloadsFolderPath, "downloads", game);
        }

        _logService.Log($"Game '{game.DisplayName}' removed, along with {profiles.Count} profile(s)." +
                        (choice == GameDeleteChoice.WithFiles ? "" : " No files were deleted."));

        await RefreshListAsync(null);
    }

    /// <summary>
    /// Removes one of the game's own folders, refusing anything that would take the installation
    /// with it.
    ///
    /// The guard is not hypothetical: nothing stops the mods folder from being set to the game
    /// folder itself, and a recursive delete there would erase the game the user was only trying to
    /// stop managing. Failures are logged rather than thrown — the row is already gone, and a folder
    /// that would not budge is something to report, not a reason to leave the app in a half state.
    /// </summary>
    private void DeleteFolder(string? path, string what, Game game)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

        string full = Path.GetFullPath(path);

        if (!string.IsNullOrWhiteSpace(game.BaseDataPath) && Contains(full, game.BaseDataPath))
        {
            _logService.Log($"Kept the {what} folder '{full}': the game itself lives inside it.");
            return;
        }

        try
        {
            Directory.Delete(full, recursive: true);
            _logService.Log($"Deleted the {what} folder '{full}'.");
        }
        catch (Exception ex)
        {
            _logService.LogError($"Could not delete the {what} folder '{full}'", ex);
        }
    }

    /// <summary>Whether <paramref name="inner"/> is <paramref name="outer"/> or sits under it.</summary>
    private static bool Contains(string outer, string inner)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outer));
        string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inner));

        return string.Equals(a, b, comparison)
            || b.StartsWith(a + Path.DirectorySeparatorChar, comparison);
    }

    /// <summary>Renames the open game. The name is a label — nothing is keyed off it.</summary>
    [RelayCommand]
    public async Task RenameGame(string? newName)
    {
        if (CurrentGame is not { Id: > 0 } game) return;
        if (string.IsNullOrWhiteSpace(newName) || newName == game.DisplayName) return;

        game.DisplayName = newName;
        await _gameService.SaveGameAsync(game);
        await RefreshListAsync(game.Id);
    }

    // ── Public helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the open game back. This is what the configuration panel saves into now — those paths
    /// describe the installation, so editing them is editing the game, not the profile that happens
    /// to be open over it.
    /// </summary>
    public async Task SaveCurrentGameAsync()
    {
        if (CurrentGame is not { Id: > 0 } game) return;

        await _lock.WaitAsync();
        try { await _gameService.SaveGameAsync(game); }
        catch (Exception ex) { _logService.Log($"GAME SAVE ERROR: {ex.Message}"); }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Rebuilds the list, then selects <paramref name="selectId"/> — or, when that is null or gone,
    /// whatever comes first. Selecting raises <see cref="GameActivated"/>, which is what loads it.
    /// </summary>
    public async Task RefreshListAsync(long? selectId)
    {
        var games = await _gameService.ListGamesAsync();
        var parked = await _profileService.ListProfilesAsync(null);

        var previous = CurrentGame;

        _suppressActivation = true;
        try
        {
            AvailableGames.Clear();
            foreach (var g in games) AvailableGames.Add(g);
            if (parked.Count > 0) AvailableGames.Add(Parked);

            CurrentGame = AvailableGames.FirstOrDefault(g => g.Id == selectId)
                       ?? AvailableGames.FirstOrDefault();
        }
        finally { _suppressActivation = false; }

        // Raised by hand rather than by the setter, because the setter was muted for the rebuild —
        // the list is replaced wholesale and every intermediate selection would otherwise load a
        // game the user never asked for. Only a real change is announced, which also means a fresh
        // install with no games at all announces nothing: there is nothing to open, and clearing the
        // window to reflect that would only overwrite what the user is in the middle of typing.
        if (previous?.Id != CurrentGame?.Id && GameActivated != null)
            await GameActivated.Invoke(CurrentGame);
    }

    /// <summary>The game to open on startup: the one last used, or the first there is.</summary>
    public Task LoadInitialGameAsync(long lastGameId) => RefreshListAsync(lastGameId);
}

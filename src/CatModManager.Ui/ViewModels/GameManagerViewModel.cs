using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatModManager.Core.Models;
using CatModManager.Core.Services;

namespace CatModManager.Ui.ViewModels;

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
    /// Set by the view: runs whatever dialog collects a new game — auto-detect or a file picker —
    /// and returns it unsaved, or null if the user backed out.
    /// </summary>
    public Func<Task<Game?>>? RequestNewGame;

    /// <summary>Set by the view to confirm deleting a game, given its name and profile count.</summary>
    public Func<Game, int, Task<bool>>? ConfirmDelete;

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
    [RelayCommand]
    public async Task AddGame()
    {
        if (RequestNewGame == null) return;

        var proposed = await RequestNewGame.Invoke();
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

        if (GameAdded != null) await GameAdded.Invoke();
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
        if (ConfirmDelete != null && !await ConfirmDelete.Invoke(game, profiles.Count)) return;

        await _gameService.DeleteGameAsync(game.Id);
        _logService.Log($"Game '{game.DisplayName}' removed, along with {profiles.Count} profile(s). " +
                        "No files were deleted.");

        await RefreshListAsync(null);
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

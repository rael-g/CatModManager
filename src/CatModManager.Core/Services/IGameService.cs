using System.Collections.Generic;
using System.Threading.Tasks;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

/// <summary>
/// Stores the installations the user manages.
///
/// Separate from <see cref="IProfileService"/> because the game stopped being a side effect of
/// saving a profile. It used to be written by whichever profile happened to be open, which is how a
/// game could be created twice, or edited by a profile the user was not looking at.
/// </summary>
public interface IGameService
{
    /// <summary>Every game, ordered by display name.</summary>
    Task<IReadOnlyList<Game>> ListGamesAsync();

    /// <summary>The game, or null when there is no such row.</summary>
    Task<Game?> LoadGameAsync(long gameId);

    /// <summary>
    /// The game already managing this folder, or null. What makes adding a game the user already
    /// has adopt it rather than duplicate it.
    /// </summary>
    Task<Game?> FindByBasePathAsync(string baseDataPath);

    /// <summary>Stores the game, and returns its id — assigned here when it was zero.</summary>
    Task<long> SaveGameAsync(Game game);

    /// <summary>
    /// Removes the game, its profiles and its mod inventory. Nothing on disk is touched: the mods
    /// stay in their folder, and adding the game back finds them again.
    /// </summary>
    Task DeleteGameAsync(long gameId);
}

using System.Collections.Generic;
using System.Threading.Tasks;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

public interface IGameLaunchService
{
    /// <summary>
    /// Runs the game and waits for it to exit. The result's value is whether the game was ever
    /// actually seen running: starting a platform launcher succeeds even when the game does not
    /// follow, so callers that undo work afterwards need to tell those two apart.
    /// </summary>
    Task<OperationResult<bool>> LaunchGameAsync(
        string? gameExecutablePath,
        string? launchArguments,
        IGameSupport activeGameSupport,
        IEnumerable<Mod> enabledMods,
        string? gameFolderPath = null);
}

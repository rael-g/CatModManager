using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.PluginSdk;

namespace CatModManager.Core.Services;

public class GameLaunchService : IGameLaunchService
{
    private readonly IProcessService _processService;
    private readonly ILogService _logService;
    private readonly IReadOnlyList<IGameLaunchHook> _launchHooks;

    public GameLaunchService(
        IProcessService processService,
        ILogService logService,
        IReadOnlyList<IGameLaunchHook>? launchHooks = null)
    {
        _processService = processService;
        _logService     = logService;
        _launchHooks    = launchHooks ?? [];
    }

    public async Task<OperationResult<bool>> LaunchGameAsync(
        string? gameExecutablePath,
        string? launchArguments,
        IGameSupport activeGameSupport,
        IEnumerable<Mod> enabledMods,
        string? gameFolderPath = null)
    {
        if (string.IsNullOrEmpty(gameExecutablePath))
            return OperationResult<bool>.Failure("No game executable specified.");

        try
        {
            string gameArgs  = activeGameSupport.GetLaunchArguments(enabledMods);
            string finalArgs = $"{gameArgs} {launchArguments}".Trim();

            var ctx = new LaunchContext
            {
                ExecutablePath = gameExecutablePath,
                Arguments      = finalArgs,
                GameId         = activeGameSupport.GameId
            };

            foreach (var hook in _launchHooks)
                await hook.OnBeforeLaunchAsync(ctx);

            _logService.Log($"Launching: {gameExecutablePath} {finalArgs}");
            // The game folder, not the executable's folder: through Steam the process we start is
            // Steam itself, so the post-exit hooks would otherwise wait on the wrong directory.
            var run = await _processService.StartProcessAsync(
                gameExecutablePath, finalArgs, runAsAdmin: false, waitForChildren: true,
                watchFolder: string.IsNullOrWhiteSpace(gameFolderPath) ? null : gameFolderPath);

            // Only once the game has actually exited. These hooks used to run unconditionally, so a
            // launch that never produced a game — Steam reporting a missing licence, say — still
            // told every plugin the session was over.
            if (run.GameObserved)
                foreach (var hook in _launchHooks)
                    await hook.OnAfterExitAsync(ctx);

            if (!run.Started)
                return OperationResult<bool>.Failure("Could not start game process.");

            return OperationResult<bool>.Success(run.GameObserved);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure($"LAUNCH ERROR: {ex.Message}", ex);
        }
    }
}

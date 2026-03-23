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

    public async Task<OperationResult> LaunchGameAsync(
        string? gameExecutablePath,
        string? launchArguments,
        IGameSupport activeGameSupport,
        IEnumerable<Mod> enabledMods)
    {
        if (string.IsNullOrEmpty(gameExecutablePath))
            return OperationResult.Failure("No game executable specified.");

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
            bool success = await _processService.StartProcessAsync(gameExecutablePath, finalArgs, false);

            foreach (var hook in _launchHooks)
                await hook.OnAfterExitAsync(ctx);

            if (!success)
                return OperationResult.Failure("Could not start game process.");

            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure($"LAUNCH ERROR: {ex.Message}", ex);
        }
    }
}

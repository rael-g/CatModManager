using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

public class GameLaunchService : IGameLaunchService
{
    private readonly IProcessService _processService;
    private readonly ILogService _logService;

    public GameLaunchService(IProcessService processService, ILogService logService)
    {
        _processService = processService;
        _logService = logService;
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
            string gameArgs = activeGameSupport.GetLaunchArguments(enabledMods);
            string finalArgs = $"{gameArgs} {launchArguments}".Trim();

            _logService.Log($"Launching: {gameExecutablePath} {finalArgs}");
            bool success = await _processService.StartProcessAsync(gameExecutablePath, finalArgs, false);

            if (!success)
            {
                return OperationResult.Failure("Could not start game process.");
            }

            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure($"LAUNCH ERROR: {ex.Message}", ex);
        }
    }
}

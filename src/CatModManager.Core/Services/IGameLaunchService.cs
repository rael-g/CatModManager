using System.Collections.Generic;
using System.Threading.Tasks;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

public interface IGameLaunchService
{
    Task<OperationResult> LaunchGameAsync(
        string? gameExecutablePath,
        string? launchArguments,
        IGameSupport activeGameSupport,
        IEnumerable<Mod> enabledMods);
}

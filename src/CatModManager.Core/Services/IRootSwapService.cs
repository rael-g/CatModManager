using System.Collections.Generic;
using System.Threading.Tasks;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

public interface IRootSwapService
{
    /// <summary>Moves Root/ contents of active mods into the game folder. Records moves in DB.</summary>
    Task DeployAsync(IEnumerable<Mod> activeMods, string gameFolder);

    /// <summary>Moves all deployed files back to their mod's Root/ folder. Restores backups.</summary>
    Task UndeployAsync(string gameFolder);

    /// <summary>Moves back only files that originated from a specific mod's Root/ folder.</summary>
    Task UndeployModAsync(string modRootPath, string gameFolder);

    /// <summary>On startup: reverses any deployment left from a crash.</summary>
    void RecoverStaleDeployments();

    /// <summary>True if there are deployed files for the given game folder.</summary>
    bool HasDeployedFiles(string gameFolder);
}

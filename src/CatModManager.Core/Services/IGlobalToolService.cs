using System.Collections.Generic;
using System.Threading.Tasks;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

/// <summary>
/// The tools that follow the user rather than an installation.
///
/// Separate from <see cref="IGameService"/> because the whole point is that these have no game: a
/// hex editor or an archive tool is the same program whichever install is open, and retyping it for
/// every game was the complaint that produced this. A tool that genuinely differs per game — xEdit
/// with its <c>-fo4</c> — stays a game tool, and nothing stops the user from having both.
/// </summary>
public interface IGlobalToolService
{
    Task<List<ExternalTool>> ListToolsAsync();

    /// <summary>Replaces the whole list. Order is the list's order, same as the game's tools.</summary>
    Task SaveToolsAsync(IReadOnlyList<ExternalTool> tools);
}

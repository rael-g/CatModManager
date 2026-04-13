using System.Collections.Generic;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

public interface IConflictResolver
{
    /// <param name="dataSubFolder">
    /// The relative path inside the game folder where mods land (e.g. "Data" or "LiesofP\Content\Paks\~mods").
    /// Used to auto-strip any matching prefix from mod files regardless of how the mod author packaged them.
    /// </param>
    /// <param name="forbiddenPath">Optional path to ignore during scanning to prevent infinite recursion.</param>
    IDictionary<string, IFileSource> ResolveConflicts(
        IEnumerable<Mod> activeMods,
        string? baseFolderPath,
        string? dataSubFolder = null,
        string? forbiddenPath = null);

    /// <summary>
    /// Returns per-mod conflict metadata: which files each mod wins or loses.
    /// </summary>
    IReadOnlyList<ConflictReport> GetConflictReport(IEnumerable<Mod> activeMods);
}

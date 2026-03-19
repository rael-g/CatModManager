using System.Collections.Generic;
using CatModManager.Core.Models;

namespace CatModManager.PluginSdk;

/// <summary>Read-only snapshot of the current CMM session state, available to all plugins.</summary>
public interface IModManagerState
{
    IReadOnlyList<Mod> ActiveMods { get; }
    string? DataFolderPath { get; }
    string? ModsFolderPath { get; }
    string? DownloadsFolderPath { get; }
    string? GameExecutablePath { get; }
    string? GameId { get; }
    string? CurrentProfileName { get; }

    /// <summary>Raised on the UI thread when the active profile changes. Argument is the new profile name.</summary>
    event Action<string>? ProfileChanged;

    /// <summary>Raised after a mod is successfully installed. Arguments are the installed Mod and the source archive path.</summary>
    event Action<Mod, string>? ModInstalled;

    /// <summary>Requests CMM to install the given archive as a mod (runs through the normal AddMod pipeline).</summary>
    void RequestInstallMod(string archivePath);
}

using System;
using System.Collections.Generic;

namespace CatModManager.PluginSdk;

/// <summary>Read-only snapshot of the current CMM session state, available to all plugins.</summary>
public interface IModManagerState
{
    IReadOnlyList<IModInfo> ActiveMods          { get; }
    string?                 DataFolderPath      { get; }
    string?                 ModsFolderPath      { get; }
    string?                 DownloadsFolderPath { get; }
    string?                 GameExecutablePath  { get; }
    string?                 GameId              { get; }
    /// <summary>Nexus Mods game domain (e.g. "skyrimspecialedition"). Used by the NexusMods plugin for Browse.</summary>
    string?                 NexusDomain         { get; }
    string?                 CurrentProfileName  { get; }
    /// <summary>Relative sub-folder the VFS mounts in (empty = game root). Use to determine routing mode in installers.</summary>
    string?                 DataSubFolder       { get; }
    /// <summary>True when the active game uses RootSwap-only mode (no VFS).</summary>
    bool                    RootSwapOnly        { get; }

    /// <summary>Raised on the UI thread when the active profile changes.</summary>
    event Action<string>? ProfileChanged;

    /// <summary>Raised after a mod is successfully installed. Arguments: installed mod, source archive path.</summary>
    event Action<IModInfo, string>? ModInstalled;

    /// <summary>Requests CMM to install the given archive as a mod.</summary>
    void RequestInstallMod(string archivePath);

    /// <summary>
    /// Requests CMM to install the given archive as a mod, supplying pre-selected FOMOD choices
    /// so the installer can auto-apply them without showing the wizard UI.
    /// </summary>
    void RequestInstallMod(string archivePath, FomodPreset? fomodPreset);

    /// <summary>
    /// Requests CMM to install the given archive into the mod's Root/ subfolder so its files
    /// are deployed directly to the game root at mount time (not into Data/).
    /// Wrapper folders in the archive are automatically stripped.
    /// </summary>
    void RequestInstallModToRoot(string archivePath);
}

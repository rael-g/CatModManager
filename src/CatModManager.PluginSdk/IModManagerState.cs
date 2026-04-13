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
    /// Hints to CMM that the next install should overwrite the given existing folder instead of
    /// creating a new one. Used by the NexusMods plugin to reinstall into the same folder when
    /// a mod with the same Nexus mod ID is already installed. Consumed (cleared) after use.
    /// </summary>
    void SetInstallFolderHint(string existingFolderPath);

    /// <summary>
    /// Registers a callback that returns true when there are active downloads in progress.
    /// Used by CMM to warn the user before switching profiles mid-download.
    /// </summary>
    void SetActiveDownloadCheck(Func<bool> check);
}


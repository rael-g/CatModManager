using System;
using System.Collections.Generic;
using CatModManager.PluginSdk;

namespace CatModManager.Ui.Plugins;

/// <summary>
/// Thin adapter over AppSessionState that satisfies IModManagerState for plugins.
/// Has no dependency on any ViewModel type.
/// </summary>
public class ModManagerStateAdapter : IModManagerState
{
    private readonly AppSessionState _state;

    public ModManagerStateAdapter(AppSessionState state) => _state = state;

    public IReadOnlyList<IModInfo> ActiveMods        => _state.ActiveMods;
    public string? DataFolderPath      => _state.DataFolderPath;
    public string? ModsFolderPath      => _state.ModsFolderPath;
    public string? DownloadsFolderPath => _state.DownloadsFolderPath;
    public string? GameExecutablePath  => _state.GameExecutablePath;
    public string? GameId              => _state.GameId;
    public string? CurrentProfileName  => _state.CurrentProfileName;
    public string? DataSubFolder       => _state.DataSubFolder;
    public bool    RootSwapOnly        => _state.RootSwapOnly;

    public event Action<string>?          ProfileChanged
    {
        add    => _state.ProfileChanged += value;
        remove => _state.ProfileChanged -= value;
    }

    public event Action<IModInfo, string>? ModInstalled
    {
        add    => _state.ModInstalled += value;
        remove => _state.ModInstalled -= value;
    }

    public void RequestInstallMod(string archivePath) =>
        _state.RequestInstall(archivePath);

    public void RequestInstallMod(string archivePath, FomodPreset? fomodPreset) =>
        _state.RequestInstall(archivePath, fomodPreset);
}

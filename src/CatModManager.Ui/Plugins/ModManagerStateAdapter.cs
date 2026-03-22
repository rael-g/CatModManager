using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CatModManager.PluginSdk;
using CatModManager.Ui.ViewModels;

namespace CatModManager.Ui.Plugins;

/// <summary>
/// Exposes MainWindowViewModel state to plugins via IModManagerState,
/// without leaking ViewModel or Core types into the SDK.
/// </summary>
public class ModManagerStateAdapter : IModManagerState
{
    private readonly MainWindowViewModel _vm;

    public ModManagerStateAdapter(MainWindowViewModel vm)
    {
        _vm = vm;
        _vm.ProfileManager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProfileManagerViewModel.CurrentProfileName) &&
                _vm.ProfileManager.CurrentProfileName is { } name)
                ProfileChanged?.Invoke(name);
        };
        _vm.ModInstalled += (mod, sourcePath) =>
            ModInstalled?.Invoke(new ModInfoAdapter(mod), sourcePath);
    }

    public IReadOnlyList<IModInfo> ActiveMods =>
        _vm.AllMods.Where(m => m.IsEnabled).Select(m => (IModInfo)new ModInfoAdapter(m)).ToList();

    public string? DataFolderPath      => _vm.BaseFolderPath;
    public string? ModsFolderPath      => _vm.ModsFolderPath;
    public string? DownloadsFolderPath => _vm.DownloadsFolderPath;
    public string? GameExecutablePath  => _vm.GameExecutablePath;
    public string? GameId              => _vm.ActiveGameSupport?.GameId;
    public string? CurrentProfileName  => _vm.ProfileManager.CurrentProfileName;
    public string? DataSubFolder       => _vm.ActiveGameSupport?.DataSubFolder;
    public bool    RootSwapOnly        => _vm.ActiveGameSupport?.RootSwapOnly ?? false;

    public event Action<string>?          ProfileChanged;
    public event Action<IModInfo, string>? ModInstalled;

    public void RequestInstallMod(string archivePath) =>
        Dispatcher.UIThread.InvokeAsync(() => _vm.AddModCommand.Execute(archivePath));
}

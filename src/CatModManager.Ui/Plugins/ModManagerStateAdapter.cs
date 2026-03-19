using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CatModManager.Core.Models;
using CatModManager.PluginSdk;
using CatModManager.Ui.ViewModels;

namespace CatModManager.Ui.Plugins;

/// <summary>
/// Exposes MainWindowViewModel state to plugins via IModManagerState,
/// without leaking ViewModel types into the SDK.
/// </summary>
public class ModManagerStateAdapter : IModManagerState
{
    private readonly MainWindowViewModel _vm;

    public ModManagerStateAdapter(MainWindowViewModel vm)
    {
        _vm = vm;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentProfileName) &&
                _vm.CurrentProfileName is { } name)
                ProfileChanged?.Invoke(name);
        };
        _vm.ModInstalled += (mod, sourcePath) => ModInstalled?.Invoke(mod, sourcePath);
    }

    public IReadOnlyList<Mod> ActiveMods => _vm.AllMods.Where(m => m.IsEnabled).ToList();
    public string? DataFolderPath => _vm.BaseFolderPath;
    public string? ModsFolderPath => _vm.ModsFolderPath;
    public string? DownloadsFolderPath => _vm.DownloadsFolderPath;
    public string? GameExecutablePath => _vm.GameExecutablePath;
    public string? GameId => _vm.ActiveGameSupport?.GameId;
    public string? CurrentProfileName => _vm.CurrentProfileName;

    public event Action<string>? ProfileChanged;
    public event Action<Mod, string>? ModInstalled;

    public void RequestInstallMod(string archivePath) =>
        Dispatcher.UIThread.InvokeAsync(() => _vm.AddModCommand.Execute(archivePath));
}

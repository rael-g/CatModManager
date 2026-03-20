using System.Collections.Generic;
using System.Collections.ObjectModel;
using CatModManager.PluginSdk;

namespace CatModManager.Ui.Plugins;

/// <summary>
/// Registry for all extensions contributed by plugins.
/// Implements IPluginRegistrar (plugin-facing) and exposes internal collections for the host.
/// </summary>
public class UiExtensionHost : IPluginRegistrar
{
    // Observable collections watched directly by the ViewModel.
    public ObservableCollection<IInspectorTab>  InspectorTabs            { get; } = new();
    public ObservableCollection<ISidebarAction> SidebarActionsObservable { get; } = new();

    // Internal lists queried by the host (game launch, VFS hooks, etc.).
    private readonly List<IModInstaller>     _modInstallers = new();
    private readonly List<IVfsLifecycleHook> _vfsHooks      = new();
    private readonly List<IGameLaunchHook>   _launchHooks   = new();
    private readonly List<ISidebarAction>    _sidebarActions = new();

    public IReadOnlyList<IModInstaller>     ModInstallers => _modInstallers;
    public IReadOnlyList<IVfsLifecycleHook> VfsHooks      => _vfsHooks;
    public IReadOnlyList<IGameLaunchHook>   LaunchHooks   => _launchHooks;
    public IReadOnlyList<ISidebarAction>    SidebarActions => _sidebarActions;

    // IPluginRegistrar
    public void RegisterInspectorTab(IInspectorTab tab)        => InspectorTabs.Add(tab);
    public void RegisterModInstaller(IModInstaller installer)   => _modInstallers.Add(installer);
    public void RegisterVfsLifecycleHook(IVfsLifecycleHook hook) => _vfsHooks.Add(hook);
    public void RegisterGameLaunchHook(IGameLaunchHook hook)    => _launchHooks.Add(hook);
    public void RegisterSidebarAction(ISidebarAction action)
    {
        _sidebarActions.Add(action);
        SidebarActionsObservable.Add(action);
    }
}

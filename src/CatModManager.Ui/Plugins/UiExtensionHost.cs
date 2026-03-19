using System.Collections.Generic;
using System.Collections.ObjectModel;
using CatModManager.PluginSdk;

namespace CatModManager.Ui.Plugins;

/// <summary>
/// Registry for all UI extensions contributed by plugins.
/// The ViewModel observes InspectorTabs and SidebarActionsObservable directly.
/// </summary>
public class UiExtensionHost : IUiExtensionHost
{
    public ObservableCollection<IInspectorTab>  InspectorTabs          { get; } = new();
    public ObservableCollection<ISidebarAction> SidebarActionsObservable { get; } = new();

    private readonly List<IModInstaller>    _modInstallers  = new();
    private readonly List<IVfsLifecycleHook> _vfsHooks      = new();
    private readonly List<IGameLaunchHook>  _launchHooks    = new();
    private readonly List<ISidebarAction>   _sidebarActions = new();

    public IReadOnlyList<IModInstaller>    ModInstallers  => _modInstallers;
    public IReadOnlyList<IVfsLifecycleHook> VfsHooks      => _vfsHooks;
    public IReadOnlyList<IGameLaunchHook>  LaunchHooks    => _launchHooks;
    public IReadOnlyList<ISidebarAction>   SidebarActions => _sidebarActions;

    public void RegisterInspectorTab(IInspectorTab tab)   => InspectorTabs.Add(tab);
    public void RegisterModInstaller(IModInstaller installer) => _modInstallers.Add(installer);
    public void RegisterVfsLifecycleHook(IVfsLifecycleHook hook) => _vfsHooks.Add(hook);
    public void RegisterGameLaunchHook(IGameLaunchHook hook) => _launchHooks.Add(hook);
    public void RegisterSidebarAction(ISidebarAction action)
    {
        _sidebarActions.Add(action);
        SidebarActionsObservable.Add(action);
    }
}

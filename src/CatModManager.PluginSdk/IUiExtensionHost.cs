using System.Collections.Generic;

namespace CatModManager.PluginSdk;

/// <summary>Allows plugins to register UI extensions into the CMM shell.</summary>
public interface IUiExtensionHost
{
    // --- Registration ---
    void RegisterInspectorTab(IInspectorTab tab);
    void RegisterModInstaller(IModInstaller installer);
    void RegisterVfsLifecycleHook(IVfsLifecycleHook hook);
    void RegisterGameLaunchHook(IGameLaunchHook hook);
    void RegisterSidebarAction(ISidebarAction action);

    // --- Querying (used by the CMM host) ---
    IReadOnlyList<IModInstaller> ModInstallers { get; }
    IReadOnlyList<IVfsLifecycleHook> VfsHooks { get; }
    IReadOnlyList<IGameLaunchHook> LaunchHooks { get; }
    IReadOnlyList<ISidebarAction> SidebarActions { get; }
}

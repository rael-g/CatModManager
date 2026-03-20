namespace CatModManager.PluginSdk;

/// <summary>Allows plugins to register extensions into the CMM shell.</summary>
public interface IPluginRegistrar
{
    void RegisterInspectorTab(IInspectorTab tab);
    void RegisterModInstaller(IModInstaller installer);
    void RegisterVfsLifecycleHook(IVfsLifecycleHook hook);
    void RegisterGameLaunchHook(IGameLaunchHook hook);
    void RegisterSidebarAction(ISidebarAction action);
}

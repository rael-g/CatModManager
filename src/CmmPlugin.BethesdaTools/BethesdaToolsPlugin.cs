using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Hooks;
using CmmPlugin.BethesdaTools.Installers;
using CmmPlugin.BethesdaTools.Services;
using CmmPlugin.BethesdaTools.Tabs;

namespace CmmPlugin.BethesdaTools;

public class BethesdaToolsPlugin : ICmmPlugin
{
    public string Id => "bethesda-tools";
    public string DisplayName => "Bethesda Tools";
    public string Version => typeof(BethesdaToolsPlugin).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public string Author => "CatModManager";

    public void Initialize(IPluginContext context)
    {
        var loadOrder = new LoadOrderService(context.Log);
        var vm = new PluginsTabViewModel(loadOrder, context.State, context.Log);
        var tab = new PluginsInspectorTab(vm);
        var hook = new BethesdaLaunchHook(loadOrder, context.State, context.Log);

        var installer = new BethesdaModInstaller(context.State);

        context.Ui.RegisterModInstaller(installer);
        context.Ui.RegisterInspectorTab(tab);
        context.Ui.RegisterGameLaunchHook(hook);

        context.Log.Log($"[{DisplayName}] Initialized — supports Skyrim, Fallout, Oblivion, Starfield and more.");
    }
}

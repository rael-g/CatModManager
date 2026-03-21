using CatModManager.PluginSdk;
using CmmPlugin.Capcom.Hooks;
using CmmPlugin.Capcom.Installers;
using CmmPlugin.Capcom.Tabs;

namespace CmmPlugin.Capcom;

public class CapcomPlugin : ICmmPlugin
{
    public string Id          => "capcom-re-engine";
    public string DisplayName => "Capcom RE Engine";
    public string Version     => typeof(CapcomPlugin).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public string Author      => "CatModManager";

    public void Initialize(IPluginContext ctx)
    {
        var vm   = new ReEngineTabViewModel(ctx.State);
        var tab  = new ReEngineInspectorTab(vm, ctx.State);
        var hook = new CapcomLaunchHook(ctx.State, ctx.Log);

        ctx.Ui.RegisterInspectorTab(tab);
        ctx.Ui.RegisterGameLaunchHook(hook);

        var installer = new ReEngineModInstaller(ctx.State);
        ctx.Ui.RegisterModInstaller(installer);

        ctx.Log.Log($"[{DisplayName}] Initialized — RE2R, RE3R, RE7, RE Village, RE4R, RE9, DMC5, MH Rise, MH Wilds, DD2, SF6.");
    }
}

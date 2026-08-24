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
        var fileService = new PhysicalFileService();
        var detector = new BethesdaDetector(fileService);
        var paths = new GamePathResolver(fileService, context.Log);
        var looseFiles = new LooseFilesIniService(fileService, context.Log);

        var loadOrder = new LoadOrderService(context.Log, fileService);
        var vm = new PluginsTabViewModel(loadOrder, context.State, context.Log, detector, paths);
        var tab = new PluginsInspectorTab(vm);
        var hook = new BethesdaLaunchHook(loadOrder, context.State, context.Log, detector, paths, looseFiles);

        var installer = new BethesdaModInstaller(context.State, context.ArchiveExtractor, detector);

        // Both events change which plugins exist on disk, so the tab has to re-scan or it keeps
        // showing a stale load order.
        context.State.ProfileChanged += _ => vm.Refresh();
        context.State.ModInstalled += (_, _) => vm.Refresh();

        context.Ui.RegisterModInstaller(installer);
        context.Ui.RegisterInspectorTab(tab);
        context.Ui.RegisterGameLaunchHook(hook);

        context.Log.Log($"[{DisplayName}] Initialized — supports Skyrim, Fallout, Oblivion, Starfield and more.");
    }
}

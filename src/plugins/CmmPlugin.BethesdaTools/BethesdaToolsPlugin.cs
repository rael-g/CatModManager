using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Hooks;
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

        // No mod installer is registered here on purpose. There used to be one whose whole job was
        // guessing where a mod's files belonged — stripping a lone top-level folder as "packaging"
        // and stripping a "Data/" prefix because the VFS mounts the mod root as Data. Guessing is
        // the wrong tool: mount points already say exactly where a mod goes, and when an archive is
        // laid out oddly the fix is to correct its folders in the mod's install folder, which is
        // visible and reversible. The guess was neither — it silently relocated files, and a mod
        // shipping only SFSE/ had that folder eaten as "packaging", leaving Plugins/x.dll that
        // nothing loads. Without routing, archives extract verbatim through InstallModAsync.

        // Both events change which plugins exist on disk, so the tab has to re-scan or it keeps
        // showing a stale load order.
        context.State.ProfileChanged += _ => vm.Refresh();
        context.State.ModInstalled += (_, _) => vm.Refresh();

        context.Ui.RegisterInspectorTab(tab);
        context.Ui.RegisterGameLaunchHook(hook);

        context.Log.Log($"[{DisplayName}] Initialized — supports Skyrim, Fallout, Oblivion, Starfield and more.");
    }
}

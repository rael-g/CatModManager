using CatModManager.PluginSdk;
using CmmPlugin.SaveManager.Hooks;
using CmmPlugin.SaveManager.Services;
using CmmPlugin.SaveManager.Tabs;

namespace CmmPlugin.SaveManager;

public class SaveManagerPlugin : ICmmPlugin
{
    public string Id          => "save-manager";
    public string DisplayName => "Save Manager";
    public string Version     => typeof(SaveManagerPlugin).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public string Author      => "CatModManager";

    public void Initialize(IPluginContext context)
    {
        var detector = new SaveDetector(context.Log, new WindowsUserFolders(new PhysicalFileService(), context.Log));
        detector.Load(context.AppDataPath);

        var backupService = new SaveBackupService(context.AppDataPath, context.Log);
        var settings      = new SaveManagerSettings(context.AppDataPath, context.Log);
        var autoSaver     = new AutoSaver(backupService, context.Log);
        var vm            = new SaveManagerTabViewModel(detector, backupService, settings, autoSaver, context.State, context.Log);
        var tab           = new SaveManagerInspectorTab(vm);
        var hook          = new SaveManagerLaunchHook(detector, backupService, settings, context.State, context.Log);

        context.Ui.RegisterInspectorTab(tab);
        context.Ui.RegisterGameLaunchHook(hook);

        context.Log.Log($"[{DisplayName}] Initialized — {detector.Count} save-managed game(s) detected from definitions.");
    }
}


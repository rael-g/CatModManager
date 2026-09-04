using CatModManager.PluginSdk;
using CmmPlugin.SaveManager.Services;

namespace CmmPlugin.SaveManager.Hooks;

public class SaveManagerLaunchHook : IGameLaunchHook
{
    private readonly SaveDetector        _detector;
    private readonly SaveBackupService   _backupService;
    private readonly SaveManagerSettings _settings;
    private readonly IModManagerState    _state;
    private readonly IPluginLogger       _log;

    public SaveManagerLaunchHook(
        SaveDetector detector, SaveBackupService backupService, SaveManagerSettings settings,
        IModManagerState state, IPluginLogger log)
    {
        _detector      = detector;
        _backupService = backupService;
        _settings      = settings;
        _state         = state;
        _log           = log;
    }

    public async Task OnBeforeLaunchAsync(LaunchContext ctx)
    {
        var def = _detector.Detect(ctx.ExecutablePath ?? _state.GameExecutablePath, _state.DataFolderPath);
        if (def == null) return;

        // Asked every launch rather than captured once: the switch lives in the tab, and the tab and
        // this hook are separate objects with no notification between them.
        if (!_settings.For(def.GameId).BackupBeforeLaunch) return;

        string? saveFolder = _detector.ResolveSaveFolder(def, _state.DataFolderPath, ctx.ExecutablePath ?? _state.GameExecutablePath);
        if (saveFolder == null)
        {
            _log.Log($"[SaveManager] Save folder not found for {def.DisplayName} — skipping backup.");
            return;
        }

        _log.Log($"[SaveManager] Backing up saves for {def.DisplayName}...");

        // Auto, so it lands in the five-slot ring buffer rather than the user's own list. This runs
        // without anyone asking for it, so it must not crowd out slots someone made on purpose.
        await _backupService.CreateAsync(def.GameId, saveFolder, "before launch", SaveSlotKind.Auto);
    }

    public Task OnAfterExitAsync(LaunchContext ctx) => Task.CompletedTask;
}


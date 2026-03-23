using CatModManager.PluginSdk;
using CmmPlugin.SaveManager.Services;

namespace CmmPlugin.SaveManager.Hooks;

public class SaveManagerLaunchHook : IGameLaunchHook
{
    private readonly SaveDetector      _detector;
    private readonly SaveBackupService _backupService;
    private readonly IModManagerState  _state;
    private readonly IPluginLogger     _log;

    public SaveManagerLaunchHook(SaveDetector detector, SaveBackupService backupService, IModManagerState state, IPluginLogger log)
    {
        _detector      = detector;
        _backupService = backupService;
        _state         = state;
        _log           = log;
    }

    public async Task OnBeforeLaunchAsync(LaunchContext ctx)
    {
        var def = _detector.Detect(ctx.ExecutablePath ?? _state.GameExecutablePath);
        if (def == null) return;

        string? saveFolder = SaveDetector.ResolveSaveFolder(def);
        if (saveFolder == null)
        {
            _log.Log($"[SaveManager] Save folder not found for {def.DisplayName} — skipping backup.");
            return;
        }

        _log.Log($"[SaveManager] Backing up saves for {def.DisplayName}...");
        await _backupService.CreateBackupAsync(def, saveFolder, label: "auto");
    }

    public Task OnAfterExitAsync(LaunchContext ctx) => Task.CompletedTask;
}

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Services;

namespace CmmPlugin.BethesdaTools.Hooks;

public class BethesdaLaunchHook : IGameLaunchHook
{
    private readonly LoadOrderService _loadOrder;
    private readonly IModManagerState _state;
    private readonly IPluginLogger    _log;
    private readonly BethesdaDetector  _detector;

    public BethesdaLaunchHook(LoadOrderService loadOrder, IModManagerState state, IPluginLogger log, BethesdaDetector detector)
    {
        _loadOrder = loadOrder;
        _state     = state;
        _log       = log;
        _detector  = detector;
    }

    public Task OnBeforeLaunchAsync(LaunchContext ctx)
    {
        var game = _detector.Detect(ctx.ExecutablePath ?? _state.GameExecutablePath);
        if (game == null) return Task.CompletedTask;

        string pluginsTextPath = BethesdaDetector.GetPluginsTextPath(game);
        _log.Log($"[Bethesda] Syncing load order to {pluginsTextPath}");
        
        string? dataDir = !string.IsNullOrEmpty(_state.GameExecutablePath) 
            ? Path.Combine(Path.GetDirectoryName(_state.GameExecutablePath)!, "Data") 
            : null;

        _loadOrder.Refresh(dataDir, pluginsTextPath, _state.ActiveMods);
        _loadOrder.Save(pluginsTextPath, game.UsesStarFormat);
        
        return Task.CompletedTask;
    }

    public Task OnAfterExitAsync(LaunchContext ctx) => Task.CompletedTask;
}

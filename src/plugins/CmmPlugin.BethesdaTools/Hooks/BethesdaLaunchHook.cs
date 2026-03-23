using System.Threading.Tasks;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Services;

namespace CmmPlugin.BethesdaTools.Hooks;

/// <summary>
/// Writes plugins.txt before every game launch so the load order is always up-to-date.
/// </summary>
public class BethesdaLaunchHook : IGameLaunchHook
{
    private readonly LoadOrderService _loadOrder;
    private readonly IModManagerState _state;
    private readonly IPluginLogger _log;

    public BethesdaLaunchHook(LoadOrderService loadOrder, IModManagerState state, IPluginLogger log)
    {
        _loadOrder = loadOrder;
        _state = state;
        _log = log;
    }

    public Task OnBeforeLaunchAsync(LaunchContext ctx)
    {
        var game = BethesdaDetector.Detect(ctx.ExecutablePath ?? _state.GameExecutablePath);
        if (game == null) return Task.CompletedTask;

        string pluginsTextPath = BethesdaDetector.GetPluginsTextPath(game);
        _loadOrder.Refresh(_state.DataFolderPath, pluginsTextPath, _state.ActiveMods);
        _loadOrder.Save(pluginsTextPath, game.UsesStarFormat);

        return Task.CompletedTask;
    }

    public Task OnAfterExitAsync(LaunchContext ctx) => Task.CompletedTask;
}

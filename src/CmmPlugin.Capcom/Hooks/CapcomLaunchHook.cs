using System.IO;
using System.Threading.Tasks;
using CatModManager.PluginSdk;
using CmmPlugin.Capcom.Services;

namespace CmmPlugin.Capcom.Hooks;

/// <summary>
/// Runs before and after game launch for RE Engine titles.
/// Warns the user when REFramework is absent so script-based mods won't work.
/// </summary>
public class CapcomLaunchHook : IGameLaunchHook
{
    private readonly IModManagerState _state;
    private readonly IPluginLogger    _log;

    public CapcomLaunchHook(IModManagerState state, IPluginLogger log)
    {
        _state = state;
        _log   = log;
    }

    public Task OnBeforeLaunchAsync(LaunchContext ctx)
    {
        var exe  = ctx.ExecutablePath ?? _state.GameExecutablePath;
        var game = ReEngineDetector.Detect(exe);
        if (game == null) return Task.CompletedTask;

        var gameFolder    = Path.GetDirectoryName(exe ?? "");
        var hasReFramework = ReEngineDetector.IsReFrameworkInstalled(gameFolder);
        var scriptCount   = ReEngineDetector.CountReFrameworkScripts(gameFolder);

        _log.Log($"[Capcom] Launching {game.DisplayName}.");

        if (game.HasReFrameworkSupport && !hasReFramework)
            _log.Log("[Capcom] WARNING: REFramework not detected — Lua script mods will not run.");
        else if (hasReFramework)
            _log.Log($"[Capcom] REFramework detected ({ReEngineDetector.GetReFrameworkVersion(gameFolder)}), {scriptCount} autorun script(s).");

        return Task.CompletedTask;
    }

    public Task OnAfterExitAsync(LaunchContext ctx) => Task.CompletedTask;
}

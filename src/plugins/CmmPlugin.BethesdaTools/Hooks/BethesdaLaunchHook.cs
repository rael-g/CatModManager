using System.IO;
using System.Threading.Tasks;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Services;

namespace CmmPlugin.BethesdaTools.Hooks;

public class BethesdaLaunchHook : IGameLaunchHook
{
    private readonly LoadOrderService _loadOrder;
    private readonly IModManagerState _state;
    private readonly IPluginLogger    _log;
    private readonly BethesdaDetector _detector;
    private readonly GamePathResolver _paths;
    private readonly LooseFilesIniService _looseFiles;

    public BethesdaLaunchHook(LoadOrderService loadOrder, IModManagerState state, IPluginLogger log,
                              BethesdaDetector detector, GamePathResolver paths, LooseFilesIniService looseFiles)
    {
        _loadOrder  = loadOrder;
        _state      = state;
        _log        = log;
        _detector   = detector;
        _paths      = paths;
        _looseFiles = looseFiles;
    }

    public Task OnBeforeLaunchAsync(LaunchContext ctx)
    {
        string? exePath = ctx.ExecutablePath ?? _state.GameExecutablePath;

        var game = _detector.Detect(exePath);
        if (game == null) return Task.CompletedTask;

        // Loose files are ignored by Starfield/FO4 without this, so do it before anything else —
        // a correct load order is useless if the engine never reads the mounted Data folder.
        _looseFiles.Apply(game, _paths.GetMyGamesPath(game, exePath));

        string? pluginsTextPath = _paths.GetPluginsTextPath(game, exePath);
        if (pluginsTextPath == null)
        {
            _log.LogError(
                "[BethesdaTools] Skipping load order sync — the game's Wine/Proton prefix could not be " +
                "located. Launch the game once through Steam so the prefix is created.", null);
            return Task.CompletedTask;
        }

        _log.Log($"[BethesdaTools] Syncing load order to {pluginsTextPath}");

        _loadOrder.Refresh(ResolveDataFolder(exePath), pluginsTextPath, _state.ActiveMods, game);
        _loadOrder.Save(pluginsTextPath, game.UsesStarFormat);

        return Task.CompletedTask;
    }

    private string? ResolveDataFolder(string? exePath)
    {
        if (!string.IsNullOrEmpty(_state.DataFolderPath)) return _state.DataFolderPath;

        string? exeDir = string.IsNullOrEmpty(exePath) ? null : Path.GetDirectoryName(exePath);
        return string.IsNullOrEmpty(exeDir) ? null : Path.Combine(exeDir, "Data");
    }

    public Task OnAfterExitAsync(LaunchContext ctx) => Task.CompletedTask;
}

using CatModManager.Core.Services;
using CatModManager.PluginSdk;

namespace CatModManager.Ui.Plugins;

public class PluginContext : IPluginContext
{
    public IPluginLogger    Log         { get; }
    public IEventBus        Events      { get; }
    public IPluginRegistrar Ui          { get; }
    public ICmmSettings     Settings    { get; }
    public IModManagerState State       { get; }
    public string           AppDataPath { get; }

    public PluginContext(IPluginLogger log, IEventBus events, IPluginRegistrar ui, ICmmSettings settings, IModManagerState state, ICatPathService pathService)
    {
        Log         = log;
        Events      = events;
        Ui          = ui;
        Settings    = settings;
        State       = state;
        AppDataPath = pathService.BaseDataPath;
    }
}

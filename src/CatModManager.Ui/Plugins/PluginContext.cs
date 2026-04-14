using CatModManager.Core.Services;
using CatModManager.PluginSdk;

namespace CatModManager.Ui.Plugins;

public class PluginContext : IPluginContext
{
    public IPluginLogger    Log         { get; }
    public IEventBus        Events      { get; }
    public IPluginRegistrar Ui          { get; }
    public IModManagerState State       { get; }
    public IArchiveExtractor ArchiveExtractor { get; }
    public string           AppDataPath { get; }

    public PluginContext(
        IPluginLogger    log, 
        IEventBus        events, 
        IPluginRegistrar ui, 
        IModManagerState state, 
        IArchiveExtractor extractor,
        ICatPathService  pathService)
    {
        Log              = log;
        Events           = events;
        Ui               = ui;
        State            = state;
        ArchiveExtractor = extractor;
        AppDataPath      = pathService.BaseDataPath;
    }
}

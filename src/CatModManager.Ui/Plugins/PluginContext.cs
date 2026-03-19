using CatModManager.Core.Services;
using CatModManager.PluginSdk;

namespace CatModManager.Ui.Plugins;

public class PluginContext : IPluginContext
{
    public ILogService Log { get; }
    public IEventBus Events { get; }
    public IUiExtensionHost Ui { get; }
    public ICmmSettings Settings { get; }
    public IModManagerState State { get; }

    public PluginContext(ILogService log, IEventBus events, IUiExtensionHost ui, ICmmSettings settings, IModManagerState state)
    {
        Log = log;
        Events = events;
        Ui = ui;
        Settings = settings;
        State = state;
    }
}

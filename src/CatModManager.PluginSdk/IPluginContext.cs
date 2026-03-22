namespace CatModManager.PluginSdk;

/// <summary>Context injected into every plugin during initialization.</summary>
public interface IPluginContext
{
    IPluginLogger    Log         { get; }
    IEventBus        Events      { get; }
    IPluginRegistrar Ui          { get; }
    ICmmSettings     Settings    { get; }
    IModManagerState State       { get; }
    /// <summary>CMM's persistent data directory. Plugins may store data in sub-folders here.</summary>
    string           AppDataPath { get; }
}

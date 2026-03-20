namespace CatModManager.PluginSdk;

/// <summary>Context injected into every plugin during initialization.</summary>
public interface IPluginContext
{
    IPluginLogger    Log      { get; }
    IEventBus        Events   { get; }
    IPluginRegistrar Ui       { get; }
    ICmmSettings     Settings { get; }
    IModManagerState State    { get; }
}

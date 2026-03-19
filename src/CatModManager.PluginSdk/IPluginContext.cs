using CatModManager.Core.Services;

namespace CatModManager.PluginSdk;

/// <summary>Context injected into every plugin during initialization.</summary>
public interface IPluginContext
{
    ILogService Log { get; }
    IEventBus Events { get; }
    IUiExtensionHost Ui { get; }
    ICmmSettings Settings { get; }
    IModManagerState State { get; }
}

using System.Threading.Tasks;

namespace CatModManager.PluginSdk;

/// <summary>Minimum contract every CMM plugin must implement.</summary>
public interface ICmmPlugin
{
    string Id          { get; }
    string DisplayName { get; }
    string Version     { get; }
    string Author      { get; }

    void Initialize(IPluginContext context);

    /// <summary>Called before the plugin is unloaded. Override to release resources.</summary>
    Task ShutdownAsync() => Task.CompletedTask;
}

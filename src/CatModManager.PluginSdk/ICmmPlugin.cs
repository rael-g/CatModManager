namespace CatModManager.PluginSdk;

/// <summary>Minimum contract every CMM plugin must implement.</summary>
public interface ICmmPlugin
{
    string Id { get; }
    string DisplayName { get; }
    string Version { get; }
    string Author { get; }
    void Initialize(IPluginContext context);
}

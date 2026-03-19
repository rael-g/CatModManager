using CatModManager.PluginSdk;

namespace CatModManager.Ui.Plugins;

/// <summary>No-op settings implementation used until per-plugin settings are backed by files.</summary>
public class NullCmmSettings : ICmmSettings
{
    public T? Get<T>(string key) => default;
    public void Set<T>(string key, T value) { }
    public void Save() { }
}

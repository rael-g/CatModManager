namespace CatModManager.PluginSdk;

public interface ICmmSettings
{
    T? Get<T>(string key);
    void Set<T>(string key, T value);
    void Save();
}

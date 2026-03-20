namespace CatModManager.PluginSdk;

public interface IInstallContext
{
    string        DestinationFolder { get; }
    IPluginLogger Log              { get; }
}

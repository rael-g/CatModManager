using CatModManager.Core.Services;

namespace CatModManager.PluginSdk;

public interface IInstallContext
{
    string DestinationFolder { get; }
    ILogService Log { get; }
}

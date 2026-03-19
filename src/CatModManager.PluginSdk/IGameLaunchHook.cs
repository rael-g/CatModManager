using System.Threading.Tasks;

namespace CatModManager.PluginSdk;

public interface IGameLaunchHook
{
    Task OnBeforeLaunchAsync(LaunchContext ctx);
    Task OnAfterExitAsync(LaunchContext ctx);
}

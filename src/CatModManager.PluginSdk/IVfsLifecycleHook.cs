using System.Threading.Tasks;

namespace CatModManager.PluginSdk;

public interface IVfsLifecycleHook
{
    Task OnBeforeMountAsync(MountInfo info);
    Task OnAfterUnmountAsync(string mountPath);
}

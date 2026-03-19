using System.Threading.Tasks;
using CatModManager.Core.Models;

namespace CatModManager.PluginSdk;

public interface IVfsLifecycleHook
{
    Task OnBeforeMountAsync(MountOptions options);
    Task OnAfterUnmountAsync(string mountPath);
}

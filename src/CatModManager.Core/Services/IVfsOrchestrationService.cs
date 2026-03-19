using System.Threading.Tasks;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

public interface IVfsOrchestrationService
{
    bool IsMounted { get; }
    Task<OperationResult> MountAsync(MountOptions options);
    Task<OperationResult> UnmountAsync();
    void RecoverStaleMounts();
    void ShutdownCleanup();
}

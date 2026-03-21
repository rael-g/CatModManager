using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CatModManager.Core.Services.GameDiscovery;

public interface IGameDiscoveryService
{
    Task<IReadOnlyList<GameInstallation>> ScanAsync(CancellationToken ct = default);
}

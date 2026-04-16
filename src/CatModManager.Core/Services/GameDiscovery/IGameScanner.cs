using System.Collections.Generic;
using System.Threading;

namespace CatModManager.Core.Services.GameDiscovery;

/// <summary>
/// Interface for platform-specific game discovery (Steam, GOG, Epic, etc.).
/// </summary>
public interface IGameScanner
{
    IEnumerable<GameInstallationInfo> Scan(CancellationToken ct);
}

public record GameInstallationInfo(string Name, string ExePath, string InstallDir, string StoreName, uint? AppId = null);

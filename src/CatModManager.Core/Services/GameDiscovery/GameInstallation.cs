namespace CatModManager.Core.Services.GameDiscovery;

/// <summary>A game installation found by one of the store scanners.</summary>
public record GameInstallation(
    string        DisplayName,
    string        ExecutablePath,
    string        GameFolder,
    string        StoreName,
    /// <summary>Null when no matching game support was found (user picks manually).</summary>
    IGameSupport? DetectedSupport);

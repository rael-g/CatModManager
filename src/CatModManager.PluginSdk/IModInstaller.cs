using System.Threading.Tasks;

namespace CatModManager.PluginSdk;

/// <summary>Intercepts mod installation for a specific archive format (FOMOD, BAIN, etc.).</summary>
public interface IModInstaller
{
    /// <summary>Returns true if this installer can handle the given archive.</summary>
    bool CanInstall(string archivePath);

    Task<InstallResult> InstallAsync(string archivePath, IInstallContext ctx);
}

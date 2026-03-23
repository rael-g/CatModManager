namespace CatModManager.PluginSdk;

public interface IInstallContext
{
    string        DestinationFolder { get; }
    IPluginLogger Log              { get; }

    /// <summary>
    /// When non-null, the FOMOD installer should auto-apply these preset choices
    /// and skip showing the wizard UI (used by Nexus Collection installs).
    /// </summary>
    FomodPreset?  FomodPreset      { get; }
}

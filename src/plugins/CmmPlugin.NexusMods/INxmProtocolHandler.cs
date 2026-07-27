namespace CmmPlugin.NexusMods;

/// <summary>
/// Registers/unregisters this app as the OS-level handler for the nxm:// URL
/// scheme, so "Mod Manager Download" buttons on Nexus Mods launch CMM directly.
/// </summary>
public interface INxmProtocolHandler
{
    bool IsRegistered();
    void Register(string exePath);
    void Unregister();
}

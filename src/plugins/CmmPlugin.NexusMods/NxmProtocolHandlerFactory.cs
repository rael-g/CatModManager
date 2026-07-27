using System;

namespace CmmPlugin.NexusMods;

internal static class NxmProtocolHandlerFactory
{
    public static INxmProtocolHandler Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsNxmProtocolHandler();
        if (OperatingSystem.IsLinux())   return new LinuxNxmProtocolHandler();
        return new NullNxmProtocolHandler();
    }
}

/// <summary>No-op handler for platforms without a supported nxm:// registration mechanism.</summary>
internal class NullNxmProtocolHandler : INxmProtocolHandler
{
    public bool IsRegistered() => false;
    public void Register(string exePath) { }
    public void Unregister() { }
}

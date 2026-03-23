using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CmmPlugin.NexusMods;

/// <summary>
/// Manages the nxm:// URL protocol handler registration in the Windows registry.
/// All methods silently no-op on non-Windows platforms.
/// </summary>
public class NxmProtocolService
{
    private const string NxmKeyPath = @"Software\Classes\nxm";

    public static bool IsRegistered()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
#if WINDOWS
            using var cmdKey = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Software\Classes\nxm\shell\open\command");
            if (cmdKey == null) return false;

            var registeredValue = cmdKey.GetValue(string.Empty) as string ?? string.Empty;
            var currentExe     = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

            return !string.IsNullOrEmpty(currentExe) &&
                   registeredValue.Contains(currentExe, StringComparison.OrdinalIgnoreCase);
#else
            return false;
#endif
        }
        catch
        {
            return false;
        }
    }

    public static void Register(string exePath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
#if WINDOWS
            using var nxmKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(NxmKeyPath);
            nxmKey.SetValue(string.Empty, "URL:NXM Protocol");
            nxmKey.SetValue("URL Protocol", string.Empty);

            using var openKey = nxmKey.CreateSubKey(@"shell\open\command");
            openKey.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");
#endif
        }
        catch
        {
            // Silently ignore registration errors
        }
    }

    public static void Unregister()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
#if WINDOWS
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(NxmKeyPath, throwOnMissingSubKey: false);
#endif
        }
        catch
        {
            // Silently ignore unregistration errors
        }
    }
}

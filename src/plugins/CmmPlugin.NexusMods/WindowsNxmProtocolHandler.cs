using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CmmPlugin.NexusMods;

/// <summary>
/// Registers nxm:// via the Windows registry, under Software\Classes\nxm for the current user.
/// Only ever instantiated on Windows (see <see cref="NxmProtocolHandlerFactory"/>).
/// </summary>
internal class WindowsNxmProtocolHandler : INxmProtocolHandler
{
    private const string NxmKeyPath = @"Software\Classes\nxm";

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST       = 0x0000;

    /// <summary>
    /// Tells Explorer/the shell to drop its cached protocol-association table.
    /// Without this, registry changes made by <see cref="Register"/> or <see cref="Unregister"/>
    /// are invisible to Explorer-mediated launches (Win+R, browsers using ShellExecute via the
    /// shell) until Explorer restarts, even though the registry itself is correct.
    /// </summary>
    private static void NotifyShellAssociationsChanged()
    {
        try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); }
        catch { /* best-effort */ }
    }

    public bool IsRegistered()
    {
        try
        {
            using var cmdKey = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Software\Classes\nxm\shell\open\command");
            if (cmdKey == null) return false;

            var registeredValue = cmdKey.GetValue(string.Empty) as string ?? string.Empty;
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

            return !string.IsNullOrEmpty(currentExe) &&
                   registeredValue.Contains(currentExe, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void Register(string exePath)
    {
        try
        {
            using var nxmKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(NxmKeyPath);
            nxmKey.SetValue(string.Empty, "URL:NXM Protocol");
            nxmKey.SetValue("URL Protocol", string.Empty);

            using var openKey = nxmKey.CreateSubKey(@"shell\open\command");
            openKey.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");
        }
        catch
        {
            // Silently ignore registration errors
        }
        finally
        {
            NotifyShellAssociationsChanged();
        }
    }

    public void Unregister()
    {
        try
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(NxmKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // Silently ignore unregistration errors
        }
        finally
        {
            NotifyShellAssociationsChanged();
        }
    }
}

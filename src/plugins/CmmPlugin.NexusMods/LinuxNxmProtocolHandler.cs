using System;
using System.Diagnostics;
using System.IO;
using CatModManager.PluginSdk;

namespace CmmPlugin.NexusMods;

/// <summary>
/// Registers nxm:// on Linux via a per-user .desktop entry plus xdg-mime, the
/// desktop-agnostic equivalent of the Windows registry approach. There is no
/// registry: association state lives in a .desktop file under
/// ~/.local/share/applications and in ~/.config/mimeapps.list (managed by xdg-mime).
/// Only ever instantiated on Linux (see <see cref="NxmProtocolHandlerFactory"/>).
/// </summary>
internal class LinuxNxmProtocolHandler : INxmProtocolHandler
{
    private const string DesktopId = "cmm-nxm-handler.desktop";
    private const string MimeType = "x-scheme-handler/nxm";

    private static string ApplicationsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "applications");

    private static string DesktopFilePath => Path.Combine(ApplicationsDir, DesktopId);

    /// <summary>
    /// The path the host would use to launch this build of CMM.
    ///
    /// A CMM running inside distrobox sees itself at "/run/host/home/you/…", but the .desktop file
    /// is always *executed* by the host — the browser and xdg-open live there — and that path does
    /// not exist outside the container. Registering it produces a handler the host silently cannot
    /// launch, which is why re-registering from inside the container appeared to do nothing.
    /// </summary>
    private static string CurrentExePath() =>
        ContainerEnvironment.ToHostPath(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty);

    public bool IsRegistered()
    {
        if (!File.Exists(DesktopFilePath)) return false;

        var currentExe = CurrentExePath();
        if (string.IsNullOrEmpty(currentExe)) return false;

        var content = File.ReadAllText(DesktopFilePath);
        if (!content.Contains(currentExe, StringComparison.Ordinal)) return false;

        var (output, _) = RunCapture("xdg-mime", $"query default {MimeType}");
        return output.Trim() == DesktopId;
    }

    public void Register(string exePath)
    {
        try
        {
            Directory.CreateDirectory(ApplicationsDir);
            exePath = ContainerEnvironment.ToHostPath(exePath);

            var content =
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Name=Cat Mod Manager (NXM Handler)\n" +
                // %u must NOT be quoted. The Desktop Entry spec forbids field codes inside a
                // quoted argument, and launchers honour that literally: with "%u" the app is
                // handed <'nxm://…'> — quotes included — so it never recognises its own argument
                // and opens a second window instead of forwarding the download. The executable
                // path does get quoted, since it may contain spaces.
                $"Exec=\"{exePath}\" %u\n" +
                "NoDisplay=true\n" +
                "StartupNotify=false\n" +
                $"MimeType={MimeType};\n";

            File.WriteAllText(DesktopFilePath, content);

            Run("update-desktop-database", ApplicationsDir);
            Run("xdg-mime", $"default {DesktopId} {MimeType}");
        }
        catch
        {
            // Silently ignore registration errors
        }
    }

    public void Unregister()
    {
        try
        {
            if (File.Exists(DesktopFilePath)) File.Delete(DesktopFilePath);
            Run("update-desktop-database", ApplicationsDir);
        }
        catch
        {
            // Silently ignore unregistration errors
        }
    }

    private static void Run(string fileName, string arguments)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            });
            proc?.WaitForExit(5000);
        }
        catch
        {
            // xdg-utils may not be installed; registration best-effort only.
        }
    }

    private static (string Output, int ExitCode) RunCapture(string fileName, string arguments)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (proc == null) return (string.Empty, -1);
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            return (output, proc.ExitCode);
        }
        catch
        {
            return (string.Empty, -1);
        }
    }
}

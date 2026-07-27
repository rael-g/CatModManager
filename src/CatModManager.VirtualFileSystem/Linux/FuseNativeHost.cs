using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CatModManager.VirtualFileSystem.Linux;

/// <summary>
/// Bridges <see cref="INativeFuseHost"/> to Mono.Fuse.NETStandard, whose
/// FileSystem.Start() blocks for the life of the mount and can only be
/// unmounted from outside its own callback context — hence the background
/// thread plus a shell-out to `fusermount` instead of the library's Stop().
/// </summary>
internal class FuseNativeHost : INativeFuseHost
{
    private readonly IFileSystem _impl;
    private CmmFuseAdapter? _adapter;
    private Thread? _fuseThread;
    private string? _mountPoint;

    public FuseNativeHost(IFileSystem impl) => _impl = impl;

    public void Mount(string mountPoint, string[] options)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("FUSE is only available on Linux.");

        _mountPoint = mountPoint;
        _adapter = new CmmFuseAdapter(_impl) { MountPoint = mountPoint };
        _adapter.ParseFuseArguments(options);

        Exception? startupError = null;
        _fuseThread = new Thread(() =>
        {
            try { _adapter.Start(); }
            catch (Exception ex) { startupError = ex; }
        }) { IsBackground = true };
        _fuseThread.Start();

        // Start() only returns early (or throws) if the mount itself fails, so a
        // short grace period is enough to tell a successful mount from a failed one.
        Thread.Sleep(300);

        if (!_fuseThread.IsAlive || startupError != null)
            throw new IOException($"Failed to mount FUSE filesystem at '{mountPoint}'.", startupError);
    }

    public void Unmount()
    {
        if (_mountPoint == null) return;
        RunFusermount(_mountPoint);
        _fuseThread?.Join(TimeSpan.FromSeconds(10));
    }

    public void Dispose()
    {
        _adapter?.Dispose();
        _adapter = null;
        _fuseThread = null;
        _mountPoint = null;
    }

    /// <summary>
    /// Unmounts, retrying a couple of times (a file manager or similar can hold the
    /// mount briefly busy right after the game/app stops reading from it), then
    /// falling back to a lazy unmount so the mount point is always detached by the
    /// time this returns — otherwise a caller that only logs "Unmounted." without
    /// checking `fusermount`'s exit code ends up lying: the OS-level mount lingers
    /// in a broken, disconnected state (looks empty) until something unmounts it.
    /// </summary>
    private static void RunFusermount(string mountPoint)
    {
        string bin = File.Exists("/usr/bin/fusermount") ? "fusermount" : "fusermount3";

        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0) Thread.Sleep(500);
            if (RunUnmountCommand(bin, $"-u \"{mountPoint}\"")) return;
        }

        // Still busy after retries — detach anyway so we don't leave a zombie mount.
        RunUnmountCommand(bin, $"-uz \"{mountPoint}\"");
    }

    private static bool RunUnmountCommand(string bin, string arguments)
    {
        var psi = new ProcessStartInfo(bin, arguments)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi);
        if (proc == null) return false;
        proc.WaitForExit(5000);
        return proc.ExitCode == 0;
    }
}

internal class FuseNativeHostFactory : INativeFuseHostFactory
{
    public INativeFuseHost CreateHost(object fileSystem) => new FuseNativeHost((IFileSystem)fileSystem);
}

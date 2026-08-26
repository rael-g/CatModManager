using System;
using System.IO;

namespace CatModManager.VirtualFileSystem.Linux;

public class FuseDriver : IFileSystemDriver
{
    private INativeFuseHost? _host;
    private readonly INativeFuseHostFactory _factory;
    private bool _isMounted;

    public bool IsMounted => _isMounted;

    public FuseDriver() : this(new FuseNativeHostFactory()) { }

    internal FuseDriver(INativeFuseHostFactory factory)
    {
        _factory = factory;
    }

    public void Mount(string mountPoint, IFileSystem fileSystem)
    {
        if (_isMounted) return;

        var host = _factory.CreateHost(fileSystem);
        // The Safe Swap overlay is always mounted on top of the game's existing
        // folder (which already has real files in it) — libfuse refuses that by
        // default as a safety check, so `nonempty` is required, not optional.
        // No `allow_other`: that requires `user_allow_other` in /etc/fuse.conf (or
        // root), which we can't ask an end user to set up. It's only needed to let
        // a *different* user than the mounter read the mount, and CMM + the game
        // process always run as the same user.
        var options = new[] { "-o", "ro", "-o", "nonempty" };

        try
        {
            host.Mount(mountPoint, options);
            _host = host;
            _isMounted = true;
        }
        catch (Exception ex)
        {
            host.Dispose();
            throw new IOException(Explain(mountPoint, ex), ex);
        }
    }

    /// <summary>
    /// Turns a mount failure into something actionable.
    ///
    /// Mono.Fuse reports every failure as "try running /sbin/modprobe fuse as the root user",
    /// which is almost never the reason. The real cause is printed by the fusermount child process
    /// on its own stderr, where nobody sees it — the common one being that fusermount refuses to
    /// mount over certain filesystems at all ("mounting over filesystem type 0x7366746e is
    /// forbidden", 0x7366746e being "ntfs"). Naming the filesystem gets the user much closer.
    /// </summary>
    private static string Explain(string mountPoint, Exception ex)
    {
        string fsType = FileSystemFactory.DescribeFilesystem(mountPoint);
        string reason = FileSystemFactory.SupportsFuseOverlay(mountPoint)
            ? $"FUSE is unavailable here — check that the 'fuse' module is loaded and that " +
              $"nothing else is already mounted at that path. ({ex.Message})"
            : $"fusermount refuses to mount over {fsType}, so the overlay cannot be used for " +
              $"this game. Mods have to be deployed with hard links instead.";

        return $"Failed to mount FUSE filesystem at '{mountPoint}' (on {fsType}). {reason}";
    }

    public void Unmount()
    {
        if (!_isMounted) return;
        _host?.Unmount();
        _host?.Dispose();
        _host = null;
        _isMounted = false;
    }

    public void Dispose() => Unmount();
}

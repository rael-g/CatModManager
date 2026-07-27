using System;

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
        catch
        {
            host.Dispose();
            throw;
        }
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

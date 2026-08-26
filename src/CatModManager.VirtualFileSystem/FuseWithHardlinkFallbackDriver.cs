using System;
using CatModManager.VirtualFileSystem.Linux;

namespace CatModManager.VirtualFileSystem;

/// <summary>
/// Deploys through the FUSE overlay when the kernel allows it, and through hard links when it does
/// not.
///
/// The overlay is preferred because it never touches the game folder: unmount and the install is
/// byte-for-byte what the store shipped. But <c>fusermount</c> keeps its own list of filesystems it
/// refuses to mount over — NTFS among them — and that list is not something we can read, only
/// discover. So rather than maintaining a guess about which filesystems will be rejected, this
/// driver treats a failed mount as the answer: try the overlay, and if it is refused, hard-link
/// instead. A failed FUSE mount leaves nothing behind, so there is no half-applied state to undo.
///
/// The known-refused check is still consulted first, purely to skip an attempt we are confident
/// will fail (and the alarming log line that comes with it).
///
/// Falling back is a real change in behaviour — hard links write into the game folder, with
/// backups — so it is always reported through <see cref="_log"/> rather than done quietly.
/// </summary>
internal sealed class FuseWithHardlinkFallbackDriver : IFileSystemDriver
{
    private readonly IHardlinkStateStore _store;
    private readonly Action<string>? _log;
    private IFileSystemDriver? _active;

    public FuseWithHardlinkFallbackDriver(IHardlinkStateStore store, Action<string>? log)
    {
        _store = store;
        _log = log;
    }

    public bool IsMounted => _active?.IsMounted ?? false;

    public void Mount(string mountPoint, IFileSystem fileSystem)
    {
        if (IsMounted) return;

        if (FileSystemFactory.SupportsFuseOverlay(mountPoint))
        {
            var fuse = new FuseDriver();
            try
            {
                fuse.Mount(mountPoint, fileSystem);
                _active = fuse;
                return;
            }
            catch (Exception ex)
            {
                fuse.Dispose();
                _log?.Invoke($"FUSE overlay unavailable for '{mountPoint}' ({ex.Message}). " +
                             "Falling back to hard links — mod files are written into the game " +
                             "folder and removed on unmount.");
            }
        }
        else
        {
            _log?.Invoke($"'{mountPoint}' is on {FileSystemFactory.DescribeFilesystem(mountPoint)}, " +
                         "which cannot host a FUSE mount. Using hard links instead.");
        }

        var hardlinks = new HardlinkDriver(_store);
        hardlinks.Mount(mountPoint, fileSystem);
        _active = hardlinks;
    }

    public void Unmount()
    {
        _active?.Unmount();
        _active = null;
    }

    public void Dispose()
    {
        _active?.Dispose();
        _active = null;
    }
}

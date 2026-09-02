using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CatModManager.VirtualFileSystem;

public static class FileSystemFactory
{
    /// <summary>
    /// Picks how mods get deployed to <paramref name="targetPath"/>. Hard links, everywhere.
    ///
    /// Linux used to prefer a FUSE overlay, because it leaves the game folder untouched. That
    /// driver is retired: it only ever worked on one platform, so it doubled the polish, testing
    /// and maintenance for the minority of users — and, worse, it was not merely a parallel path.
    /// A FUSE mount left behind by a killed process stays registered but disconnected, and then
    /// every access under it raises ENOTCONN. That took down the hard link fallback with it: the
    /// overlay failed because something was already mounted there, and WalkAndLink then died
    /// walking into the dead mount. The experimental driver was breaking the stable one.
    ///
    /// Hard links carry the same promise on all three platforms and were already the real driver
    /// here anyway — NTFS refuses FUSE mounts, and a Windows game library on NTFS played through
    /// Proton is the common Linux setup. The FUSE implementation stays in the repository history;
    /// it is simply no longer built or shipped.
    ///
    /// The signature keeps <paramref name="targetPath"/> and <paramref name="log"/> so callers do
    /// not have to change, and so a future driver that does need them can be slotted back in.
    /// </summary>
    /// <param name="targetPath">Where mods will be deployed. Unused by the hard link driver.</param>
    /// <param name="log">Unused by the hard link driver.</param>
    public static IFileSystemDriver CreateDriver(
        IHardlinkStateStore stateStore, string? targetPath = null, Action<string>? log = null)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new HardlinkDriver(stateStore);

        throw new PlatformNotSupportedException("No file system driver available for this platform.");
    }

    /// <summary>
    /// Driver used to clean up after a crash, on every platform.
    ///
    /// This must be the hard link driver: it is the only one holding persistent state (the deployed
    /// links and their backups, in IHardlinkStateStore), and a fresh instance with no in-memory
    /// state loads and reverts all of it. Returning a FuseDriver here — which is what choosing by
    /// platform used to do on Linux — made crash recovery a silent no-op, because its Unmount()
    /// returns immediately when it never mounted anything. Orphaned FUSE mounts are a separate
    /// concern, handled by IVfsStateService against /proc/mounts.
    /// </summary>
    public static IFileSystemDriver CreateCrashRecoveryDriver(IHardlinkStateStore stateStore) =>
        new HardlinkDriver(stateStore);

    /// <summary>
    /// The filesystem type backing <paramref name="path"/>, found by walking up to the nearest
    /// existing directory and matching it against the longest mount point in /proc/mounts.
    /// Returns null when it cannot be determined.
    ///
    /// Kept after the FUSE driver was retired because it is the only thing that can name the
    /// filesystem in a diagnostic — a hard link failure across a device boundary reads as a bare
    /// "Invalid cross-device link" otherwise.
    /// </summary>
    internal static string? GetFilesystemType(string path)
    {
        try
        {
            string? dir = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                dir = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(dir)) return null;

            string? bestType = null;
            int bestLength = -1;

            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                // device  mountpoint  fstype  options  dump  pass
                var parts = line.Split(' ');
                if (parts.Length < 3) continue;

                // /proc/mounts octal-escapes spaces and other specials in the mount point.
                string mountPoint = parts[1].Replace(@"\040", " ");
                string fsType = parts[2];

                bool covers = dir.Equals(mountPoint, StringComparison.Ordinal)
                              || (mountPoint == "/" )
                              || dir.StartsWith(
                                     mountPoint.EndsWith('/') ? mountPoint : mountPoint + "/",
                                     StringComparison.Ordinal);

                if (covers && mountPoint.Length > bestLength)
                {
                    bestLength = mountPoint.Length;
                    bestType = fsType;
                }
            }

            return bestType;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Human-readable filesystem name for a path, for log and error messages.</summary>
    internal static string DescribeFilesystem(string? path) =>
        (path == null ? null : GetFilesystemType(path)) ?? "an unknown filesystem";
}

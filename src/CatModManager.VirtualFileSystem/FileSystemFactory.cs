using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CatModManager.VirtualFileSystem;

public static class FileSystemFactory
{
    /// <summary>
    /// Picks how mods get deployed to <paramref name="targetPath"/>.
    ///
    /// Windows always uses hard links. Linux prefers the FUSE overlay, because it leaves the game
    /// folder untouched — but <c>fusermount</c> refuses to mount over some filesystems, NTFS among
    /// them ("mounting over filesystem type 0x7366746e is forbidden"). Plenty of people keep a
    /// Windows game library on NTFS and play it under Proton, and for them the overlay simply is
    /// not available. NTFS does support hard links, so fall back to that rather than failing.
    /// </summary>
    /// <param name="targetPath">Where mods will be deployed.</param>
    /// <param name="log">Receives a note when the overlay is unavailable and hard links are used.</param>
    public static IFileSystemDriver CreateDriver(
        IHardlinkStateStore stateStore, string? targetPath = null, Action<string>? log = null)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new HardlinkDriver(stateStore);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new FuseWithHardlinkFallbackDriver(stateStore, log);

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
    /// Filesystems <c>fusermount</c> refuses to mount over. Kept deliberately short: every entry
    /// here is one we have actually seen rejected, not a guess about what might be rejected.
    /// </summary>
    private static readonly string[] FuseRefusedFilesystems = { "ntfs", "ntfs3" };

    /// <summary>Human-readable filesystem name for a path, for log and error messages.</summary>
    internal static string DescribeFilesystem(string? path) =>
        (path == null ? null : GetFilesystemType(path)) ?? "an unknown filesystem";

    internal static bool SupportsFuseOverlay(string? targetPath)
    {
        if (string.IsNullOrEmpty(targetPath)) return true;

        string? fsType = GetFilesystemType(targetPath);
        if (fsType == null) return true;   // unknown — let the mount attempt decide

        return IsFuseMountableFilesystem(fsType);
    }

    /// <summary>Whether fusermount will agree to mount over the given filesystem type.</summary>
    internal static bool IsFuseMountableFilesystem(string fsType)
    {
        foreach (var refused in FuseRefusedFilesystems)
            if (string.Equals(fsType, refused, StringComparison.OrdinalIgnoreCase))
                return false;

        return true;
    }

    /// <summary>
    /// The filesystem type backing <paramref name="path"/>, found by walking up to the nearest
    /// existing directory and matching it against the longest mount point in /proc/mounts.
    /// Returns null when it cannot be determined.
    /// </summary>
    private static string? GetFilesystemType(string path)
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
}

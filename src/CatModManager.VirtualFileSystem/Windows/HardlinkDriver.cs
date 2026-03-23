using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CatModManager.VirtualFileSystem.Windows;

/// <summary>
/// Deploys mod files into the game root via NTFS hard links at mount time,
/// and removes them at unmount time. No VFS kernel driver is involved.
///
/// Hard link semantics:
///   • CreateHardLinkW adds a new directory entry pointing to the same MFT record
///     as the source file — O(1), no bytes copied, no extra disk space.
///   • The game sees the file as a normal NTFS file: DRM, anti-cheat and file
///     verifiers (Steam/GOG) are fully transparent.
///
/// Cross-volume fallback:
///   If the mod folder is on a different drive than the game folder,
///   CreateHardLinkW fails with ERROR_NOT_SAME_DEVICE. In that case the driver
///   falls back to File.Copy. Unmount cleans up copied files exactly like links.
///
/// Backup strategy:
///   If a game file already exists at the link destination, it is renamed to
///   ".<originalName>" (hidden dot-prefix) before linking. No suffix is added.
///   On Windows the backup is also given the Hidden attribute.
///   On unmount the link/copy is deleted and the backup is restored.
///
/// Crash recovery:
///   All deployed links are persisted via IHardlinkStateStore (SQLite-backed).
///   VfsOrchestrationService calls Unmount() on startup — a fresh instance with
///   no in-memory state will load all stale DB entries and clean them up.
/// </summary>
public class HardlinkDriver : IFileSystemDriver
{
    private readonly IHardlinkStateStore _store;

    private string? _mountPoint;
    private bool    _isMounted;

    public bool IsMounted => _isMounted;

    public HardlinkDriver(IHardlinkStateStore store)
    {
        _store = store;
    }

    // ── IFileSystemDriver ────────────────────────────────────────────────────

    public void Mount(string mountPoint, IFileSystem fileSystem)
    {
        if (_isMounted) return;

        _mountPoint = mountPoint;
        var entries = new List<HardlinkStateEntry>();

        WalkAndLink(this, fileSystem, "", mountPoint, entries);

        try
        {
            _store.Save(mountPoint, entries);
        }
        catch (UnauthorizedAccessException ex)
        {
            foreach (var e in entries)
            {
                try
                {
                    if (File.Exists(e.DestPath)) File.Delete(e.DestPath);
                    if (e.BackupPath != null && File.Exists(e.BackupPath))
                        File.Move(e.BackupPath, e.DestPath, overwrite: true);
                }
                catch { }
            }
            throw new IOException(
                $"Cannot persist crash-recovery state for '{mountPoint}'. " +
                $"Run CMM as administrator or move the game outside of Program Files. ({ex.Message})", ex);
        }

        _isMounted = true;
    }

    public void Unmount()
    {
        IReadOnlyList<HardlinkStateEntry> entries;

        if (!_isMounted && _mountPoint == null)
        {
            // Crash recovery: clean up any stale links from a previous session.
            entries = _store.Load(null);
            if (entries.Count == 0) return;
        }
        else
        {
            entries = _store.Load(_mountPoint);
        }

        var dirsToCheck = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in entries)
        {
            try
            {
                if (File.Exists(e.DestPath))
                    File.Delete(e.DestPath);

                if (e.BackupPath != null && File.Exists(e.BackupPath))
                    File.Move(e.BackupPath, e.DestPath, overwrite: true);
            }
            catch { /* best-effort */ }

            // Collect parent directories for empty-dir cleanup
            var dir = Path.GetDirectoryName(e.DestPath);
            while (!string.IsNullOrEmpty(dir))
            {
                dirsToCheck.Add(dir);
                dir = Path.GetDirectoryName(dir);
            }
        }

        // Remove empty directories that were created for mod files, deepest first
        foreach (var dir in dirsToCheck.OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch { /* best-effort */ }
        }

        _store.Clear(_mountPoint); // null → clears all

        _isMounted  = false;
        _mountPoint = null;
    }

    public void Dispose() => Unmount();

    // ── Link walk ────────────────────────────────────────────────────────────

    private static void WalkAndLink(
        HardlinkDriver driver, IFileSystem fs, string relDir, string mountPoint, List<HardlinkStateEntry> entries)
    {
        foreach (var name in fs.ReadDirectory(relDir))
        {
            var rel  = string.IsNullOrEmpty(relDir) ? name : relDir + Path.DirectorySeparatorChar + name;
            var info = fs.GetInfo(rel);
            if (info == null) continue;

            if (info.IsDirectory)
            {
                WalkAndLink(driver, fs, rel, mountPoint, entries);
                continue;
            }

            var physPath = fs.GetPhysicalPath(rel);
            if (physPath == null)
            {
                System.Diagnostics.Debug.WriteLine($"[HardlinkDriver] Skipping archive-backed file '{rel}' — extract the mod to a folder to deploy it.");
                continue;
            }

            var destPath = Path.Combine(mountPoint, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            string? backupPath = null;
            if (File.Exists(destPath))
            {
                backupPath = Path.Combine(
                    Path.GetDirectoryName(destPath)!,
                    '.' + Path.GetFileName(destPath));
                File.Move(destPath, backupPath, overwrite: true);
                TryHide(backupPath);
            }

            driver.DeployFile(physPath, destPath, rel);

            entries.Add(new HardlinkStateEntry(rel, destPath, backupPath));
        }
    }

    // ── Deploy (overridable for testing) ─────────────────────────────────────

    /// <summary>
    /// Deploys <paramref name="sourcePath"/> to <paramref name="destPath"/>.
    /// Tries a hard link first; falls back to File.Copy when source and destination
    /// are on different volumes (Win32 error 17 — ERROR_NOT_SAME_DEVICE).
    /// </summary>
    internal virtual void DeployFile(string sourcePath, string destPath, string relPath)
    {
        if (!CreateHardLinkW(destPath, sourcePath, IntPtr.Zero))
        {
            int err = Marshal.GetLastWin32Error();
            const int ErrorNotSameDevice = 17; // cross-volume
            if (err == ErrorNotSameDevice)
                File.Copy(sourcePath, destPath, overwrite: true);
            else
                throw new IOException($"CreateHardLink failed for '{relPath}': Win32 error {err}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void TryHide(string path)
    {
        try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); }
        catch { }
    }

    // ── P/Invoke ─────────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);
}

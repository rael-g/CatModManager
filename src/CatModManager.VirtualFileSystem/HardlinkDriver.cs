using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CatModManager.VirtualFileSystem;

/// <summary>
/// Deploys mod files into the game root via hard links at mount time, and removes
/// them at unmount time. No VFS kernel driver is involved.
///
/// Hard link semantics:
///   • A hard link is a second directory entry pointing at the same inode / MFT
///     record — O(1), no bytes copied, no extra disk space.
///   • The game sees a perfectly normal file: DRM, anti-cheat and file verifiers
///     (Steam/GOG) are fully transparent.
///
/// Works on Windows (CreateHardLinkW) and Linux (link(2)). On Linux this is the
/// only option when the game lives on NTFS: fusermount refuses to mount over an
/// ntfs3 filesystem, so the FUSE overlay is unavailable there — but NTFS itself
/// supports hard links, so this driver works fine.
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

        // Everything from here to Save() is the window where the game folder is modified but
        // nothing is recorded anywhere. Any escape from it without undoing the work leaves the
        // user's real game files renamed to dot-backups that no future unmount and no crash
        // recovery will ever restore, because the DB never learned they existed.
        try
        {
            WalkAndLink(this, fileSystem, "", mountPoint, entries);
            _store.Save(mountPoint, entries);
        }
        catch (Exception ex)
        {
            Rollback(entries);

            // Losing write access to the state store is worth its own advice; anything else
            // propagates as-is, with the diagnosis WalkAndLink already attached.
            if (ex is UnauthorizedAccessException)
                throw new IOException(
                    $"Cannot persist crash-recovery state for '{mountPoint}'. " +
                    $"Run CMM as administrator or move the game outside of Program Files. ({ex.Message})", ex);

            throw;
        }

        _isMounted = true;
    }

    /// <summary>
    /// Undoes a partial mount: drops each deployed link and puts the displaced game file back.
    ///
    /// Deliberately best-effort per entry — one file that refuses to budge (locked by a running
    /// game, say) must not strand the other several hundred. Runs in reverse so a later entry
    /// nested under an earlier one is cleared first.
    /// </summary>
    private static void Rollback(List<HardlinkStateEntry> entries)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            try
            {
                if (File.Exists(e.DestPath)) File.Delete(e.DestPath);
                if (e.BackupPath != null && File.Exists(e.BackupPath))
                {
                    File.Move(e.BackupPath, e.DestPath, overwrite: true);
                    TryClearHidden(e.DestPath);
                }
            }
            catch { /* best-effort: restore as much as can be restored */ }
        }
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

        var dirsToCheck = new SortedSet<string>(PathComparer);

        foreach (var e in entries)
        {
            try
            {
                if (File.Exists(e.DestPath))
                    File.Delete(e.DestPath);

                if (e.BackupPath != null && File.Exists(e.BackupPath))
                {
                    File.Move(e.BackupPath, e.DestPath, overwrite: true);
                    TryClearHidden(e.DestPath);
                }
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

            // physPath may carry a \\?\ long-path prefix; strip it for comparison.
            var physPathNorm = physPath.StartsWith(@"\\?\", StringComparison.Ordinal) ? physPath[4..] : physPath;

            // File is already at the destination (unoverridden base game file) — no action needed.
            if (string.Equals(physPathNorm, destPath, PathComparison))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            string? backupPath = null;
            if (File.Exists(destPath))
            {
                backupPath = Path.Combine(
                    Path.GetDirectoryName(destPath)!,
                    '.' + Path.GetFileName(destPath));

                try
                {
                    File.Move(destPath, backupPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    // The raw exception names one path and does not say which role it played, so a
                    // failure here read as "could not find <backup>" — a file that is supposed not
                    // to exist yet. Say what was being moved where, and what was actually on disk.
                    throw new IOException(
                        $"Could not set aside the existing file before linking a mod over it.\n" +
                        $"  entry:      '{rel}'\n" +
                        $"  mount point:'{mountPoint}'\n" +
                        $"  move from:  '{destPath}' (exists now: {File.Exists(destPath)})\n" +
                        $"  move to:    '{backupPath}' (exists now: {File.Exists(backupPath)})\n" +
                        $"  mod source: '{physPath}' (exists now: {File.Exists(physPath)})\n" +
                        $"  reason:     {ex.GetType().Name}: {ex.Message}\n" +
                        Diagnose(destPath, backupPath), ex);
                }

                TryHide(backupPath);
            }

            // Recorded before deploying, not after. The game file has already been moved aside at
            // this point, so if the link fails the entry is the only thing that knows where the
            // real file went — appending afterwards means a failed deploy strands it permanently.
            entries.Add(new HardlinkStateEntry(rel, destPath, backupPath));

            driver.DeployFile(physPath, destPath, rel);
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
        if (OperatingSystem.IsWindows())
        {
            if (CreateHardLinkW(destPath, sourcePath, IntPtr.Zero)) return;

            int err = Marshal.GetLastWin32Error();
            const int ErrorNotSameDevice = 17;
            if (err == ErrorNotSameDevice)
                File.Copy(sourcePath, destPath, overwrite: true);
            else
                throw new IOException($"CreateHardLink failed for '{relPath}': Win32 error {err}");
            return;
        }

        if (link(sourcePath, destPath) == 0) return;

        int errno = Marshal.GetLastWin32Error();
        const int EXDEV = 18;  // cross-device link — mods on a different filesystem
        if (errno == EXDEV)
            File.Copy(sourcePath, destPath, overwrite: true);
        else
            throw new IOException($"link() failed for '{relPath}': errno {errno}");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Windows paths are case-insensitive; Linux paths are not.</summary>
    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>
    /// Extra detail for a move that failed for no visible reason.
    ///
    /// Read-only on purpose. An earlier version retried the move to see whether the failure was
    /// stable, and undid it when it wasn't — writing to the game folder from inside an error path,
    /// at the exact moment the filesystem is behaving unexpectedly, is the last place to be taking
    /// liberties.
    ///
    /// What it answers: is the string we handed the OS the string we printed (invisible or
    /// non-UTF8 characters do not survive a log line), does the destination directory exist and
    /// hold something under that name already, and what does the source actually look like.
    /// </summary>
    private static string Diagnose(string source, string dest)
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            sb.Append("  from bytes: ").AppendLine(Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(source)));
            sb.Append("  to bytes:   ").AppendLine(Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(dest)));

            var dir = Path.GetDirectoryName(source)!;
            sb.Append("  dest dir:   '").Append(dir).Append("' exists: ").AppendLine(Directory.Exists(dir).ToString());

            // Every entry sharing the stem, dot-prefixed ones included. The pattern used to be
            // "<stem>*", which silently skips exactly the backup names this code creates — so the
            // one entry worth seeing was the one that could never show up.
            var stem = Path.GetFileNameWithoutExtension(source);
            var kin = Directory.EnumerateFileSystemEntries(dir)
                               .Select(Path.GetFileName)
                               .Where(n => n != null && n.Contains(stem, PathComparison))
                               .OrderBy(n => n, PathComparer);
            sb.Append("  siblings:   ").AppendLine(string.Join(", ", kin));

            // A directory sitting where the backup should go, or a dangling symlink, both read as
            // "does not exist" to File.Exists while still being in the way of a rename.
            sb.Append("  dest kind:  ").AppendLine(
                File.Exists(dest) ? "file" : Directory.Exists(dest) ? "DIRECTORY" : "absent");

            var info = new FileInfo(source);
            sb.Append("  source:     length ").Append(info.Length)
              .Append(", attributes ").Append(info.Attributes)
              .Append(", last written ").Append(info.LastWriteTimeUtc.ToString("O"));
        }
        catch (Exception ex) { sb.Append("  (diagnosis failed: ").Append(ex.Message).Append(')'); }

        return sb.ToString();
    }

    private static void TryHide(string path)
    {
        try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); }
        catch { }
    }

    private static void TryClearHidden(string path)
    {
        try { File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.Hidden); }
        catch { }
    }

    // ── P/Invoke ─────────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);

    /// <summary>POSIX hard link. Note the argument order is (existing, new) — the
    /// opposite of CreateHardLinkW.</summary>
    [DllImport("libc", EntryPoint = "link", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int link(string oldpath, string newpath);
}

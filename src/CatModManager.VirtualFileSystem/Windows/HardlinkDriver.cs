using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

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
///   • Because mods are always installed inside the game folder (same volume),
///     the same-volume constraint of hard links is always satisfied.
///
/// Backup strategy:
///   If a game file already exists at the link destination, it is renamed to
///   ".<originalName>" (hidden dot-prefix) before linking. No suffix is added.
///   On Windows the backup is also given the Hidden attribute.
///   On unmount the link is deleted and the backup is restored.
///
/// Crash recovery:
///   All deployed links are recorded in ".cmm_hl.json" at the game root.
///   The file is hidden. VfsOrchestrationService calls Unmount() on startup
///   when IsMounted is still true, which reads and reverses the state file.
/// </summary>
public sealed class HardlinkDriver : IFileSystemDriver
{
    private const string StateFileName = ".cmm_hl.json";

    private string? _mountPoint;
    private bool    _isMounted;

    public bool IsMounted          => _isMounted;

    // ── IFileSystemDriver ────────────────────────────────────────────────────

    public void Mount(string mountPoint, IFileSystem fileSystem)
    {
        if (_isMounted) return;

        _mountPoint = mountPoint;
        var entries = new List<HardlinkEntry>();

        WalkAndLink(fileSystem, "", mountPoint, entries);

        var statePath = Path.Combine(mountPoint, StateFileName);
        File.WriteAllText(statePath, JsonSerializer.Serialize(entries,
            new JsonSerializerOptions { WriteIndented = false }));
        TryHide(statePath);

        _isMounted = true;
    }

    public void Unmount()
    {
        if (!_isMounted && _mountPoint == null) return;

        var root      = _mountPoint!;
        var statePath = Path.Combine(root, StateFileName);
        var entries   = LoadState(statePath);

        foreach (var e in entries)
        {
            try
            {
                if (File.Exists(e.DestPath))
                    File.Delete(e.DestPath);

                if (e.BackupPath != null && File.Exists(e.BackupPath))
                    File.Move(e.BackupPath, e.DestPath, overwrite: true);
            }
            catch { /* best-effort: leave the state file for manual recovery */ }
        }

        try { File.Delete(statePath); } catch { }

        _isMounted  = false;
        _mountPoint = null;
    }

    public void Dispose() => Unmount();

    // ── Link walk ────────────────────────────────────────────────────────────

    private static void WalkAndLink(
        IFileSystem fs, string relDir, string mountPoint, List<HardlinkEntry> entries)
    {
        foreach (var name in fs.ReadDirectory(relDir))
        {
            var rel  = string.IsNullOrEmpty(relDir) ? name : relDir + Path.DirectorySeparatorChar + name;
            var info = fs.GetInfo(rel);
            if (info == null) continue;

            if (info.IsDirectory)
            {
                WalkAndLink(fs, rel, mountPoint, entries);
                continue;
            }

            var physPath = fs.GetPhysicalPath(rel);
            if (physPath == null) continue; // archive-backed — skipped

            var destPath = Path.Combine(mountPoint, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            // Backup any existing game file with a hidden dot-prefixed name
            string? backupPath = null;
            if (File.Exists(destPath))
            {
                backupPath = Path.Combine(
                    Path.GetDirectoryName(destPath)!,
                    '.' + Path.GetFileName(destPath));
                File.Move(destPath, backupPath, overwrite: true);
                TryHide(backupPath);
            }

            if (!CreateHardLinkW(destPath, physPath, IntPtr.Zero))
                throw new IOException(
                    $"CreateHardLink failed for '{rel}': Win32 error {Marshal.GetLastWin32Error()}");

            entries.Add(new HardlinkEntry(rel, destPath, backupPath));
        }
    }

    // ── State file ───────────────────────────────────────────────────────────

    private static List<HardlinkEntry> LoadState(string statePath)
    {
        try
        {
            if (!File.Exists(statePath)) return [];
            return JsonSerializer.Deserialize<List<HardlinkEntry>>(
                       File.ReadAllText(statePath)) ?? [];
        }
        catch { return []; }
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

    // ── State record ─────────────────────────────────────────────────────────

    private record HardlinkEntry(
        string  RelPath,
        string  DestPath,
        string? BackupPath);
}

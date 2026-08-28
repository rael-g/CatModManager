using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatModManager.VirtualFileSystem;
using Xunit;

namespace CatModManager.Tests.VirtualFileSystem;

/// <summary>
/// Mounting on top of links this driver itself left behind.
///
/// A session that is killed — or one whose rollback cannot finish — leaves mod hard links sitting
/// in the game folder. The next mount then finds a file at the destination and, knowing nothing
/// about where it came from, treats it as the player's original: renames it to a dot-backup and
/// links the mod over it. That backup is a mod file wearing the game file's name, and the unmount
/// after it "restores" the mod on top of the real installation. The one guarantee the whole
/// project rests on — that the game folder comes back exactly as it was — is gone.
///
/// The fix is identity: a destination that is already a hard link to the very file being deployed
/// is this driver's own work, not the player's, and is adopted rather than backed up.
/// </summary>
public class RemountOverLeftoverLinksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CMM_Leftover_" + Guid.NewGuid().ToString("N"));
    private readonly string _gameDir;
    private readonly string _modDir;

    public RemountOverLeftoverLinksTests()
    {
        _gameDir = Path.Combine(_root, "Game");
        _modDir  = Path.Combine(_root, "Mod");
        Directory.CreateDirectory(_gameDir);
        Directory.CreateDirectory(_modDir);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void ALeftoverLinkIsAdoptedInsteadOfBackedUpAsIfItWereTheGameFile()
    {
        var fs = ModAdding("meshes.nif");
        var store = new MemoryStore();

        // First mount deploys the link; then the process "dies" without unmounting and without the
        // state store ever learning about it.
        new LinkingDriver(new MemoryStore()).Mount(_gameDir, fs);
        Assert.True(File.Exists(Path.Combine(_gameDir, "meshes.nif")));

        new LinkingDriver(store).Mount(_gameDir, fs);

        var strays = Directory.GetFiles(_gameDir).Select(Path.GetFileName).Where(f => f!.StartsWith('.')).ToArray();
        Assert.True(strays.Length == 0, "A mod file was filed away as if it were the player's: " + string.Join(", ", strays));

        var entry = Assert.Single(store.Load(_gameDir));
        Assert.Null(entry.BackupPath);
    }

    /// <summary>
    /// Adopting is only worth anything if unmount then removes it. Otherwise the leftover simply
    /// becomes permanent under a friendlier name.
    /// </summary>
    [Fact]
    public void AnAdoptedLeftoverIsRemovedByTheNextUnmount()
    {
        var fs = ModAdding("meshes.nif");
        var store = new MemoryStore();

        new LinkingDriver(new MemoryStore()).Mount(_gameDir, fs);

        var driver = new LinkingDriver(store);
        driver.Mount(_gameDir, fs);
        driver.Unmount();

        Assert.False(File.Exists(Path.Combine(_gameDir, "meshes.nif")),
            "The leftover link outlived the unmount that adopted it.");
    }

    /// <summary>
    /// A real game file at the destination is a different thing entirely and must still be set
    /// aside — the identity check must not swallow the case it does not apply to.
    /// </summary>
    [Fact]
    public void AnUnrelatedFileAtTheDestinationIsStillBackedUp()
    {
        File.WriteAllText(Path.Combine(_gameDir, "meshes.nif"), "REAL GAME");
        var fs = ModAdding("meshes.nif");
        var store = new MemoryStore();

        var driver = new LinkingDriver(store);
        driver.Mount(_gameDir, fs);

        var entry = Assert.Single(store.Load(_gameDir));
        Assert.NotNull(entry.BackupPath);
        Assert.Equal("REAL GAME", File.ReadAllText(entry.BackupPath!));

        driver.Unmount();
        Assert.Equal("REAL GAME", File.ReadAllText(Path.Combine(_gameDir, "meshes.nif")));
    }

    /// <summary>
    /// What rollback cannot undo has to be written down. An unrecorded link is invisible to every
    /// later unmount and to crash recovery, which is exactly how the game folder accumulates mod
    /// files nobody can account for.
    /// </summary>
    [Fact]
    public void WhatRollbackCannotUndoIsRecordedForTheNextUnmount()
    {
        var fs = ModAdding("a.esp", "b.esp");
        var store = new MemoryStore();

        // Deploys a.esp, then fails on b.esp — and the deployed a.esp refuses to be deleted.
        var driver = new UndeletableDeployDriver(store, failOnCall: 2, new IOException("errno 5"));

        Assert.Throws<IOException>(() => driver.Mount(_gameDir, fs));

        var recorded = store.Load(_gameDir);
        Assert.Contains(recorded, e => e.RelPath == "a.esp");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Files the mod adds to the game folder, with no base-game file of the same name.</summary>
    private FlatFs ModAdding(params string[] names)
    {
        foreach (var n in names) File.WriteAllText(Path.Combine(_modDir, n), "mod: " + n);
        return new FlatFs(names.ToDictionary(n => n, n => Path.Combine(_modDir, n)));
    }

    /// <summary>
    /// Deploys with a real hard link on Windows and a copy elsewhere. Copies have no shared
    /// identity, so on Linux these tests exercise the fallback path rather than the fix.
    /// </summary>
    private class LinkingDriver : HardlinkDriver
    {
        public LinkingDriver(IHardlinkStateStore store) : base(store) { }
    }

    /// <summary>Deploys for real until the chosen call, then throws — and blocks the cleanup of
    /// what it already deployed, standing in for a file the OS will not let go of.</summary>
    private class UndeletableDeployDriver : HardlinkDriver
    {
        private readonly int       _failOnCall;
        private readonly Exception _failure;
        private int _calls;

        public UndeletableDeployDriver(IHardlinkStateStore store, int failOnCall, Exception failure) : base(store)
            => (_failOnCall, _failure) = (failOnCall, failure);

        internal override void DeployFile(string sourcePath, string destPath, string relPath)
        {
            if (++_calls == _failOnCall) throw _failure;

            // A read-only file cannot be deleted, which is what rollback tries to do next.
            File.Copy(sourcePath, destPath, overwrite: true);
            File.SetAttributes(destPath, FileAttributes.ReadOnly);
        }
    }

    private class MemoryStore : IHardlinkStateStore
    {
        private readonly List<HardlinkStateEntry> _entries = new();
        public void Save(string mountPoint, IReadOnlyList<HardlinkStateEntry> entries) => _entries.AddRange(entries);
        public IReadOnlyList<HardlinkStateEntry> Load(string? mountPoint) => _entries.ToList();
        public void Clear(string? mountPoint) => _entries.Clear();
    }

    /// <summary>A flat set of files in the mount root, in a fixed order.</summary>
    private class FlatFs : IFileSystem
    {
        private readonly Dictionary<string, string> _files;
        public FlatFs(Dictionary<string, string> files) => _files = files;

        public FileSystemNodeInfo? GetInfo(string path)
        {
            var name = path.Trim(Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(name)) return new FileSystemNodeInfo { IsDirectory = true };
            return _files.ContainsKey(name) ? new FileSystemNodeInfo { IsDirectory = false } : null;
        }

        public IEnumerable<string> ReadDirectory(string path)
            => string.IsNullOrEmpty(path.Trim(Path.DirectorySeparatorChar)) ? _files.Keys : Enumerable.Empty<string>();

        public Stream? OpenFile(string path)
            => _files.TryGetValue(path.Trim(Path.DirectorySeparatorChar), out var p) ? File.OpenRead(p) : null;

        public string? GetPhysicalPath(string path)
            => _files.TryGetValue(path.Trim(Path.DirectorySeparatorChar), out var p) ? p : null;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatModManager.VirtualFileSystem;
using CatModManager.Tests.Support;
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
    private readonly LinkLedger _links = new();

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
        new LinkingDriver(new MemoryStore(), _links).Mount(_gameDir, fs);
        Assert.True(File.Exists(Path.Combine(_gameDir, "meshes.nif")));

        new LinkingDriver(store, _links).Mount(_gameDir, fs);

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

        new LinkingDriver(new MemoryStore(), _links).Mount(_gameDir, fs);

        var driver = new LinkingDriver(store, _links);
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

        var driver = new LinkingDriver(store, _links);
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
    /// Answers the identity question from what the test deployed, instead of asking the filesystem.
    ///
    /// Only the answer is platform work — <see cref="HardlinkDriver.IsSameFile"/> needs
    /// GetFileInformationByHandle on Windows and st_ino/st_dev elsewhere. The rule built on the
    /// answer is the same everywhere, and it is the rule these tests are about, so they run
    /// everywhere. The real implementation is covered by the integration tests next door.
    /// </summary>
    private class LinkingDriver : HardlinkDriver
    {
        private readonly LinkLedger _links;

        public LinkingDriver(IHardlinkStateStore store, LinkLedger links) : base(store) => _links = links;

        internal override void DeployFile(string sourcePath, string destPath, string relPath)
        {
            File.Copy(sourcePath, destPath, overwrite: true);
            _links.Record(destPath, sourcePath);
        }

        internal override bool IsSameFile(string a, string b) => _links.SameFile(a, b);
    }

    /// <summary>
    /// Which paths ended up being the same file. Outlives the driver that made them, because the
    /// disk does: the whole point is a second driver meeting links a dead one left behind.
    /// </summary>
    private class LinkLedger
    {
        private readonly HashSet<string> _pairs = new(StringComparer.Ordinal);

        public void Record(string dest, string source) => _pairs.Add(Key(dest, source));

        public bool SameFile(string a, string b) =>
            File.Exists(a) && File.Exists(b) && _pairs.Contains(Key(a, b));

        private static string Key(string a, string b) => a + "\0" + b;
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

        private readonly HashSet<string> _stuck = new(StringComparer.Ordinal);

        internal override void DeployFile(string sourcePath, string destPath, string relPath)
        {
            if (++_calls == _failOnCall) throw _failure;

            File.Copy(sourcePath, destPath, overwrite: true);
            _stuck.Add(destPath);
        }

        /// <summary>The file the OS will not let go of, which is what rollback tries next.</summary>
        internal override void RemoveDeployed(string path)
        {
            if (_stuck.Contains(path)) throw new IOException("errno 16 (EBUSY)");
            base.RemoveDeployed(path);
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

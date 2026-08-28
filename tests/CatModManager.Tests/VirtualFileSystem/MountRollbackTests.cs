using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatModManager.VirtualFileSystem;
using Xunit;

namespace CatModManager.Tests.VirtualFileSystem;

/// <summary>
/// A mount that fails partway must leave the game folder exactly as it found it.
///
/// This is the sharp edge of the whole driver. Deploying a mod renames the real game file to a
/// hidden dot-backup, and the record of that rename is written to the state store only after the
/// entire walk succeeds. So a failure mid-walk used to leave the user's actual game files renamed
/// out of the way with nothing — not unmount, not crash recovery on the next launch — able to find
/// them again. The game looks intact except for the files that silently are not there.
/// </summary>
public class MountRollbackTests : IDisposable
{
    private readonly string _root    = Path.Combine(Path.GetTempPath(), "CMM_RB_" + Guid.NewGuid().ToString("N"));
    private readonly string _gameDir;
    private readonly string _modDir;

    public MountRollbackTests()
    {
        _gameDir = Path.Combine(_root, "Game");
        _modDir  = Path.Combine(_root, "Mod");
        Directory.CreateDirectory(_gameDir);
        Directory.CreateDirectory(_modDir);
    }

    /// <summary>Sets up N files present in both the game folder and the mod, so every one gets backed up.</summary>
    private FlatFs Overriding(params string[] names)
    {
        foreach (var n in names)
        {
            File.WriteAllText(Path.Combine(_gameDir, n), "REAL GAME: " + n);
            File.WriteAllText(Path.Combine(_modDir,  n), "mod: " + n);
        }
        return new FlatFs(names.ToDictionary(n => n, n => Path.Combine(_modDir, n)));
    }

    private void AssertGameFolderUntouched(params string[] names)
    {
        foreach (var n in names)
        {
            var path = Path.Combine(_gameDir, n);
            Assert.True(File.Exists(path), $"'{n}' never came back — the real game file is lost.");
            Assert.Equal("REAL GAME: " + n, File.ReadAllText(path));
        }

        var strays = Directory.GetFiles(_gameDir).Select(Path.GetFileName)
                              .Where(f => f!.StartsWith('.')).ToArray();
        Assert.True(strays.Length == 0, "Orphaned backups left behind: " + string.Join(", ", strays));
    }

    /// <summary>
    /// The failure that motivated this: several files swapped, then one throws. Everything already
    /// swapped has to come back, and the exception is not <c>UnauthorizedAccessException</c> — which
    /// is the only one the driver used to roll back on.
    /// </summary>
    [Fact]
    public void AFailedDeployRestoresEveryFileAlreadySwapped()
    {
        var fs = Overriding("a.esp", "b.esp", "c.esp", "d.esp");
        var driver = new FailingDeployDriver(new MemoryStore(), failOnCall: 3, new IOException("link() failed: errno 5"));

        Assert.Throws<IOException>(() => driver.Mount(_gameDir, fs));

        AssertGameFolderUntouched("a.esp", "b.esp", "c.esp", "d.esp");
    }

    /// <summary>
    /// The narrowest window in the driver: the game file has been renamed aside and the link has not
    /// been made yet. The entry used to be appended only after a successful deploy, so a failure
    /// here produced a backup that was in no list and therefore restorable by nothing.
    /// </summary>
    [Fact]
    public void AFailureBetweenTheBackupAndTheLinkDoesNotStrandTheGameFile()
    {
        var fs = Overriding("Starfield.exe");
        var driver = new FailingDeployDriver(new MemoryStore(), failOnCall: 1, new IOException("link() failed: errno 28"));

        Assert.Throws<IOException>(() => driver.Mount(_gameDir, fs));

        AssertGameFolderUntouched("Starfield.exe");
    }

    /// <summary>An unwritable state store already rolled back; this keeps that true and keeps the advice.</summary>
    [Fact]
    public void AnUnwritableStateStoreRollsBackAndExplainsItself()
    {
        var fs = Overriding("a.esp", "b.esp");
        var driver = new HardlinkDriver(new ThrowingStore(new UnauthorizedAccessException("denied")));

        var ex = Assert.Throws<IOException>(() => driver.Mount(_gameDir, fs));

        Assert.Contains("crash-recovery state", ex.Message);
        Assert.IsType<UnauthorizedAccessException>(ex.InnerException);
        AssertGameFolderUntouched("a.esp", "b.esp");
    }

    /// <summary>
    /// A store that fails for some other reason is just as fatal to the mount, and used to escape
    /// without rolling back anything at all.
    /// </summary>
    [Fact]
    public void AStateStoreFailingForAnyOtherReasonAlsoRollsBack()
    {
        var fs = Overriding("a.esp", "b.esp");
        var driver = new HardlinkDriver(new ThrowingStore(new InvalidOperationException("database is locked")));

        Assert.Throws<InvalidOperationException>(() => driver.Mount(_gameDir, fs));

        AssertGameFolderUntouched("a.esp", "b.esp");
    }

    /// <summary>
    /// Rolling back must not disguise why the mount failed. The original exception is what the log
    /// and the diagnosis are attached to.
    /// </summary>
    [Fact]
    public void TheOriginalFailureSurvivesTheRollback()
    {
        var fs = Overriding("a.esp", "b.esp");
        var driver = new FailingDeployDriver(new MemoryStore(), failOnCall: 2, new IOException("errno 5 on the real device"));

        var ex = Assert.Throws<IOException>(() => driver.Mount(_gameDir, fs));

        Assert.Contains("errno 5 on the real device", ex.Message);
    }

    /// <summary>A mount that threw is not a mount. Reporting otherwise makes the next unmount a no-op.</summary>
    [Fact]
    public void AFailedMountDoesNotClaimToBeMounted()
    {
        var fs = Overriding("a.esp");
        var driver = new FailingDeployDriver(new MemoryStore(), failOnCall: 1, new IOException("nope"));

        Assert.Throws<IOException>(() => driver.Mount(_gameDir, fs));

        Assert.False(driver.IsMounted);
    }

    /// <summary>
    /// Mods that add new files rather than replacing them have no backup. Rollback still has to
    /// remove what it deployed, or a failed mount leaves half a mod installed.
    /// </summary>
    [Fact]
    public void FilesAddedWithNoBackupAreRemovedAgain()
    {
        File.WriteAllText(Path.Combine(_modDir, "new1.esp"), "brand new");
        File.WriteAllText(Path.Combine(_modDir, "new2.esp"), "brand new");
        var fs = new FlatFs(new Dictionary<string, string>
        {
            ["new1.esp"] = Path.Combine(_modDir, "new1.esp"),
            ["new2.esp"] = Path.Combine(_modDir, "new2.esp"),
        });

        var driver = new FailingDeployDriver(new MemoryStore(), failOnCall: 2, new IOException("nope"));
        Assert.Throws<IOException>(() => driver.Mount(_gameDir, fs));

        Assert.Empty(Directory.GetFiles(_gameDir));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    // ── doubles ──────────────────────────────────────────────────────────────

    /// <summary>Deploys for real until the chosen call, then throws — a mount failing partway through.</summary>
    private class FailingDeployDriver : HardlinkDriver
    {
        private readonly int       _failOnCall;
        private readonly Exception _failure;
        private int _calls;

        public FailingDeployDriver(IHardlinkStateStore store, int failOnCall, Exception failure) : base(store)
            => (_failOnCall, _failure) = (failOnCall, failure);

        internal override void DeployFile(string sourcePath, string destPath, string relPath)
        {
            if (++_calls == _failOnCall) throw _failure;
            File.Copy(sourcePath, destPath, overwrite: true);
        }
    }

    private class MemoryStore : IHardlinkStateStore
    {
        private readonly List<HardlinkStateEntry> _entries = new();
        public void Save(string mountPoint, IReadOnlyList<HardlinkStateEntry> entries) => _entries.AddRange(entries);
        public IReadOnlyList<HardlinkStateEntry> Load(string? mountPoint) => _entries;
        public void Clear(string? mountPoint) => _entries.Clear();
    }

    private class ThrowingStore : IHardlinkStateStore
    {
        private readonly Exception _failure;
        public ThrowingStore(Exception failure) => _failure = failure;
        public void Save(string mountPoint, IReadOnlyList<HardlinkStateEntry> entries) => throw _failure;
        public IReadOnlyList<HardlinkStateEntry> Load(string? mountPoint) => Array.Empty<HardlinkStateEntry>();
        public void Clear(string? mountPoint) { }
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
            => string.IsNullOrEmpty(path.Trim(Path.DirectorySeparatorChar)) ? _files.Keys : Array.Empty<string>();

        public Stream? OpenFile(string path)
            => _files.TryGetValue(path.Trim(Path.DirectorySeparatorChar), out var p) ? File.OpenRead(p) : null;

        public string? GetPhysicalPath(string path)
            => _files.TryGetValue(path.Trim(Path.DirectorySeparatorChar), out var p) ? p : null;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Vfs;
using CatModManager.PluginSdk;
using CatModManager.VirtualFileSystem;
using Xunit;

namespace CatModManager.Tests.VirtualFileSystem;

/// <summary>
/// The whole stack — resolver, virtual filesystem and driver — against the situation a user
/// actually ends up in: mod hard links left in the game folder by a session that never unmounted,
/// with the state store empty. A mount followed by an unmount has to leave the game folder clean.
///
/// The driver's own tests drive it with a hand-built file map. This one goes through
/// SimpleConflictResolver, which also scans the game folder itself — and that scan is what decides
/// whether the leftover is even seen as a mod file.
/// </summary>
public class LeftoverLinksEndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CMM_E2E_" + Guid.NewGuid().ToString("N"));
    private readonly string _dataDir;
    private readonly string _modDir;

    public LeftoverLinksEndToEndTests()
    {
        _dataDir = Path.Combine(_root, "Game", "Data");
        _modDir  = Path.Combine(_root, "Game", "cmm", "mods", "ModA");
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(Path.Combine(_modDir, "SFSE", "Plugins"));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void AMountFollowedByAnUnmountRemovesLinksLeftBehindByAnEarlierSession()
    {
        if (!OperatingSystem.IsWindows()) return;   // hard links via CreateHardLinkW

        var modFile = Path.Combine(_modDir, "SFSE", "Plugins", "sf360.dll");
        File.WriteAllText(modFile, "mod payload");
        File.WriteAllText(Path.Combine(_dataDir, "Starfield.esm"), "REAL GAME");

        // The leftover: a session deployed this and died before unmounting, so nothing recorded it.
        var leftover = Path.Combine(_dataDir, "SFSE", "Plugins", "sf360.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(leftover)!);
        Assert.True(CreateHardLinkW(leftover, modFile, IntPtr.Zero), "test setup: could not create the leftover link");

        var store = new MemoryStore();
        var vfs = new CatVirtualFileSystem(
            new SimpleConflictResolver(new SilentLog(), new SevenZipArchiveExtractor()),
            new HardlinkDriver(store));

        var mods = new List<Mod> { new() { Name = "ModA", ModRootPath = _modDir, IsEnabled = true, Priority = 0 } };

        vfs.Mount(_dataDir, mods);
        vfs.Unmount();

        Assert.False(File.Exists(leftover),
            "The leftover link survived a full mount/unmount cycle — it is now permanent.");
        Assert.Equal("REAL GAME", File.ReadAllText(Path.Combine(_dataDir, "Starfield.esm")));
    }

    /// <summary>
    /// The failure the user saw first: with the leftover in place, the mount itself throws
    /// "Could not set aside the existing file", because the resolver is holding the very file open.
    /// </summary>
    [Fact]
    public void MountingOverALeftoverDoesNotFail()
    {
        if (!OperatingSystem.IsWindows()) return;

        var modFile = Path.Combine(_modDir, "SFSE", "Plugins", "sf360.dll");
        File.WriteAllText(modFile, "mod payload");

        var leftover = Path.Combine(_dataDir, "SFSE", "Plugins", "sf360.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(leftover)!);
        CreateHardLinkW(leftover, modFile, IntPtr.Zero);

        var vfs = new CatVirtualFileSystem(
            new SimpleConflictResolver(new SilentLog(), new SevenZipArchiveExtractor()),
            new HardlinkDriver(new MemoryStore()));

        var mods = new List<Mod> { new() { Name = "ModA", ModRootPath = _modDir, IsEnabled = true, Priority = 0 } };

        vfs.Mount(_dataDir, mods);   // must not throw
        vfs.Unmount();
    }

    /// <summary>
    /// A real game file the mod overrides is still displaced and put back — the file the resolver
    /// pins open is exactly the file the driver has to rename, and a pinned handle that does not
    /// allow deletion makes that rename fail.
    /// </summary>
    [Fact]
    public void AGameFileTheModOverridesIsDisplacedAndRestored()
    {
        var modFile = Path.Combine(_modDir, "Starfield.esm");
        File.WriteAllText(modFile, "mod version");
        var gameFile = Path.Combine(_dataDir, "Starfield.esm");
        File.WriteAllText(gameFile, "REAL GAME");

        var vfs = new CatVirtualFileSystem(
            new SimpleConflictResolver(new SilentLog(), new SevenZipArchiveExtractor()),
            new HardlinkDriver(new MemoryStore()));

        var mods = new List<Mod> { new() { Name = "ModA", ModRootPath = _modDir, IsEnabled = true, Priority = 0 } };

        vfs.Mount(_dataDir, mods);
        Assert.Equal("mod version", File.ReadAllText(gameFile));

        vfs.Unmount();
        Assert.Equal("REAL GAME", File.ReadAllText(gameFile));
    }

    private class MemoryStore : IHardlinkStateStore
    {
        private readonly List<HardlinkStateEntry> _entries = new();
        public void Save(string mountPoint, IReadOnlyList<HardlinkStateEntry> entries) => _entries.AddRange(entries);
        public IReadOnlyList<HardlinkStateEntry> Load(string? mountPoint) => _entries;
        public void Clear(string? mountPoint) => _entries.Clear();
    }

    private class SilentLog : ILogService
    {
        public event Action<string>? OnLog;
        public void Log(string m) => OnLog?.Invoke(m);
        public void LogError(string m, Exception? e) => OnLog?.Invoke(m);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}

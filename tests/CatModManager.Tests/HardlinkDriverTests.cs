using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;
using CatModManager.VirtualFileSystem.Windows;
using CatModManager.VirtualFileSystem;
using CatModManager.Core.Services;

namespace CatModManager.Tests;

/// <summary>
/// xUnit fact that skips automatically on non-Windows platforms.
/// HardlinkDriver uses CreateHardLinkW (kernel32) which is Windows-only.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Skip = "Windows-only (CreateHardLinkW)";
    }
}

public class HardlinkDriverTests : IDisposable
{
    private readonly string              _root;
    private readonly string              _gameDir;
    private readonly string              _modDir;
    private readonly IHardlinkStateStore _store;

    public HardlinkDriverTests()
    {
        _root    = Path.Combine(Path.GetTempPath(), "CMM_HL_" + Guid.NewGuid().ToString("N"));
        _gameDir = Path.Combine(_root, "Game");
        _modDir  = Path.Combine(_root, "Mods", "Mod1");
        Directory.CreateDirectory(_gameDir);
        Directory.CreateDirectory(_modDir);

        var db = new AppDatabase(new MockCatPathService(Path.Combine(_root, "AppData")));
        _store = new SqliteHardlinkStateStore(db);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private HardlinkDriver NewDriver() => new(_store);

    private static IFileSystem SingleFileFs(string relPath, string physPath)
        => new StubFileSystem(relPath, physPath);

    // ── tests ─────────────────────────────────────────────────────────────────

    [WindowsFact]
    public void Mount_CreatesHardLink_InGameDir()
    {
        var sourceFile = Path.Combine(_modDir, "pak.pak");
        File.WriteAllText(sourceFile, "mod content");

        var driver = NewDriver();
        driver.Mount(_gameDir, SingleFileFs("pak.pak", sourceFile));

        Assert.True(driver.IsMounted);
        var destFile = Path.Combine(_gameDir, "pak.pak");
        Assert.True(File.Exists(destFile), "Hard link should exist in game dir");
        Assert.Equal("mod content", File.ReadAllText(destFile));
        driver.Unmount();
    }

    [WindowsFact]
    public void Mount_BacksUpExistingFile_WithDotPrefix()
    {
        var gameFile = Path.Combine(_gameDir, "pak.pak");
        File.WriteAllText(gameFile, "original content");

        var sourceFile = Path.Combine(_modDir, "pak.pak");
        File.WriteAllText(sourceFile, "mod content");

        var driver = NewDriver();
        driver.Mount(_gameDir, SingleFileFs("pak.pak", sourceFile));

        var backup = Path.Combine(_gameDir, ".pak.pak");
        Assert.True(File.Exists(backup), "Backup should be dot-prefixed");
        Assert.Equal("original content", File.ReadAllText(backup));
        Assert.Equal("mod content", File.ReadAllText(gameFile));

        driver.Unmount();
    }

    [WindowsFact]
    public void Unmount_RemovesLink_AndRestoresBackup()
    {
        var gameFile   = Path.Combine(_gameDir, "pak.pak");
        File.WriteAllText(gameFile, "original");

        var sourceFile = Path.Combine(_modDir, "pak.pak");
        File.WriteAllText(sourceFile, "mod");

        var driver = NewDriver();
        driver.Mount(_gameDir, SingleFileFs("pak.pak", sourceFile));
        driver.Unmount();

        Assert.False(driver.IsMounted);
        Assert.True(File.Exists(gameFile));
        Assert.Equal("original", File.ReadAllText(gameFile));
        Assert.False(File.Exists(Path.Combine(_gameDir, ".pak.pak")));
        // No .cmm_hl.json should exist — state is in DB now
        Assert.False(File.Exists(Path.Combine(_gameDir, ".cmm_hl.json")));
    }

    [WindowsFact]
    public void Unmount_WithoutPriorMount_IsNoop()
    {
        var driver = NewDriver();
        driver.Unmount();
        Assert.False(driver.IsMounted);
    }

    [WindowsFact]
    public void Mount_IsIdempotent_WhenAlreadyMounted()
    {
        var sourceFile = Path.Combine(_modDir, "pak.pak");
        File.WriteAllText(sourceFile, "mod");

        var driver = NewDriver();
        driver.Mount(_gameDir, SingleFileFs("pak.pak", sourceFile));

        var sourceFile2 = Path.Combine(_modDir, "other.pak");
        File.WriteAllText(sourceFile2, "other");
        driver.Mount(_gameDir, SingleFileFs("other.pak", sourceFile2));

        Assert.True(File.Exists(Path.Combine(_gameDir, "pak.pak")));
        Assert.False(File.Exists(Path.Combine(_gameDir, "other.pak")));
        driver.Unmount();
    }

    [WindowsFact]
    public void CrashRecovery_NewInstance_CleansUpStaleLinks()
    {
        var gameFile   = Path.Combine(_gameDir, "pak.pak");
        File.WriteAllText(gameFile, "original");

        var sourceFile = Path.Combine(_modDir, "pak.pak");
        File.WriteAllText(sourceFile, "mod");

        // driverA mounts but crashes (no Unmount called)
        var driverA = NewDriver();
        driverA.Mount(_gameDir, SingleFileFs("pak.pak", sourceFile));
        Assert.True(driverA.IsMounted);

        // driverB is a fresh instance on app restart — same DB, no in-memory state
        var driverB = NewDriver();
        Assert.False(driverB.IsMounted);

        // Crash recovery: Unmount() on fresh instance loads all stale DB entries
        driverB.Unmount();

        // Original file must be restored
        Assert.True(File.Exists(gameFile));
        Assert.Equal("original", File.ReadAllText(gameFile));
        // Backup removed
        Assert.False(File.Exists(Path.Combine(_gameDir, ".pak.pak")));
    }

    [WindowsFact]
    public void Mount_SubDirectory_CreatesLinkInSubDir()
    {
        Directory.CreateDirectory(Path.Combine(_modDir, "Data"));
        var sourceFile = Path.Combine(_modDir, "Data", "mesh.bin");
        File.WriteAllText(sourceFile, "mesh data");

        var driver = NewDriver();
        driver.Mount(_gameDir, SingleFileFs(@"Data\mesh.bin", sourceFile));

        var destFile = Path.Combine(_gameDir, "Data", "mesh.bin");
        Assert.True(File.Exists(destFile));
        Assert.Equal("mesh data", File.ReadAllText(destFile));

        driver.Unmount();
        Assert.False(File.Exists(destFile));
    }

    [WindowsFact]
    public void Mount_CrossVolumeFallback_CopiesFileWhenHardLinkFails()
    {
        // Simulate a cross-volume scenario by using a driver subclass that always
        // reports ERROR_NOT_SAME_DEVICE from CreateHardLinkW, forcing the copy path.
        var sourceFile = Path.Combine(_modDir, "pak.pak");
        File.WriteAllText(sourceFile, "mod content");

        var driver = new CrossVolumeSimulatingDriver(_store);
        driver.Mount(_gameDir, SingleFileFs("pak.pak", sourceFile));

        var destFile = Path.Combine(_gameDir, "pak.pak");
        Assert.True(File.Exists(destFile), "File should be deployed via copy");
        Assert.Equal("mod content", File.ReadAllText(destFile));

        driver.Unmount();
        Assert.False(File.Exists(destFile), "Copied file should be removed on unmount");
    }

    [WindowsFact]
    public void Mount_CrossVolumeFallback_RestoresBackupOnUnmount()
    {
        var gameFile = Path.Combine(_gameDir, "pak.pak");
        File.WriteAllText(gameFile, "original");

        var sourceFile = Path.Combine(_modDir, "pak.pak");
        File.WriteAllText(sourceFile, "mod content");

        var driver = new CrossVolumeSimulatingDriver(_store);
        driver.Mount(_gameDir, SingleFileFs("pak.pak", sourceFile));
        driver.Unmount();

        Assert.True(File.Exists(gameFile));
        Assert.Equal("original", File.ReadAllText(gameFile));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    // ── mocks ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Simulates a cross-volume mount by always choosing the File.Copy path
    /// (overrides DeployFile to skip CreateHardLinkW entirely).
    /// </summary>
    private class CrossVolumeSimulatingDriver : HardlinkDriver
    {
        public CrossVolumeSimulatingDriver(IHardlinkStateStore store) : base(store) { }

        internal override void DeployFile(string sourcePath, string destPath, string relPath)
            => File.Copy(sourcePath, destPath, overwrite: true);
    }

    private class MockCatPathService : ICatPathService
    {
        public string BaseDataPath { get; }
        public string ProfilesPath     => Path.Combine(BaseDataPath, "profiles");
        public string GameSupportsPath => Path.Combine(BaseDataPath, "game_definitions");
        public string ActiveMountsFile => Path.Combine(BaseDataPath, "active_mounts.toml");
        public string DownloadsPath    => Path.Combine(BaseDataPath, "downloads");
        public MockCatPathService(string path) => BaseDataPath = path;
        public string GetProfilePath(string n) => Path.Combine(ProfilesPath, n + ".toml");
    }

    // ── stub ─────────────────────────────────────────────────────────────────

    private class StubFileSystem : IFileSystem
    {
        private readonly string _relPath;
        private readonly string _physPath;

        public StubFileSystem(string relPath, string physPath)
        {
            _relPath  = relPath.Replace('/', Path.DirectorySeparatorChar);
            _physPath = physPath;
        }

        public FileSystemNodeInfo? GetInfo(string path)
        {
            var normalized = path.Replace('/', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar);
            if (normalized == _relPath)
                return new FileSystemNodeInfo { IsDirectory = false };

            if (_relPath.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return new FileSystemNodeInfo { IsDirectory = true };

            return null;
        }

        public IEnumerable<string> ReadDirectory(string path)
        {
            var normalized = path.Replace('/', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar);
            var parts      = _relPath.Split(Path.DirectorySeparatorChar);

            if (string.IsNullOrEmpty(normalized))
                return new[] { parts[0] };

            if (_relPath.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var rest = _relPath[(normalized.Length + 1)..];
                return new[] { rest.Split(Path.DirectorySeparatorChar)[0] };
            }

            return Array.Empty<string>();
        }

        public Stream? OpenFile(string path) =>
            path.Replace('/', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar)
                == _relPath ? File.OpenRead(_physPath) : null;

        public string? GetPhysicalPath(string path) =>
            path.Replace('/', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar)
                == _relPath ? _physPath : null;
    }
}

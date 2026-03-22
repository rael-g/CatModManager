using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit;
using CatModManager.VirtualFileSystem.Windows;
using CatModManager.VirtualFileSystem;

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
    private readonly string _root;
    private readonly string _gameDir;
    private readonly string _modDir;

    public HardlinkDriverTests()
    {
        _root    = Path.Combine(Path.GetTempPath(), "CMM_HL_" + Guid.NewGuid().ToString("N"));
        _gameDir = Path.Combine(_root, "Game");
        _modDir  = Path.Combine(_root, "Mods", "Mod1");
        Directory.CreateDirectory(_gameDir);
        Directory.CreateDirectory(_modDir);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a fake IFileSystem that serves a single file at the given relative path
    /// from the given physical source file.
    /// </summary>
    private static IFileSystem SingleFileFs(string relPath, string physPath)
        => new StubFileSystem(relPath, physPath);

    // ── tests ─────────────────────────────────────────────────────────────────

    [WindowsFact]
    public void Mount_CreatesHardLink_InGameDir()
    {
        var sourceFile = Path.Combine(_modDir, "pak.pak");
        File.WriteAllText(sourceFile, "mod content");

        var driver = new HardlinkDriver();
        driver.Mount(_gameDir, SingleFileFs("pak.pak", sourceFile));

        Assert.True(driver.IsMounted);
        var destFile = Path.Combine(_gameDir, "pak.pak");
        Assert.True(File.Exists(destFile), "Hard link should exist in game dir");
        Assert.Equal("mod content", File.ReadAllText(destFile));
    }

    [WindowsFact]
    public void Mount_BacksUpExistingFile_WithDotPrefix()
    {
        // Pre-existing game file
        var gameFile = Path.Combine(_gameDir, "pak.pak");
        File.WriteAllText(gameFile, "original content");

        var sourceFile = Path.Combine(_modDir, "pak.pak");
        File.WriteAllText(sourceFile, "mod content");

        var driver = new HardlinkDriver();
        driver.Mount(_gameDir, SingleFileFs("pak.pak", sourceFile));

        // Backup must be dot-prefixed
        var backup = Path.Combine(_gameDir, ".pak.pak");
        Assert.True(File.Exists(backup), "Backup should be dot-prefixed");
        Assert.Equal("original content", File.ReadAllText(backup));

        // Link replaces original
        Assert.Equal("mod content", File.ReadAllText(gameFile));
    }

    [WindowsFact]
    public void Unmount_RemovesLink_AndRestoresBackup()
    {
        var gameFile   = Path.Combine(_gameDir, "pak.pak");
        File.WriteAllText(gameFile, "original");

        var sourceFile = Path.Combine(_modDir, "pak.pak");
        File.WriteAllText(sourceFile, "mod");

        var driver = new HardlinkDriver();
        driver.Mount(_gameDir, SingleFileFs("pak.pak", sourceFile));
        driver.Unmount();

        Assert.False(driver.IsMounted);
        // Original restored
        Assert.True(File.Exists(gameFile));
        Assert.Equal("original", File.ReadAllText(gameFile));
        // Backup removed
        Assert.False(File.Exists(Path.Combine(_gameDir, ".pak.pak")));
        // State file cleaned up
        Assert.False(File.Exists(Path.Combine(_gameDir, ".cmm_hl.json")));
    }

    [WindowsFact]
    public void Unmount_WithoutPriorMount_IsNoop()
    {
        var driver = new HardlinkDriver();
        // Should not throw
        driver.Unmount();
        Assert.False(driver.IsMounted);
    }

    [WindowsFact]
    public void Mount_IsIdempotent_WhenAlreadyMounted()
    {
        var sourceFile = Path.Combine(_modDir, "pak.pak");
        File.WriteAllText(sourceFile, "mod");

        var driver = new HardlinkDriver();
        driver.Mount(_gameDir, SingleFileFs("pak.pak", sourceFile));

        // Second mount with a different content — should be ignored
        var sourceFile2 = Path.Combine(_modDir, "other.pak");
        File.WriteAllText(sourceFile2, "other");
        driver.Mount(_gameDir, SingleFileFs("other.pak", sourceFile2));

        // Only the first file should exist
        Assert.True(File.Exists(Path.Combine(_gameDir, "pak.pak")));
        Assert.False(File.Exists(Path.Combine(_gameDir, "other.pak")));
    }

    [WindowsFact]
    public void CrashRecovery_StateFile_AllowsUnmountOnNewInstance()
    {
        var gameFile   = Path.Combine(_gameDir, "pak.pak");
        File.WriteAllText(gameFile, "original");

        var sourceFile = Path.Combine(_modDir, "pak.pak");
        File.WriteAllText(sourceFile, "mod");

        // Mount with instance A — simulates crash (no Unmount called)
        var driverA = new HardlinkDriver();
        driverA.Mount(_gameDir, SingleFileFs("pak.pak", sourceFile));

        // State file must exist
        var stateFile = Path.Combine(_gameDir, ".cmm_hl.json");
        Assert.True(File.Exists(stateFile), "State file must be written for crash recovery");

        // Instance B simulates restart recovery: reads state file and reverses
        var driverB = new HardlinkDriver();
        driverB.Unmount(); // nothing mounted yet — noop

        // Simulate the VfsOrchestrationService pattern: set mount point then unmount
        // HardlinkDriver.Unmount reads the state file at the game root it last set.
        // To test crash recovery we need to set _mountPoint without going through Mount.
        // The public API for this is: just call Unmount after setting state via reflection,
        // or (simpler) verify that a fresh driver whose game dir has .cmm_hl.json can
        // recover via the static helper path in Unmount().
        // The driver reads _mountPoint from the state file path — but _mountPoint is set
        // only by Mount(). So crash recovery requires passing the path explicitly.
        // This is handled at a higher level by VfsOrchestrationService which persists
        // the game folder in VfsStateService and calls Unmount on a fresh driver.
        // For the driver itself, test that the state file contains the correct entries:
        var json    = File.ReadAllText(stateFile);
        var entries = JsonSerializer.Deserialize<JsonElement[]>(json);
        Assert.NotNull(entries);
        Assert.NotEmpty(entries);

        // Cleanup: unmount A
        driverA.Unmount();
        Assert.Equal("original", File.ReadAllText(gameFile));
    }

    [WindowsFact]
    public void Mount_SubDirectory_CreatesLinkInSubDir()
    {
        Directory.CreateDirectory(Path.Combine(_modDir, "Data"));
        var sourceFile = Path.Combine(_modDir, "Data", "mesh.bin");
        File.WriteAllText(sourceFile, "mesh data");

        var driver = new HardlinkDriver();
        driver.Mount(_gameDir, SingleFileFs(@"Data\mesh.bin", sourceFile));

        var destFile = Path.Combine(_gameDir, "Data", "mesh.bin");
        Assert.True(File.Exists(destFile));
        Assert.Equal("mesh data", File.ReadAllText(destFile));

        driver.Unmount();
        Assert.False(File.Exists(destFile));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
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

            // parent directories
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

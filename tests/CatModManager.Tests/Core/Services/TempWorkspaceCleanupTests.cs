using System;
using System.IO;
using System.Threading;
using CatModManager.Core.Services;
using Xunit;

namespace CatModManager.Tests.Core.Services;

/// <summary>
/// Dispose covers the normal path, cancellation included, but nothing runs when the process is
/// killed — a crash, an OOM kill, a close mid-extraction. What is left is a half-extracted mod
/// worth hundreds of megabytes, hidden in the mods folder behind a leading dot.
/// </summary>
public class TempWorkspaceCleanupTests : IDisposable
{
    private readonly string _baseDir;

    public TempWorkspaceCleanupTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "CMM_TempSweep_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true); } catch { }
    }

    [Fact]
    public void RemovesWorkspacesLeftByAPreviousRun()
    {
        string orphan = Path.Combine(_baseDir, ".cmm_tmp_deadbeef");
        Directory.CreateDirectory(orphan);
        File.WriteAllText(Path.Combine(orphan, "half-extracted.bin"), "junk");
        // Predate this process, which is what marks it as another run's leftover.
        Directory.SetCreationTimeUtc(orphan, DateTime.UtcNow.AddDays(-1));

        TempWorkspace.CleanupStale(_baseDir);

        Assert.False(Directory.Exists(orphan));
    }

    [Fact]
    public void LeavesAWorkspaceOfTheRunningProcessAlone()
    {
        // An install happening right now must not have its extraction folder pulled out from under
        // it — the sweep runs on profile load, which can happen while one is in flight.
        using var live = new TempWorkspace(_baseDir);

        TempWorkspace.CleanupStale(_baseDir);

        Assert.True(Directory.Exists(live.Path));
    }

    [Fact]
    public void LeavesUnrelatedFoldersAlone()
    {
        string mod = Path.Combine(_baseDir, "A Real Mod");
        Directory.CreateDirectory(mod);
        Directory.SetCreationTimeUtc(mod, DateTime.UtcNow.AddDays(-1));

        TempWorkspace.CleanupStale(_baseDir);

        Assert.True(Directory.Exists(mod));
    }

    [Fact]
    public void DoesNothingWhenTheFolderIsMissingOrUnset()
    {
        TempWorkspace.CleanupStale(Path.Combine(_baseDir, "nope"));
        TempWorkspace.CleanupStale("");
    }
}

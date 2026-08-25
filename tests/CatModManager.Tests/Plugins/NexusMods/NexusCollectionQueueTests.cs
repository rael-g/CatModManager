using System;
using System.Collections.Generic;
using CmmPlugin.NexusMods;
using CatModManager.PluginSdk;
using Xunit;

namespace CatModManager.Tests.Plugins.NexusMods;

/// <summary>
/// The collection queue is a state machine with one hard invariant: exactly one Nexus page open at
/// a time, advancing only when the current mod is claimed by its nxm:// callback. Getting this
/// wrong either stalls the collection or spams the browser with every mod at once.
/// </summary>
public class NexusCollectionQueueTests
{
    private static NexusCollectionQueue Build(List<string> opened) =>
        new(new NullLogger(), opened.Add);

    private static PendingCollectionMod Mod(int modId, int fileId = 1) =>
        new(modId, fileId, "skyrimspecialedition",
            new DownloadEntry { ModName = $"Mod {modId}", ModId = modId, FileId = fileId });

    [Fact]
    public void Enqueue_OpensOnlyTheFirstPage()
    {
        var opened = new List<string>();
        Build(opened).Enqueue(new[] { Mod(1), Mod(2), Mod(3) });

        Assert.Single(opened);
        Assert.Contains("/mods/1?", opened[0]);
    }

    [Fact]
    public void TryClaim_AdvancesToTheNextMod()
    {
        var opened = new List<string>();
        var queue = Build(opened);
        queue.Enqueue(new[] { Mod(1), Mod(2) });

        Assert.NotNull(queue.TryClaim(1, 1));
        queue.OpenNext();

        Assert.Equal(2, opened.Count);
        Assert.Contains("/mods/2?", opened[1]);
    }

    [Fact]
    public void TryClaim_IgnoresUnrelatedDownloads()
    {
        var opened = new List<string>();
        var queue = Build(opened);
        queue.Enqueue(new[] { Mod(1), Mod(2) });

        // An ordinary (non-collection) nxm:// arriving while a page is open must not consume the slot.
        Assert.Null(queue.TryClaim(99, 1));
        queue.OpenNext();

        Assert.Single(opened);
    }

    [Fact]
    public void Pause_StopsAdvancingAndResumeContinues()
    {
        var opened = new List<string>();
        var queue = Build(opened);
        queue.Enqueue(new[] { Mod(1), Mod(2) });

        queue.Pause();
        queue.TryClaim(1, 1);
        queue.OpenNext();
        Assert.Single(opened);

        queue.Resume();
        Assert.Equal(2, opened.Count);
    }

    [Fact]
    public void CancelAll_ClearsTheQueueAndReportsZero()
    {
        var opened = new List<string>();
        var queue = Build(opened);

        int lastCount = -1;
        queue.CountChanged += c => lastCount = c;
        queue.Enqueue(new[] { Mod(1), Mod(2), Mod(3) });

        queue.CancelAll();
        Assert.Equal(0, lastCount);

        // Nothing left to open, so a later claim/advance is a no-op rather than reopening page 1.
        queue.OpenNext();
        Assert.Single(opened);
    }

    [Fact]
    public void OpenNext_SkipsEntriesCancelledWhileWaiting()
    {
        var opened = new List<string>();
        var queue = Build(opened);
        var second = Mod(2);
        queue.Enqueue(new[] { Mod(1), second, Mod(3) });

        second.Entry.HasFailed = true;   // user hit cancel on the waiting row
        queue.TryClaim(1, 1);
        queue.OpenNext();

        Assert.Equal(2, opened.Count);
        Assert.Contains("/mods/3?", opened[1]);
    }

    [Fact]
    public void CountChanged_CountsTheOpenPageAsStillPending()
    {
        var opened = new List<string>();
        var queue = Build(opened);

        int lastCount = -1;
        queue.CountChanged += c => lastCount = c;
        queue.Enqueue(new[] { Mod(1), Mod(2) });

        // One page open + one waiting: the user still has two mods to get through.
        Assert.Equal(2, lastCount);
    }

    private sealed class NullLogger : IPluginLogger
    {
        public void Log(string message) { }
        public void LogError(string message, Exception? ex) { }
    }
}

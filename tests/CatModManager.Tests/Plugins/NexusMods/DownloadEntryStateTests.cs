using CmmPlugin.NexusMods;
using Xunit;

namespace CatModManager.Tests.Plugins.NexusMods;

/// <summary>
/// Whether an entry counts as "in flight" decides whether a fresh nxm:// for the same mod+file is
/// dropped as a duplicate or allowed to start. The guard used to ask "has it not failed?", which a
/// finished download answers the same way as a running one — so re-downloading anything already in
/// the list silently did nothing until the row was cleared by hand.
/// </summary>
public class DownloadEntryStateTests
{
    [Fact]
    public void QueuedEntry_IsInFlight_EvenBeforeItStartsTransferring()
    {
        // It sits at "Queued" while waiting on the concurrency semaphore, with IsActive still false.
        // Treating that window as settled would let a duplicate nxm:// start a second transfer.
        var entry = new DownloadEntry { Status = DownloadEntry.QueuedStatus, IsActive = false };

        Assert.True(entry.IsInFlight);
    }

    [Fact]
    public void ActiveEntry_IsInFlight()
    {
        Assert.True(new DownloadEntry { Status = "Downloading foo.7z...", IsActive = true }.IsInFlight);
    }

    [Fact]
    public void CompletedEntry_IsNotInFlight_SoItCanBeDownloadedAgain()
    {
        // The case that bit: the archive was deleted from disk but the row stayed. HasFailed is
        // false on a completed entry and nothing ever clears it, so the old guard blocked forever.
        var entry = new DownloadEntry { Status = "Done", IsActive = false, HasFailed = false };

        Assert.False(entry.IsInFlight);
    }

    [Fact]
    public void FailedAndCancelledEntries_AreNotInFlight()
    {
        Assert.False(new DownloadEntry { Status = "Failed: boom", HasFailed = true }.IsInFlight);
        Assert.False(new DownloadEntry { Status = "Cancelled" }.IsInFlight);
    }
}

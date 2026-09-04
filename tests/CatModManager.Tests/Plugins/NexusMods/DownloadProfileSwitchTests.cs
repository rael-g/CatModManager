using System;
using System.IO;
using CatModManager.PluginSdk;
using CmmPlugin.NexusMods;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CatModManager.Tests.Plugins.NexusMods;

/// <summary>
/// A download outlives the profile it was started under. The transfer runs on its own task holding
/// its entry, so switching profile never stopped it — but the row used to disappear from the list
/// the moment the profile changed, and the completion was then written to whichever profile was
/// open by then, or to none at all. These pin the seam between the visible list and the two
/// profiles' stored rows.
/// </summary>
public class DownloadProfileSwitchTests : IDisposable
{
    private readonly string _dir;
    private readonly NexusDownloadService _service;

    public DownloadProfileSwitchTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "CMM_Dl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        var db  = new NexusDatabase(_dir);
        var log = new SilentLogger();
        _service = new NexusDownloadService(new NexusApiService(db), log, new NexusModTrackingService(db), db);
    }

    private static DownloadEntry Running(string name, int modId) => new()
    {
        ModName = name, FileName = $"mod_{modId}", ModId = modId, FileId = modId * 10,
        Status = "Downloading…", IsActive = true
    };

    private static DownloadEntry Done(string name, int modId) => new()
    {
        ModName = name, FileName = $"{name}.7z", ModId = modId, FileId = modId * 10,
        Status = "Done", LocalPath = $"/downloads/{name}.7z"
    };

    [Fact]
    public void ATransferStillRunningStaysVisibleAfterSwitchingProfile()
    {
        _service.LoadDownloads("alpha");
        _service.Downloads.Add(Running("Big Archive", 1));
        _service.SaveDownloads("alpha");

        _service.LoadDownloads("beta");

        Assert.Contains(_service.Downloads, d => d.ModName == "Big Archive");
    }

    /// <summary>
    /// Settled entries are the other profile's business. Carrying them too would show the user a
    /// list that is not the one they switched to.
    /// </summary>
    [Fact]
    public void AFinishedDownloadDoesNotFollowTheUserToTheNextProfile()
    {
        _service.LoadDownloads("alpha");
        _service.Downloads.Add(Done("Old Mod", 2));
        _service.SaveDownloads("alpha");

        _service.LoadDownloads("beta");

        Assert.DoesNotContain(_service.Downloads, d => d.ModName == "Old Mod");
    }

    /// <summary>
    /// The carried entry is only passing through: saving the profile it is displayed over must not
    /// adopt it, or the same download ends up recorded against both.
    /// </summary>
    [Fact]
    public void TheCarriedEntryIsNotWrittenIntoTheProfileItIsMerelyShownOver()
    {
        _service.LoadDownloads("alpha");
        _service.Downloads.Add(Running("Big Archive", 1));
        _service.SaveDownloads("alpha");

        _service.LoadDownloads("beta");
        _service.SaveDownloads("beta");

        _service.LoadDownloads("beta");
        Assert.DoesNotContain(_service.Downloads, d => d.ModName == "Big Archive" && d.OwnerProfile == "beta");
    }

    /// <summary>
    /// The case the whole thing exists for: the file lands on disk after the user has moved on, and
    /// the profile that asked for it has to end up with the path, not with an interrupted row.
    /// </summary>
    [Fact]
    public void FinishingAfterTheSwitchWritesThePathBackToTheProfileThatStartedIt()
    {
        _service.LoadDownloads("alpha");
        var entry = Running("Big Archive", 1);
        _service.Downloads.Add(entry);
        _service.SaveDownloads("alpha");

        _service.LoadDownloads("beta");

        // The transfer finishes while beta is open.
        entry.IsActive  = false;
        entry.Status    = "Done";
        entry.LocalPath = "/downloads/big.7z";
        _service.SaveDownloads("beta");

        _service.LoadDownloads("alpha");

        var stored = Assert.Single(_service.Downloads, d => d.ModName == "Big Archive");
        Assert.Equal("/downloads/big.7z", stored.LocalPath);
        Assert.False(stored.HasFailed);
    }

    [Fact]
    public void LoadedEntriesKnowWhichProfileTheyBelongTo()
    {
        _service.LoadDownloads("alpha");
        _service.Downloads.Add(Done("Old Mod", 2));
        _service.SaveDownloads("alpha");

        _service.LoadDownloads("alpha");

        Assert.All(_service.Downloads, d => Assert.Equal("alpha", d.OwnerProfile));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private sealed class SilentLogger : IPluginLogger
    {
        public void Log(string message) { }
        public void LogError(string message, Exception? ex = null) { }
    }
}

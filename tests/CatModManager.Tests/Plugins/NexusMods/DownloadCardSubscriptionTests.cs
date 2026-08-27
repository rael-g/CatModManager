using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using Avalonia.Headless.XUnit;
using CatModManager.PluginSdk;
using CmmPlugin.NexusMods;
using Xunit;

namespace CatModManager.Tests.Plugins.NexusMods;

/// <summary>
/// The downloads tab rebuilds every card whenever the collection changes or any entry flips between
/// active and finished. Each card subscribed to its entry's PropertyChanged and nothing ever
/// unsubscribed, so handlers piled up: one dead one per rebuild per entry, each pinning the
/// discarded card's whole visual tree and still running on every progress tick. A batch of large
/// downloads therefore made the UI thread slower with every rebuild while memory only grew.
/// </summary>
public class DownloadCardSubscriptionTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "cmm-dlcards-" + Path.GetRandomFileName());

    /// <summary>
    /// Live PropertyChanged subscribers, read off the backing delegate — the leak is not observable
    /// from the public surface, which is exactly why it went unnoticed.
    /// </summary>
    private static int SubscriberCount(DownloadEntry entry)
    {
        for (var type = entry.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField("PropertyChanged",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) continue;

            return field.GetValue(entry) is PropertyChangedEventHandler handler
                ? handler.GetInvocationList().Length
                : 0;
        }
        return 0;
    }

    [AvaloniaFact]
    public void RebuildingTheCardsDoesNotAccumulateHandlersOnAnEntry()
    {
        Directory.CreateDirectory(_tempDir);

        var db  = new NexusDatabase(_tempDir);
        var log = new SilentLogger();
        var api = new NexusApiService(db);
        var service = new NexusDownloadService(api, log, new NexusModTrackingService(db), db);

        var tab = new NexusDownloadsTabControl(service, api);

        var entry = new DownloadEntry { ModName = "Big Texture Pack", Status = "Downloading", IsActive = true };
        service.Downloads.Add(entry);

        int afterFirstBuild = SubscriberCount(entry);
        Assert.True(afterFirstBuild >= 1, "The card should be listening to its entry at all.");

        // What a batch of downloads does to this control: many rebuilds over the same entries.
        for (int i = 0; i < 25; i++)
        {
            service.Downloads.Add(new DownloadEntry { ModName = $"Other {i}" });
            service.Downloads.RemoveAt(service.Downloads.Count - 1);
        }

        Assert.Equal(afterFirstBuild, SubscriberCount(entry));

        GC.KeepAlive(tab);
    }

    private sealed class SilentLogger : IPluginLogger
    {
        public void Log(string message) { }
        public void LogError(string message, Exception? ex = null) { }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}

using Avalonia.Threading;

namespace CmmPlugin.NexusMods;

/// <summary>
/// State transitions for a <see cref="DownloadEntry"/>. Every one of these marshals to the UI
/// thread, because the download pipeline runs on background tasks and the entry is bound to the
/// downloads list. Previously each call site spelled out its own Dispatcher.Post with the same
/// three-property assignment, which is how a few of them ended up forgetting to clear IsActive.
/// </summary>
public static class DownloadEntryExtensions
{
    /// <summary>Updates the status message without changing whether the entry is running.</summary>
    public static void SetStatus(this DownloadEntry entry, string status) =>
        Dispatcher.UIThread.Post(() => entry.Status = status);

    /// <summary>Marks the entry as running, with a user-facing status message.</summary>
    public static void Begin(this DownloadEntry entry, string status) =>
        Dispatcher.UIThread.Post(() =>
        {
            entry.IsActive  = true;
            entry.HasFailed = false;
            entry.Status    = status;
        });

    /// <summary>Marks the entry as failed. <paramref name="reason"/> is shown verbatim.</summary>
    public static void Fail(this DownloadEntry entry, string reason) =>
        Dispatcher.UIThread.Post(() =>
        {
            entry.HasFailed = true;
            entry.IsActive  = false;
            entry.Status    = reason;
        });

    /// <summary>
    /// Marks the entry as cancelled. Unlike <see cref="Fail"/> this does not set HasFailed —
    /// a cancelled download is not offered a Retry button.
    /// </summary>
    public static void MarkCancelled(this DownloadEntry entry) =>
        Dispatcher.UIThread.Post(() =>
        {
            entry.IsActive = false;
            entry.Status   = "Cancelled";
        });

    /// <summary>Marks the entry as finished and records where the file landed.</summary>
    public static void Complete(this DownloadEntry entry, string localPath) =>
        Dispatcher.UIThread.Post(() =>
        {
            entry.LocalPath = localPath;
            entry.Progress  = 100;
            entry.IsActive  = false;
            entry.Status    = "Done";
        });
}

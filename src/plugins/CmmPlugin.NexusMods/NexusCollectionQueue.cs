using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CatModManager.PluginSdk;

namespace CmmPlugin.NexusMods;

/// <summary>One collection mod waiting for the user to click Download on its Nexus page.</summary>
public record PendingCollectionMod(int ModId, int FileId, string Domain, DownloadEntry Entry);

/// <summary>
/// Drives the free-user collection flow: Nexus only hands out a download link when the user clicks
/// the button on the mod page, so the collection's mods are walked one at a time — open a page,
/// wait for the nxm:// callback to claim it, then open the next.
///
/// Split out of NexusDownloadService because this is a state machine with its own invariants
/// (at most one page open, pause/resume, cancellation) that has nothing to do with HTTP transfers.
/// All public members are thread-safe; the nxm callback arrives on a different thread than the one
/// that queued the collection.
/// </summary>
public class NexusCollectionQueue
{
    private readonly IPluginLogger _log;
    private readonly Action<string> _openUrl;

    private readonly Queue<PendingCollectionMod> _queue = new();
    private readonly object _lock = new();
    private PendingCollectionMod? _current;
    private bool _pageOpen;
    private bool _paused;

    public NexusCollectionQueue(IPluginLogger log, Action<string>? openUrl = null)
    {
        _log = log;
        _openUrl = openUrl ?? OpenInSystemBrowser;
    }

    /// <summary>Fired whenever the number of pending mods changes. Arg is the new count.</summary>
    public event Action<int>? CountChanged;

    public bool IsPaused => _paused;

    /// <summary>Adds mods to the tail of the queue and starts the walk if nothing is open yet.</summary>
    public void Enqueue(IEnumerable<PendingCollectionMod> mods)
    {
        lock (_lock)
        {
            foreach (var mod in mods) _queue.Enqueue(mod);
        }
        NotifyCount();
        OpenNext();
    }

    public void Pause()
    {
        _paused = true;
        NotifyCount();
    }

    public void Resume()
    {
        _paused = false;
        NotifyCount();
        OpenNext();
    }

    /// <summary>Drops every pending mod (including the one whose page is open) and marks it failed.</summary>
    public void CancelAll()
    {
        List<DownloadEntry> toCancel;
        lock (_lock)
        {
            toCancel = _queue.Select(m => m.Entry).ToList();
            if (_current != null)
            {
                toCancel.Add(_current.Entry);
                _current = null;
            }
            _queue.Clear();
            _pageOpen = false;
            _paused   = false;
        }

        // Fail rather than MarkCancelled: these entries keep HasFailed so the list offers a Retry.
        foreach (var entry in toCancel) entry.Fail("Cancelled");
        NotifyCount();
    }

    /// <summary>
    /// If the given mod+file is the one whose page is currently open, hands back its existing entry
    /// and clears the slot, so the caller reuses it instead of creating a duplicate row.
    /// Returns null when the nxm:// link is an ordinary, non-collection download.
    /// </summary>
    public DownloadEntry? TryClaim(int modId, int fileId)
    {
        lock (_lock)
        {
            if (_current == null || _current.ModId != modId || _current.FileId != fileId)
                return null;

            var entry = _current.Entry;
            _current  = null;
            _pageOpen = false;
            return entry;
        }
    }

    /// <summary>
    /// Opens the next pending mod page in the system browser.
    /// No-ops if paused, the queue is empty, or a page is already open.
    /// </summary>
    public void OpenNext()
    {
        PendingCollectionMod? next;
        lock (_lock)
        {
            if (_pageOpen || _paused) return;

            // Skip entries that were already cancelled
            do
            {
                if (!_queue.TryDequeue(out next)) { NotifyCount(); return; }
            } while (next.Entry.HasFailed || next.Entry.Status == "Cancelled");

            _current  = next;
            _pageOpen = true;
        }

        NotifyCount();
        // Status only — the entry is not transferring yet, so it must not count as an active
        // download (that would block app shutdown while merely waiting on a browser click).
        next.Entry.SetStatus("Click Download on the Nexus page ↗");

        _log.Log($"[NexusMods] Opening browser for collection mod: {next.Entry.ModName}");
        _openUrl($"https://www.nexusmods.com/{next.Domain}/mods/{next.ModId}?tab=files&file_id={next.FileId}&nmm=1");
    }

    private void NotifyCount()
    {
        int count;
        lock (_lock)
            count = _queue.Count + (_current != null ? 1 : 0);
        CountChanged?.Invoke(count);
    }

    private void OpenInSystemBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch (Exception ex) { _log.LogError("[NexusMods] Failed to open browser", ex); }
    }
}

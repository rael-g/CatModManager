using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CatModManager.PluginSdk;

namespace CmmPlugin.NexusMods;

/// <summary>
/// Owns the visible download list and runs transfers against the Nexus API.
///
/// Persistence lives in <see cref="NexusDownloadRepository"/>, the free-user collection walk in
/// <see cref="NexusCollectionQueue"/>, and collection resolution in <see cref="NexusCollectionResolver"/>.
/// What is left here is the transfer pipeline itself: every queued download — nxm://, direct,
/// collection archive — goes through the single <see cref="RunAsync"/> path.
/// </summary>
public class NexusDownloadService
{
    private readonly NexusApiService _api;
    private readonly IPluginLogger _log;
    private readonly NexusModTrackingService _tracking;
    private readonly NexusDownloadRepository _repository;
    private readonly NexusCollectionResolver _resolver;
    private readonly NexusCollectionQueue _collection;

    /// <summary>Limits concurrent HTTP downloads to avoid flooding the Nexus API.</summary>
    private readonly SemaphoreSlim _concurrentDownloads = new(3, 3);

    public ObservableCollection<DownloadEntry> Downloads { get; } = new();

    public NexusDownloadService(NexusApiService api, IPluginLogger log, NexusModTrackingService tracking, NexusDatabase db)
    {
        _api = api;
        _log = log;
        _tracking = tracking;
        _repository = new NexusDownloadRepository(db, log);
        _resolver = new NexusCollectionResolver(api, log);
        _collection = new NexusCollectionQueue(log);
        _collection.CountChanged += count => CollectionQueueCountChanged?.Invoke(count);
    }

    // ── Collection queue facade ───────────────────────────────────────────────

    /// <summary>Fired whenever the number of pending collection mods changes. Arg is the new count.</summary>
    public event Action<int>? CollectionQueueCountChanged;

    public bool IsCollectionQueuePaused => _collection.IsPaused;
    public void PauseCollectionQueue()  => _collection.Pause();
    public void ResumeCollectionQueue() => _collection.Resume();
    public void CancelCollectionQueue() => _collection.CancelAll();

    // ── Persistence ───────────────────────────────────────────────────────────

    public void LoadDownloads(string profileName)
    {
        var loaded = _repository.Load(profileName);
        Downloads.Clear();
        foreach (var entry in loaded) Downloads.Add(entry);
    }

    public void SaveDownloads(string profileName) => _repository.Save(profileName, Downloads);

    // ── Queueing ──────────────────────────────────────────────────────────────

    /// <summary>Queues the download a nxm:// link refers to, reusing the collection entry if it matches.</summary>
    public void QueueDownloadFromNxm(NxmLink link, string modName, string downloadsFolder)
    {
        // If this nxm:// matches the collection mod we opened in the browser, reuse that entry
        // instead of creating a duplicate. This is the core of the free-user collection flow.
        var collectionEntry = _collection.TryClaim(link.ModId, link.FileId);

        DownloadEntry entry;
        if (collectionEntry != null)
        {
            entry = collectionEntry;
        }
        else
        {
            // Guard against duplicate NXM arrivals for the same mod+file (e.g. after Premium redirect).
            if (Downloads.Any(d => d.ModId == link.ModId && d.FileId == link.FileId && !d.HasFailed))
                return;

            entry = new DownloadEntry
            {
                ModName    = modName,
                FileName   = $"mod_{link.ModId}_file_{link.FileId}",
                Status     = "Queued",
                ModId      = link.ModId,
                FileId     = link.FileId,
                GameDomain = link.GameDomain
            };
            Dispatcher.UIThread.Post(() => Downloads.Add(entry));
        }

        StartModDownload(entry, downloadsFolder, link.Key, link.Expires,
            // The nxm:// handler only knows the name the browser passed; the API name is better.
            adoptApiModName: true,
            // A collection mod that finishes (or fails) must release the browser slot, or the
            // rest of the collection never opens.
            onFinished: collectionEntry != null ? _collection.OpenNext : null);
    }

    /// <summary>Queues a download for a known mod+file without going through a nxm:// link.</summary>
    public void QueueDownloadDirect(string gameDomain, int modId, int fileId, string modName, string downloadsFolder, string version = "", string category = "", FomodPreset? fomodPreset = null)
    {
        var entry = new DownloadEntry
        {
            ModName     = modName,
            FileName    = $"mod_{modId}_file_{fileId}",
            Status      = "Queued",
            ModId       = modId,
            FileId      = fileId,
            GameDomain  = gameDomain,
            Version     = version,
            Category    = string.IsNullOrEmpty(category) ? "Uncategorized" : category,
            FomodPreset = fomodPreset
        };

        // Always marshal to UI thread — this method may be called from background threads.
        Dispatcher.UIThread.Post(() => Downloads.Add(entry));

        StartModDownload(entry, downloadsFolder, key: null, expires: null, adoptApiModName: false);
    }

    /// <summary>Removes a failed entry and re-queues a fresh attempt using its existing mod/file IDs.</summary>
    public void RetryDownload(DownloadEntry entry, string downloadsFolder)
    {
        if (entry.IsActive) return;
        // Remove the stale failed entry first, then queue a fresh one
        Dispatcher.UIThread.Post(() => Downloads.Remove(entry));
        QueueDownloadDirect(entry.GameDomain, entry.ModId, entry.FileId, entry.ModName, downloadsFolder, entry.Version, entry.Category);
    }

    /// <summary>Queues a collection archive download given a pre-resolved download URL.</summary>
    public void QueueCollectionDownload(string collectionName, string slug, int revision, string downloadUrl, string downloadsFolder)
    {
        var entry = new DownloadEntry
        {
            ModName    = collectionName,
            FileName   = $"{slug}_rev{revision}.zip",
            Status     = "Queued",
            GameDomain = string.Empty,
            Version    = $"rev{revision}",
            Category   = "Collection",
        };

        Dispatcher.UIThread.Post(() => Downloads.Add(entry));

        RunAsync(entry, async _ =>
        {
            entry.Begin($"Downloading {entry.FileName}...");
            await Task.CompletedTask;
            return downloadUrl;
        }, downloadsFolder);
    }

    // ── Transfer pipeline ─────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a mod's metadata and download link, then transfers it. This is the single path
    /// shared by the nxm:// and direct entry points, which used to be near-identical 90-line copies
    /// that had already drifted apart in how they filled in version and category.
    /// </summary>
    private void StartModDownload(
        DownloadEntry entry, string downloadsFolder, string? key, string? expires,
        bool adoptApiModName, Action? onFinished = null)
    {
        RunAsync(entry, async ct =>
        {
            if (!_api.HasApiKey)
            {
                entry.Fail("No API key. Click the 'Nexus' button to configure.");
                return null;
            }

            entry.Begin("Getting download link...");
            await EnrichMetadataAsync(entry, adoptApiModName, ct);

            var links = await _api.GetDownloadLinksAsync(
                entry.GameDomain, entry.ModId, entry.FileId, key, expires, ct);

            if (links.Count == 0)
            {
                entry.Fail("Failed: No download links available");
                return null;
            }
            return links[0]?.URI;
        }, downloadsFolder, onFinished, premiumFallback: true);
    }

    /// <summary>
    /// Fills in the mod's display name, version and category from the API. Best-effort — a failure
    /// here must not stop the download, since the link request is what actually matters.
    /// </summary>
    private async Task EnrichMetadataAsync(DownloadEntry entry, bool adoptApiModName, CancellationToken ct)
    {
        var details = await _api.GetModDetailsAsync(entry.GameDomain, entry.ModId, ct);
        if (details == null) return;

        var resolvedCategory = await _api.ResolveCategoryAsync(entry.GameDomain, details.CategoryId, ct);

        // Prefer the file-specific version over the mod-page version: authors sometimes upload
        // "v1.2.1" as a new file but forget to bump the mod page, which would still say "1.2".
        string version = details.Version;
        try
        {
            var files = await _api.GetFilesAsync(entry.GameDomain, entry.ModId, ct);
            var matched = files.Files.FirstOrDefault(f => f.FileId == entry.FileId);
            if (matched != null && !string.IsNullOrEmpty(matched.Version)) version = matched.Version;
        }
        catch (OperationCanceledException) { throw; }
        catch { /* best-effort; fall back to mod-page version */ }

        Dispatcher.UIThread.Post(() =>
        {
            if (adoptApiModName) entry.ModName = details.Name;
            // A caller-supplied version is authoritative (e.g. the pin recorded in a collection).
            if (adoptApiModName || string.IsNullOrEmpty(entry.Version)) entry.Version = version;
            if (!string.IsNullOrEmpty(resolvedCategory)) entry.Category = resolvedCategory;
        });
    }

    /// <summary>
    /// Runs one transfer end to end under the concurrency limit: resolve a URL, download it, and
    /// funnel every failure mode into the entry's status. <paramref name="resolveUri"/> returning
    /// null means it already reported its own failure.
    /// </summary>
    private void RunAsync(
        DownloadEntry entry,
        Func<CancellationToken, Task<string?>> resolveUri,
        string downloadsFolder,
        Action? onFinished = null,
        bool premiumFallback = false)
    {
        _ = Task.Run(async () =>
        {
            await _concurrentDownloads.WaitAsync(entry.Cts.Token);
            try
            {
                var uri = await resolveUri(entry.Cts.Token);
                if (uri == null) return;
                await DownloadAndSave(entry, uri, downloadsFolder);
            }
            catch (OperationCanceledException)
            {
                entry.MarkCancelled();
            }
            catch (UnauthorizedAccessException) when (premiumFallback)
            {
                // Nexus Premium required — drop the entry and open the mod page so the user can
                // click the download button and let the nxm:// handler take over.
                Dispatcher.UIThread.Post(() => Downloads.Remove(entry));
                OpenUrl($"https://www.nexusmods.com/{entry.GameDomain}/mods/{entry.ModId}?tab=files");
                _log.Log($"[NexusMods] Premium required for mod {entry.ModId} — opened Nexus page in browser.");
            }
            catch (Exception ex)
            {
                _log.LogError($"[NexusMods] Download failed for mod {entry.ModId}", ex);
                entry.Fail($"Failed: {ex.Message}");
            }
            finally
            {
                _concurrentDownloads.Release();
                onFinished?.Invoke();
            }
        });
    }

    private async Task DownloadAndSave(DownloadEntry entry, string? downloadUri, string downloadsFolder)
    {
        if (string.IsNullOrWhiteSpace(downloadUri))
        {
            entry.Fail("Failed: No download URL");
            return;
        }

        var fileName = Path.GetFileName(new Uri(downloadUri).LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = $"nexus_mod_{entry.ModId}_file_{entry.FileId}.zip";

        Dispatcher.UIThread.Post(() =>
        {
            entry.FileName = fileName;
            entry.Status   = $"Downloading {fileName}...";
        });

        Directory.CreateDirectory(downloadsFolder);
        var destPath = Path.Combine(downloadsFolder, fileName);

        var progress = new Progress<double>(p =>
            Dispatcher.UIThread.Post(() => entry.Progress = p));

        bool ok = await _api.DownloadToFileAsync(downloadUri, destPath, progress, entry.Cts.Token);
        if (!ok)
        {
            entry.Fail("Failed: Download error");
            return;
        }

        _tracking.Track(destPath, entry.ModId, entry.FileId, entry.Version, entry.GameDomain, sourceArchivePath: destPath);
        entry.Complete(destPath);

        _log.Log($"[NexusMods] Downloaded: {fileName} → {destPath}");
    }

    // ── Collections ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a Nexus collection revision and walks its mods one page at a time.
    ///
    /// The mods are added as "Waiting" entries and their Nexus pages are opened in order; each
    /// nxm:// link the user triggers is routed back to the matching entry by QueueDownloadFromNxm.
    /// This works for free and premium accounts alike.
    /// </summary>
    public void QueueCollectionDownloadFromNxm(NxmCollectionLink link, string downloadsFolder)
    {
        var collectionEntry = new DownloadEntry
        {
            ModName    = $"Collection: {link.Slug} rev.{link.Revision}",
            FileName   = $"{link.Slug}_r{link.Revision}",
            Status     = "Queued",
            GameDomain = link.GameDomain
        };

        // Called from UI thread via NXM handler — safe to Add directly.
        Downloads.Add(collectionEntry);

        _ = Task.Run(async () =>
        {
            try
            {
                collectionEntry.Begin("Resolving collection…");

                var resolved = await _resolver.ResolveAsync(
                    link, collectionEntry.SetStatus, collectionEntry.Cts.Token);

                if (resolved.Mods.Count == 0)
                {
                    collectionEntry.Fail("Failed: collection not found or empty.");
                    return;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    collectionEntry.Progress = 100;
                    collectionEntry.IsActive = false;
                    collectionEntry.Status   = resolved.HasManifest
                        ? $"Ready — {resolved.Mods.Count} mod(s) in phase order. Opening pages…"
                        : $"Ready — {resolved.Mods.Count} mod(s). Opening pages…";
                });

                var pending = new List<PendingCollectionMod>();
                var entries = new List<DownloadEntry>();
                foreach (var mod in resolved.Mods)
                {
                    var entry = new DownloadEntry
                    {
                        ModName     = mod.Name,
                        FileName    = $"mod_{mod.ModId}_file_{mod.FileId}",
                        Status      = "Waiting — Nexus page will open",
                        ModId       = mod.ModId,
                        FileId      = mod.FileId,
                        GameDomain  = mod.Domain,
                        Version     = mod.Version,
                        Category    = "Uncategorized",
                        FomodPreset = mod.FomodPreset
                    };
                    entries.Add(entry);
                    pending.Add(new PendingCollectionMod(mod.ModId, mod.FileId, mod.Domain, entry));
                }

                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var e in entries) Downloads.Add(e);
                });

                _collection.Enqueue(pending);
            }
            catch (OperationCanceledException)
            {
                collectionEntry.MarkCancelled();
            }
            catch (Exception ex)
            {
                _log.LogError($"[NexusMods] Collection download failed: {link.Slug}", ex);
                collectionEntry.Fail($"Failed: {ex.Message}");
            }
        });
    }

    // ── Misc ──────────────────────────────────────────────────────────────────

    public void Cancel(DownloadEntry entry) => entry.Cts.Cancel();

    public void Shutdown()
    {
        _log.Log("[NexusMods] Shutdown detected. Cancelling all downloads...");
        CancelCollectionQueue();
        foreach (var entry in Downloads.Where(d => d.IsActive).ToList())
        {
            try { entry.Cts.Cancel(); } catch { }
        }
    }

    public void OpenFolder(DownloadEntry entry)
    {
        if (entry.LocalPath == null) return;
        var folder = Path.GetDirectoryName(entry.LocalPath);
        if (folder != null) OpenUrl(folder);
    }

    private void OpenUrl(string target)
    {
        try { Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true }); }
        catch (Exception ex) { _log.LogError($"[NexusMods] Failed to open {target}", ex); }
    }
}

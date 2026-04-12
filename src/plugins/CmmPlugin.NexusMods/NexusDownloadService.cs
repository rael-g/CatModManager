using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CatModManager.PluginSdk;
using Microsoft.Data.Sqlite;

namespace CmmPlugin.NexusMods;

public class NexusDownloadService
{
    private readonly NexusApiService _api;
    private readonly IPluginLogger _log;
    private readonly NexusModTrackingService _tracking;
    private readonly NexusDatabase _db;

    /// <summary>Limits concurrent HTTP downloads to avoid flooding the Nexus API.</summary>
    private readonly SemaphoreSlim _concurrentDownloads = new(3, 3);

    // ── Collection queue (free-user page-by-page flow) ─────────────────────
    private record PendingCollectionMod(int ModId, int FileId, string Domain, DownloadEntry Entry);
    private readonly Queue<PendingCollectionMod> _collectionQueue = new();
    private PendingCollectionMod? _currentCollectionMod;
    private bool                  _collectionPageOpen;
    private bool                  _collectionPaused;
    private readonly object       _collectionLock = new();

    /// <summary>Fired whenever the number of pending collection mods changes. Arg is the new count.</summary>
    public event Action<int>? CollectionQueueCountChanged;

    private void NotifyQueueCount()
    {
        int count;
        lock (_collectionLock)
            count = _collectionQueue.Count + (_currentCollectionMod != null ? 1 : 0);
        CollectionQueueCountChanged?.Invoke(count);
    }

    public bool IsCollectionQueuePaused => _collectionPaused;

    public void PauseCollectionQueue()  { _collectionPaused = true;  NotifyQueueCount(); }

    public void ResumeCollectionQueue()
    {
        _collectionPaused = false;
        NotifyQueueCount();
        OpenNextCollectionMod();
    }

    public void CancelCollectionQueue()
    {
        List<DownloadEntry> toCancel;
        lock (_collectionLock)
        {
            toCancel = _collectionQueue.Select(m => m.Entry).ToList();
            if (_currentCollectionMod != null) { toCancel.Add(_currentCollectionMod.Entry); _currentCollectionMod = null; }
            _collectionQueue.Clear();
            _collectionPageOpen = false;
            _collectionPaused   = false;
        }
        Dispatcher.UIThread.Post(() => { foreach (var e in toCancel) { e.HasFailed = true; e.Status = "Cancelled"; } });
        NotifyQueueCount();
    }

    public ObservableCollection<DownloadEntry> Downloads { get; } = new();

    public NexusDownloadService(NexusApiService api, IPluginLogger log, NexusModTrackingService tracking, NexusDatabase db)
    {
        _api = api;
        _log = log;
        _tracking = tracking;
        _db = db;
    }

    public void LoadDownloads(string profileName)
    {
        try
        {
            // Read all rows before touching Downloads to avoid a SQLite deadlock:
            // Downloads.Clear() fires CollectionChanged → SaveDownloads → tries to write while
            // the reader still holds a shared lock on nexus.db, blocking the write indefinitely.
            var loaded = new System.Collections.Generic.List<DownloadEntry>();
            using (var conn = _db.Open())
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT mod_name, file_name, local_path, mod_id, file_id, game_domain, version, category, has_failed
                    FROM downloads WHERE profile_name = @profile ORDER BY id ASC
                    """;
                cmd.Parameters.AddWithValue("@profile", profileName);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    bool hasFailed = reader.GetInt32(8) != 0;
                    var path = reader.GetString(2);
                    // Entries with no local path and no failure flag were interrupted mid-download
                    // (app closed while queued/downloading). Show as failed so user knows to retry.
                    bool interrupted = !hasFailed && string.IsNullOrEmpty(path);
                    var entry = new DownloadEntry
                    {
                        ModName    = reader.GetString(0),
                        FileName   = reader.GetString(1),
                        ModId      = reader.GetInt32(3),
                        FileId     = reader.GetInt32(4),
                        GameDomain = reader.GetString(5),
                        Version    = reader.GetString(6),
                        Category   = reader.GetString(7),
                        HasFailed  = hasFailed || interrupted,
                        IsActive   = false,
                        Progress   = (hasFailed || interrupted) ? 0 : 100,
                        Status     = hasFailed ? "Failed" : interrupted ? "Interrupted" : "Done"
                    };
                    entry.LocalPath = string.IsNullOrEmpty(path) ? null : path;
                    loaded.Add(entry);
                }
            } 

            Downloads.Clear();
            foreach (var entry in loaded)
                Downloads.Add(entry);
        }
        catch (Exception ex)
        {
            _log.Log($"[NexusMods] Failed to load downloads: {ex.Message}");
        }
    }

    public void SaveDownloads(string profileName)
    {
        try
        {
            using var conn = _db.Open();
            using var tx  = conn.BeginTransaction();

            using var del = conn.CreateCommand();
            del.CommandText = "DELETE FROM downloads WHERE profile_name = @profile";
            del.Parameters.AddWithValue("@profile", profileName);
            del.ExecuteNonQuery();

            using var ins = conn.CreateCommand();
            ins.CommandText = """
                INSERT INTO downloads (profile_name, mod_name, file_name, local_path, mod_id, file_id, game_domain, version, category, has_failed)
                VALUES (@profile, @modName, @fileName, @localPath, @modId, @fileId, @gameDomain, @version, @category, @hasFailed)
                """;

            foreach (var e in Downloads)
            {
                ins.Parameters.Clear();
                ins.Parameters.AddWithValue("@profile",    profileName);
                ins.Parameters.AddWithValue("@modName",    e.ModName);
                ins.Parameters.AddWithValue("@fileName",   e.FileName);
                ins.Parameters.AddWithValue("@localPath",  e.LocalPath ?? string.Empty);
                ins.Parameters.AddWithValue("@modId",      e.ModId);
                ins.Parameters.AddWithValue("@fileId",     e.FileId);
                ins.Parameters.AddWithValue("@gameDomain", e.GameDomain);
                ins.Parameters.AddWithValue("@version",    e.Version);
                ins.Parameters.AddWithValue("@category",   e.Category);
                ins.Parameters.AddWithValue("@hasFailed",  e.HasFailed ? 1 : 0);
                ins.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            _log.Log($"[NexusMods] Failed to save downloads: {ex.Message}");
        }
    }

    public void QueueDownloadFromNxm(NxmLink link, string modName, string downloadsFolder)
    {
        // If this nxm:// matches the collection mod we opened in the browser, reuse that entry
        // instead of creating a duplicate. This is the core of the free-user collection flow.
        bool isCollectionMod = false;
        DownloadEntry? collectionEntry = null;
        lock (_collectionLock)
        {
            if (_currentCollectionMod != null &&
                _currentCollectionMod.ModId == link.ModId &&
                _currentCollectionMod.FileId == link.FileId)
            {
                collectionEntry     = _currentCollectionMod.Entry;
                _currentCollectionMod = null;
                _collectionPageOpen   = false;
                isCollectionMod     = true;
            }
        }

        var entry = collectionEntry ?? new DownloadEntry
        {
            ModName    = modName,
            FileName   = $"mod_{link.ModId}_file_{link.FileId}",
            Status     = "Queued",
            ModId      = link.ModId,
            FileId     = link.FileId,
            GameDomain = link.GameDomain
        };

        // Only add to list if it's not already there (collection entries are added during resolution).
        // Also guard against duplicate NXM arrivals for the same mod+file (e.g. after Premium redirect).
        if (collectionEntry == null)
        {
            bool alreadyQueued = Downloads.Any(d => d.ModId == link.ModId && d.FileId == link.FileId && !d.HasFailed);
            if (alreadyQueued) return;
            Dispatcher.UIThread.Post(() => Downloads.Add(entry));
        }

        _ = Task.Run(async () =>
        {
            await _concurrentDownloads.WaitAsync(entry.Cts.Token);
            try
            {
                if (!_api.HasApiKey)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        entry.HasFailed = true;
                        entry.IsActive  = false;
                        entry.Status    = "No API key. Click the 'Nexus' button to configure.";
                    });
                    return;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    entry.IsActive = true;
                    entry.Status   = "Getting download link...";
                });

                var details = await _api.GetModDetailsAsync(link.GameDomain, link.ModId, entry.Cts.Token);
                if (details != null)
                {
                    var resolvedCategory = await _api.ResolveCategoryAsync(link.GameDomain, details.CategoryId, entry.Cts.Token);

                    // Prefer the file-specific version (NexusFile.Version) over the mod-page version
                    // (NexusModDetails.Version). Mod authors sometimes upload "v1.2.1" as a new file
                    // but forget to update the mod page version, which would still show "1.2".
                    string fileVersion = details.Version;
                    try
                    {
                        var filesResp = await _api.GetFilesAsync(link.GameDomain, link.ModId, entry.Cts.Token);
                        var matchedFile = filesResp.Files.FirstOrDefault(f => f.FileId == link.FileId);
                        if (matchedFile != null && !string.IsNullOrEmpty(matchedFile.Version))
                            fileVersion = matchedFile.Version;
                    }
                    catch { /* best-effort; fall back to mod-page version */ }

                    Dispatcher.UIThread.Post(() =>
                    {
                        entry.ModName = details.Name;
                        entry.Version = fileVersion;
                        if (!string.IsNullOrEmpty(resolvedCategory)) entry.Category = resolvedCategory;
                    });
                }

                var links = await _api.GetDownloadLinksAsync(
                    link.GameDomain, link.ModId, link.FileId,
                    link.Key, link.Expires, entry.Cts.Token);

                if (links.Count == 0)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        entry.HasFailed = true;
                        entry.IsActive  = false;
                        entry.Status    = "Failed: No download links available";
                    });
                    return;
                }

                await DownloadAndSave(entry, links[0]?.URI, downloadsFolder);
            }
            catch (OperationCanceledException)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    entry.IsActive = false;
                    entry.Status   = "Cancelled";
                });
            }
            catch (Exception ex)
            {
                _log.LogError($"[NexusMods] Download failed for mod {link.ModId}", ex);
                Dispatcher.UIThread.Post(() =>
                {
                    entry.HasFailed = true;
                    entry.IsActive  = false;
                    entry.Status    = $"Failed: {ex.Message}";
                });
            }
            finally
            {
                _concurrentDownloads.Release();
                // Advance to the next collection mod (if this was a collection entry)
                if (isCollectionMod) OpenNextCollectionMod();
            }
        });
    }

    public void QueueDownloadDirect(string gameDomain, int modId, int fileId, string modName, string downloadsFolder, string version = "", string category = "", CatModManager.PluginSdk.FomodPreset? fomodPreset = null)
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

        _ = Task.Run(async () =>
        {
            await _concurrentDownloads.WaitAsync(entry.Cts.Token);
            try
            {
                if (!_api.HasApiKey)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        entry.HasFailed = true;
                        entry.IsActive  = false;
                        entry.Status    = "No API key. Click the 'Nexus' button to configure.";
                    });
                    return;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    entry.IsActive = true;
                    entry.Status   = "Getting download link...";
                });

                var details = await _api.GetModDetailsAsync(gameDomain, modId, entry.Cts.Token);
                if (details != null)
                {
                    var resolvedCategory = await _api.ResolveCategoryAsync(gameDomain, details.CategoryId, entry.Cts.Token);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (string.IsNullOrEmpty(entry.Version)) entry.Version = details.Version;
                        if (!string.IsNullOrEmpty(resolvedCategory)) entry.Category = resolvedCategory;
                    });
                }

                var links = await _api.GetDownloadLinksAsync(
                    gameDomain, modId, fileId, key: null, expires: null, entry.Cts.Token);

                if (links.Count == 0)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        entry.HasFailed = true;
                        entry.IsActive  = false;
                        entry.Status    = "Failed: No download links available";
                    });
                    return;
                }

                await DownloadAndSave(entry, links[0]?.URI, downloadsFolder);
            }
            catch (OperationCanceledException)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    entry.IsActive = false;
                    entry.Status   = "Cancelled";
                });
            }
            catch (UnauthorizedAccessException)
            {
                // Nexus Premium required — remove the entry and open the mod page so user can
                // click the NXM button to download as a free user.
                Dispatcher.UIThread.Post(() => Downloads.Remove(entry));
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    $"https://www.nexusmods.com/{gameDomain}/mods/{modId}?tab=files")
                { UseShellExecute = true });
                _log.Log($"[NexusMods] Premium required for mod {modId} — opened Nexus page in browser.");
            }
            catch (Exception ex)
            {
                _log.LogError($"[NexusMods] Download failed for mod {modId}", ex);
                Dispatcher.UIThread.Post(() =>
                {
                    entry.HasFailed = true;
                    entry.IsActive  = false;
                    entry.Status    = $"Failed: {ex.Message}";
                });
            }
            finally
            {
                _concurrentDownloads.Release();
            }
        });
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

        _ = Task.Run(async () =>
        {
            await _concurrentDownloads.WaitAsync(entry.Cts.Token);
            try
            {
                Dispatcher.UIThread.Post(() => { entry.IsActive = true; entry.Status = $"Downloading {entry.FileName}..."; });
                await DownloadAndSave(entry, downloadUrl, downloadsFolder);
            }
            catch (OperationCanceledException)
            {
                Dispatcher.UIThread.Post(() => { entry.IsActive = false; entry.Status = "Cancelled"; });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => { entry.HasFailed = true; entry.IsActive = false; entry.Status = $"Failed: {ex.Message}"; });
            }
            finally { _concurrentDownloads.Release(); }
        });
    }

    private async Task DownloadAndSave(DownloadEntry entry, string? downloadUri, string downloadsFolder)
    {
        if (string.IsNullOrWhiteSpace(downloadUri))
        {
            Dispatcher.UIThread.Post(() =>
            {
                entry.HasFailed = true;
                entry.IsActive  = false;
                entry.Status    = "Failed: No download URL";
            });
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
            Dispatcher.UIThread.Post(() =>
            {
                entry.HasFailed = true;
                entry.IsActive  = false;
                entry.Status    = "Failed: Download error";
            });
            return;
        }

        _tracking.Track(destPath, entry.ModId, entry.FileId, entry.Version, entry.GameDomain, sourceArchivePath: destPath);

        Dispatcher.UIThread.Post(() =>
        {
            entry.LocalPath = destPath;
            entry.Progress  = 100;
            entry.IsActive  = false;
            entry.Status    = "Done";
        });

        _log.Log($"[NexusMods] Downloaded: {fileName} → {destPath}");
    }

    // ── Collection download ───────────────────────────────────────────────────

    /// <summary>
    /// Resolves a Nexus collection revision and queues individual mod downloads.
    ///
    /// Strategy:
    ///  1. GraphQL (v2, no API key) → get modId+fileId for every mod in the collection.
    ///  2. Collection archive (v1, NXM key) → try to get collection.json for phase ordering
    ///     and FOMOD preset choices. Falls back gracefully if unavailable.
    ///  3. Mods are queued in phase order (0 → 1 → 2 …); within a phase in curator order.
    ///  4. Max 3 concurrent downloads (shared semaphore with regular downloads).
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
                Dispatcher.UIThread.Post(() =>
                {
                    collectionEntry.IsActive = true;
                    collectionEntry.Status   = "Resolving collection via Nexus API…";
                });

                // ── Step 1: GraphQL — get the mod list (no API key required) ──────────
                var gql = await _api.QueryCollectionRevisionAsync(
                    link.Slug, link.Revision, collectionEntry.Cts.Token);

                var modFiles = gql?.Data?.CollectionRevision?.ModFiles;
                if (modFiles == null || modFiles.Count == 0)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        collectionEntry.HasFailed = true;
                        collectionEntry.IsActive  = false;
                        collectionEntry.Status    = "Failed: collection not found or empty.";
                    });
                    return;
                }

                // Build a lookup: (modId, fileId) → GraphQL entry
                var gqlMap = modFiles
                    .Where(f => f.File?.Mod != null && f.File.Mod.ModId != 0 && f.FileId != 0)
                    .ToDictionary(f => ((int)f.File!.Mod!.ModId, (int)f.FileId));

                // ── Step 2: collection.json — try to get phase + FOMOD choices ────────
                Dispatcher.UIThread.Post(() =>
                    collectionEntry.Status = "Fetching collection manifest…");

                NexusCollectionManifest? manifest = null;
                var archiveUrl = await _api.GetCollectionArchiveUrlAsync(
                    link.Slug, link.Revision, link.Key, link.Expires, collectionEntry.Cts.Token);

                if (!string.IsNullOrEmpty(archiveUrl))
                {
                    var zipBytes = await _api.GetBytesAsync(archiveUrl, ct: collectionEntry.Cts.Token);
                    if (zipBytes.Length > 0)
                    {
                        try
                        {
                            using var ms  = new System.IO.MemoryStream(zipBytes);
                            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
                            var zipEntry  = zip.GetEntry("collection.json");
                            if (zipEntry != null)
                            {
                                using var stream = zipEntry.Open();
                                manifest = await JsonSerializer.DeserializeAsync<NexusCollectionManifest>(stream);
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.Log($"[NexusMods] Could not parse collection.json: {ex.Message}");
                        }
                    }
                }

                // ── Step 3: Build ordered mod list ────────────────────────────────────
                // If we got a manifest, apply phase ordering and enrich with FOMOD choices.
                // Otherwise fall back to GraphQL order (curator order, phase 0 for all).

                var orderedMods = new List<(int ModId, int FileId, string Domain, string Name, string Version, NexusCollectionFomodChoices? Choices)>();

                if (manifest != null)
                {
                    var required = manifest.Mods
                        .Where(m => !m.Optional &&
                                    string.Equals(m.Source?.Type, "nexus", StringComparison.OrdinalIgnoreCase) &&
                                    m.Source!.ModId != 0 && m.Source.FileId != 0)
                        .OrderBy(m => m.Phase)
                        .ToList();

                    foreach (var m in required)
                    {
                        var src     = m.Source!;
                        string domain = !string.IsNullOrEmpty(src.GameDomain) ? src.GameDomain : link.GameDomain;
                        // Prefer GraphQL name (it's always up-to-date), fall back to manifest name
                        string name = gqlMap.TryGetValue((src.ModId, (int)src.FileId), out var g)
                            ? (g.File?.Mod?.Name ?? m.Name)
                            : m.Name;
                        orderedMods.Add((src.ModId, (int)src.FileId, domain, name, m.Version, m.Choices));
                    }
                }
                else
                {
                    // No manifest — use GraphQL list (already in curator order)
                    foreach (var f in modFiles.Where(f => !f.Optional))
                    {
                        var mod    = f.File?.Mod;
                        if (mod == null || mod.ModId == 0 || f.FileId == 0) continue;
                        string domain  = mod.Game?.DomainName ?? link.GameDomain;
                        string name    = mod.Name.Length > 0 ? mod.Name : $"Mod #{mod.ModId}";
                        string version = f.File?.Version ?? string.Empty;
                        orderedMods.Add((mod.ModId, (int)f.FileId, domain, name, version, null));
                    }
                }

                Dispatcher.UIThread.Post(() =>
                {
                    collectionEntry.Progress = 100;
                    collectionEntry.IsActive = false;
                    collectionEntry.Status   = manifest != null
                        ? $"Ready — {orderedMods.Count} mod(s) in phase order. Opening pages…"
                        : $"Ready — {orderedMods.Count} mod(s). Opening pages…";
                });

                // ── Step 4: Page-by-page flow (works for free and premium users) ──────
                // Add all mod entries as "Waiting" items and open Nexus pages one at a time.
                // When the user clicks Download on a page, the nxm:// link is intercepted
                // by QueueDownloadFromNxm which routes it to the matching waiting entry.
                var modEntries = orderedMods.Select(m => new DownloadEntry
                {
                    ModName     = m.Name,
                    FileName    = $"mod_{m.ModId}_file_{m.FileId}",
                    Status      = "Waiting — Nexus page will open",
                    ModId       = m.ModId,
                    FileId      = m.FileId,
                    GameDomain  = m.Domain,
                    Version     = m.Version,
                    Category    = "Uncategorized",
                    FomodPreset = ConvertToFomodPreset(m.Choices)
                }).ToList();

                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var e in modEntries) Downloads.Add(e);
                });

                lock (_collectionLock)
                {
                    foreach (var (entry, mod) in modEntries.Zip(orderedMods))
                        _collectionQueue.Enqueue(new PendingCollectionMod(mod.ModId, mod.FileId, mod.Domain, entry));
                }
                NotifyQueueCount();

                OpenNextCollectionMod();
            }
            catch (OperationCanceledException)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    collectionEntry.IsActive = false;
                    collectionEntry.Status   = "Cancelled";
                });
            }
            catch (Exception ex)
            {
                _log.LogError($"[NexusMods] Collection download failed: {link.Slug}", ex);
                Dispatcher.UIThread.Post(() =>
                {
                    collectionEntry.HasFailed = true;
                    collectionEntry.IsActive  = false;
                    collectionEntry.Status    = $"Failed: {ex.Message}";
                });
            }
        });
    }

    /// <summary>
    /// Opens the next pending collection mod page in the system browser.
    /// No-ops if paused, the queue is empty, or a page is already open.
    /// Thread-safe.
    /// </summary>
    private void OpenNextCollectionMod()
    {
        PendingCollectionMod? next;
        lock (_collectionLock)
        {
            if (_collectionPageOpen || _collectionPaused) return;

            // Skip entries that were already cancelled
            do
            {
                if (!_collectionQueue.TryDequeue(out next)) { NotifyQueueCount(); return; }
            } while (next.Entry.HasFailed || next.Entry.Status == "Cancelled");

            _currentCollectionMod = next;
            _collectionPageOpen   = true;
        }

        NotifyQueueCount();
        Dispatcher.UIThread.Post(() => next.Entry.Status = "Click Download on the Nexus page ↗");

        var url = $"https://www.nexusmods.com/{next.Domain}/mods/{next.ModId}?tab=files&file_id={next.FileId}&nmm=1";
        _log.Log($"[NexusMods] Opening browser for collection mod: {next.Entry.ModName}");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch (Exception ex) { _log.LogError("[NexusMods] Failed to open browser", ex); }
    }

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

    private static CatModManager.PluginSdk.FomodPreset? ConvertToFomodPreset(NexusCollectionFomodChoices? choices)
    {
        if (choices == null || !string.Equals(choices.Type, "fomod", StringComparison.OrdinalIgnoreCase))
            return null;

        var preset = new CatModManager.PluginSdk.FomodPreset();
        foreach (var option in choices.Options)
        {
            var group = new CatModManager.PluginSdk.FomodPresetGroup { GroupName = option.Name };
            foreach (var choice in option.Choices)
            {
                group.SelectedNames.Add(choice.Name);
                group.SelectedIndices.Add(choice.Idx);
            }
            preset.Groups.Add(group);
        }
        return preset;
    }

    public void OpenFolder(DownloadEntry entry)
    {
        if (entry.LocalPath == null) return;
        try
        {
            var folder = Path.GetDirectoryName(entry.LocalPath);
            if (folder == null) return;
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.LogError("[NexusMods] Failed to open folder", ex);
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
                    var entry = new DownloadEntry
                    {
                        ModName    = reader.GetString(0),
                        FileName   = reader.GetString(1),
                        ModId      = reader.GetInt32(3),
                        FileId     = reader.GetInt32(4),
                        GameDomain = reader.GetString(5),
                        Version    = reader.GetString(6),
                        Category   = reader.GetString(7),
                        HasFailed  = hasFailed,
                        IsActive   = false,
                        Progress   = hasFailed ? 0 : 100,
                        Status     = hasFailed ? "Failed" : "Done"
                    };
                    var path = reader.GetString(2);
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
        var entry = new DownloadEntry
        {
            ModName    = modName,
            FileName   = $"mod_{link.ModId}_file_{link.FileId}",
            Status     = "Queued",
            ModId      = link.ModId,
            FileId     = link.FileId,
            GameDomain = link.GameDomain
        };

        Downloads.Add(entry);

        _ = Task.Run(async () =>
        {
            try
            {
                if (!_api.HasApiKey)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        entry.HasFailed = true;
                        entry.IsActive  = false;
                        entry.Status    = "No API key. Click the 'Nexus' button to configure.";
                    });
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    entry.IsActive = true;
                    entry.Status   = "Getting download link...";
                });

                var details = await _api.GetModDetailsAsync(link.GameDomain, link.ModId, entry.Cts.Token);
                if (details != null)
                {
                    var resolvedCategory = await _api.ResolveCategoryAsync(link.GameDomain, details.CategoryId, entry.Cts.Token);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        entry.ModName = details.Name;
                        entry.Version = details.Version;
                        if (!string.IsNullOrEmpty(resolvedCategory)) entry.Category = resolvedCategory;
                    });
                }

                var links = await _api.GetDownloadLinksAsync(
                    link.GameDomain, link.ModId, link.FileId,
                    link.Key, link.Expires, entry.Cts.Token);

                if (links.Count == 0)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
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
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    entry.IsActive = false;
                    entry.Status   = "Cancelled";
                });
            }
            catch (Exception ex)
            {
                _log.LogError($"[NexusMods] Download failed for mod {link.ModId}", ex);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    entry.HasFailed = true;
                    entry.IsActive  = false;
                    entry.Status    = $"Failed: {ex.Message}";
                });
            }
        });
    }

    public void QueueDownloadDirect(string gameDomain, int modId, int fileId, string modName, string downloadsFolder, string version = "", string category = "")
    {
        var entry = new DownloadEntry
        {
            ModName    = modName,
            FileName   = $"mod_{modId}_file_{fileId}",
            Status     = "Queued",
            ModId      = modId,
            FileId     = fileId,
            GameDomain = gameDomain,
            Version    = version,
            Category   = string.IsNullOrEmpty(category) ? "Uncategorized" : category
        };

        Downloads.Add(entry);

        _ = Task.Run(async () =>
        {
            try
            {
                if (!_api.HasApiKey)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        entry.HasFailed = true;
                        entry.IsActive  = false;
                        entry.Status    = "No API key. Click the 'Nexus' button to configure.";
                    });
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    entry.IsActive = true;
                    entry.Status   = "Getting download link...";
                });

                var details = await _api.GetModDetailsAsync(gameDomain, modId, entry.Cts.Token);
                if (details != null)
                {
                    var resolvedCategory = await _api.ResolveCategoryAsync(gameDomain, details.CategoryId, entry.Cts.Token);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (string.IsNullOrEmpty(entry.Version)) entry.Version = details.Version;
                        if (!string.IsNullOrEmpty(resolvedCategory)) entry.Category = resolvedCategory;
                    });
                }

                var links = await _api.GetDownloadLinksAsync(
                    gameDomain, modId, fileId, key: null, expires: null, entry.Cts.Token);

                if (links.Count == 0)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
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
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    entry.IsActive = false;
                    entry.Status   = "Cancelled";
                });
            }
            catch (Exception ex)
            {
                _log.LogError($"[NexusMods] Download failed for mod {modId}", ex);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    entry.HasFailed = true;
                    entry.IsActive  = false;
                    entry.Status    = $"Failed: {ex.Message}";
                });
            }
        });
    }

    private async Task DownloadAndSave(DownloadEntry entry, string? downloadUri, string downloadsFolder)
    {
        if (string.IsNullOrWhiteSpace(downloadUri))
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
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

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            entry.FileName = fileName;
            entry.Status   = $"Downloading {fileName}...";
        });

        var progress = new Progress<double>(p =>
            Dispatcher.UIThread.InvokeAsync(() => entry.Progress = p));

        var bytes = await _api.GetBytesAsync(downloadUri, progress, entry.Cts.Token);

        if (bytes.Length == 0)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                entry.HasFailed = true;
                entry.IsActive  = false;
                entry.Status    = "Failed: Download returned empty data";
            });
            return;
        }

        Directory.CreateDirectory(downloadsFolder);
        var destPath = Path.Combine(downloadsFolder, fileName);
        await File.WriteAllBytesAsync(destPath, bytes, entry.Cts.Token);

        _tracking.Track(destPath, entry.ModId, entry.FileId, entry.Version, entry.GameDomain, sourceArchivePath: destPath);

        await Dispatcher.UIThread.InvokeAsync(() =>
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
    /// Resolves a collection revision via the Nexus v2 GraphQL API (no API key required)
    /// and queues individual mod downloads. A single "Collection" entry tracks overall progress.
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
        Downloads.Add(collectionEntry);

        _ = Task.Run(async () =>
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    collectionEntry.IsActive = true;
                    collectionEntry.Status   = "Resolving collection via Nexus API…";
                });

                var gql = await _api.QueryCollectionRevisionAsync(
                    link.Slug, link.Revision, collectionEntry.Cts.Token);

                var modFiles = gql?.Data?.CollectionRevision?.ModFiles;
                if (modFiles == null || modFiles.Count == 0)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        collectionEntry.HasFailed = true;
                        collectionEntry.IsActive  = false;
                        collectionEntry.Status    = "Failed: collection not found or empty.";
                    });
                    return;
                }

                var required = modFiles.Where(f => !f.Optional).ToList();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    collectionEntry.Progress = 100;
                    collectionEntry.IsActive = false;
                    collectionEntry.Status   = $"Done — queued {required.Count} mod(s) for download.";
                });

                foreach (var modFile in required)
                {
                    var info       = modFile.File;
                    var mod        = info?.Mod;
                    string domain  = mod?.Game?.DomainName ?? link.GameDomain;
                    string name    = mod?.Name ?? info?.Name ?? $"Mod #{mod?.ModId}";
                    string version = info?.Version ?? string.Empty;

                    if (mod == null || mod.ModId == 0 || modFile.FileId == 0) continue;

                    QueueDownloadDirect(domain, mod.ModId, (int)modFile.FileId,
                        name, downloadsFolder, version);
                }
            }
            catch (OperationCanceledException)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    collectionEntry.IsActive = false;
                    collectionEntry.Status   = "Cancelled";
                });
            }
            catch (Exception ex)
            {
                _log.LogError($"[NexusMods] Collection download failed: {link.Slug}", ex);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    collectionEntry.HasFailed = true;
                    collectionEntry.IsActive  = false;
                    collectionEntry.Status    = $"Failed: {ex.Message}";
                });
            }
        });
    }

    public void Cancel(DownloadEntry entry) => entry.Cts.Cancel();

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

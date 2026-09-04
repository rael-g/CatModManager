using System;
using System.Collections.Generic;
using CatModManager.PluginSdk;

namespace CmmPlugin.NexusMods;

/// <summary>
/// Persists the download list per profile in nexus.db. Split out of NexusDownloadService so the
/// SQLite schema lives in one place and the download pipeline holds no database knowledge.
/// </summary>
public class NexusDownloadRepository
{
    private readonly NexusDatabase _db;
    private readonly IPluginLogger _log;

    public NexusDownloadRepository(NexusDatabase db, IPluginLogger log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>
    /// Reads every stored entry for a profile. Materialises the whole list before returning:
    /// callers assign into an ObservableCollection, and doing that while the reader still holds a
    /// shared lock on nexus.db deadlocks against the CollectionChanged handler's write.
    /// </summary>
    public List<DownloadEntry> Load(string profileName)
    {
        var loaded = new List<DownloadEntry>();
        try
        {
            using var conn = _db.Open();
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
                string path = reader.GetString(2);

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
                    Status     = hasFailed ? "Failed" : interrupted ? "Interrupted" : "Done",
                    LocalPath  = string.IsNullOrEmpty(path) ? null : path
                };
                loaded.Add(entry);
            }
        }
        catch (Exception ex)
        {
            _log.Log($"[NexusMods] Failed to load downloads: {ex.Message}");
        }
        return loaded;
    }

    /// <summary>
    /// Writes back a single entry of a profile that is not the open one.
    ///
    /// <see cref="Save"/> cannot do this job: it deletes the profile's rows and reinserts what it
    /// was handed, so calling it with the one carried-over entry would erase everything else that
    /// profile had. This matches on mod and file id, which is the only identity that survives the
    /// delete-and-reinsert cycle the rows go through.
    /// </summary>
    public void UpdateEntry(string profileName, DownloadEntry entry)
    {
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE downloads
                   SET local_path = @localPath, file_name = @fileName,
                       mod_name = @modName, version = @version,
                       category = @category, has_failed = @hasFailed
                 WHERE profile_name = @profile AND mod_id = @modId AND file_id = @fileId
                """;
            cmd.Parameters.AddWithValue("@profile",   profileName);
            cmd.Parameters.AddWithValue("@modId",     entry.ModId);
            cmd.Parameters.AddWithValue("@fileId",    entry.FileId);
            cmd.Parameters.AddWithValue("@localPath", entry.LocalPath ?? string.Empty);
            cmd.Parameters.AddWithValue("@fileName",  entry.FileName);
            cmd.Parameters.AddWithValue("@modName",   entry.ModName);
            cmd.Parameters.AddWithValue("@version",   entry.Version);
            cmd.Parameters.AddWithValue("@category",  entry.Category);
            cmd.Parameters.AddWithValue("@hasFailed", entry.HasFailed ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _log.Log($"[NexusMods] Failed to update download '{entry.ModName}': {ex.Message}");
        }
    }

    /// <summary>Replaces the profile's stored rows with the given entries, in order.</summary>
    public void Save(string profileName, IEnumerable<DownloadEntry> entries)
    {
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();

            using var del = conn.CreateCommand();
            del.CommandText = "DELETE FROM downloads WHERE profile_name = @profile";
            del.Parameters.AddWithValue("@profile", profileName);
            del.ExecuteNonQuery();

            using var ins = conn.CreateCommand();
            ins.CommandText = """
                INSERT INTO downloads (profile_name, mod_name, file_name, local_path, mod_id, file_id, game_domain, version, category, has_failed)
                VALUES (@profile, @modName, @fileName, @localPath, @modId, @fileId, @gameDomain, @version, @category, @hasFailed)
                """;

            foreach (var e in entries)
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
}

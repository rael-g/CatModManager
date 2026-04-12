using System;
using Microsoft.Data.Sqlite;

namespace CmmPlugin.NexusMods;

public class NexusModTrackingService
{
    private readonly NexusDatabase _db;

    public NexusModTrackingService(NexusDatabase db)
    {
        _db = db;
    }

    public void Track(string modFolderPath, int modId, int fileId, string version, string gameDomain, string? sourceArchivePath = null)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO tracks (mod_folder_path, mod_id, file_id, version, game_domain, source_archive_path)
            VALUES (@path, @modId, @fileId, @version, @domain, @src)
            ON CONFLICT(mod_folder_path) DO UPDATE SET
                mod_id              = excluded.mod_id,
                file_id             = excluded.file_id,
                version             = excluded.version,
                game_domain         = excluded.game_domain,
                source_archive_path = COALESCE(excluded.source_archive_path, source_archive_path)
            """;
        cmd.Parameters.AddWithValue("@path",    modFolderPath);
        cmd.Parameters.AddWithValue("@modId",   modId);
        cmd.Parameters.AddWithValue("@fileId",  fileId);
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@domain",  gameDomain);
        cmd.Parameters.AddWithValue("@src",     (object?)sourceArchivePath ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public NexusTrackEntry? GetEntry(string modFolderPath)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT mod_id, file_id, version, game_domain, source_archive_path
            FROM tracks WHERE mod_folder_path = @path COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("@path", modFolderPath);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new NexusTrackEntry
        {
            ModFolderPath     = modFolderPath,
            ModId             = reader.GetInt32(0),
            FileId            = reader.GetInt32(1),
            Version           = reader.GetString(2),
            GameDomain        = reader.GetString(3),
            SourceArchivePath = reader.IsDBNull(4) ? null : reader.GetString(4)
        };
    }

    public NexusTrackEntry? GetEntryBySourcePath(string sourcePath)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT mod_folder_path, mod_id, file_id, version, game_domain
            FROM tracks WHERE source_archive_path = @src COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("@src", sourcePath);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new NexusTrackEntry
        {
            ModFolderPath     = reader.GetString(0),
            ModId             = reader.GetInt32(1),
            FileId            = reader.GetInt32(2),
            Version           = reader.GetString(3),
            GameDomain        = reader.GetString(4),
            SourceArchivePath = sourcePath
        };
    }

    public bool IsTracked(string modFolderPath) => GetEntry(modFolderPath) != null;

    /// <summary>Returns the tracked installed-folder entry matching both ModId and FileId.
    /// Used to detect true same-file reinstalls vs. different variant files of the same mod.</summary>
    public NexusTrackEntry? GetEntryByModIdAndFileId(int modId, int fileId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT mod_folder_path, version, game_domain, source_archive_path
            FROM tracks WHERE mod_id = @modId AND file_id = @fileId
            """;
        cmd.Parameters.AddWithValue("@modId",  modId);
        cmd.Parameters.AddWithValue("@fileId", fileId);
        using var reader = cmd.ExecuteReader();

        var archiveExts = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".zip", ".7z", ".rar" };

        NexusTrackEntry? fallback = null;
        while (reader.Read())
        {
            var path = reader.GetString(0);
            var entry = new NexusTrackEntry
            {
                ModFolderPath     = path,
                ModId             = modId,
                FileId            = fileId,
                Version           = reader.GetString(1),
                GameDomain        = reader.GetString(2),
                SourceArchivePath = reader.IsDBNull(3) ? null : reader.GetString(3),
            };
            if (!archiveExts.Contains(System.IO.Path.GetExtension(path)))
                return entry;
            fallback ??= entry;
        }
        return fallback;
    }

    /// <summary>Returns the tracked entry with the given Nexus mod ID that corresponds to the
    /// installed mod folder (preferred over download-archive rows). Optionally excludes a path.</summary>
    public NexusTrackEntry? GetEntryByModId(int modId, string? excludeFolderPath = null)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT mod_folder_path, file_id, version, game_domain, source_archive_path
            FROM tracks WHERE mod_id = @modId
            """;
        cmd.Parameters.AddWithValue("@modId", modId);
        using var reader = cmd.ExecuteReader();

        var archiveExts = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".zip", ".7z", ".rar" };

        NexusTrackEntry? fallback = null; // archive-path row, used only if no dir row found
        while (reader.Read())
        {
            var path = reader.GetString(0);
            if (excludeFolderPath != null && string.Equals(path, excludeFolderPath, StringComparison.OrdinalIgnoreCase))
                continue;
            var entry = new NexusTrackEntry
            {
                ModFolderPath     = path,
                ModId             = modId,
                FileId            = reader.GetInt32(1),
                Version           = reader.GetString(2),
                GameDomain        = reader.GetString(3),
                SourceArchivePath = reader.IsDBNull(4) ? null : reader.GetString(4),
            };
            // Prefer rows whose path is a mod folder (not an archive file)
            if (!archiveExts.Contains(System.IO.Path.GetExtension(path)))
                return entry;
            fallback ??= entry;
        }
        return fallback;
    }
}


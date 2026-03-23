using System;
using System.Collections.Generic;
using CatModManager.VirtualFileSystem;
using Microsoft.Data.Sqlite;

namespace CatModManager.Core.Services;

public class SqliteHardlinkStateStore : IHardlinkStateStore
{
    private readonly AppDatabase _db;

    public SqliteHardlinkStateStore(AppDatabase db) => _db = db;

    public void Save(string mountPoint, IReadOnlyList<HardlinkStateEntry> entries)
    {
        using var conn = _db.Open();
        using var tx   = conn.BeginTransaction();
        foreach (var e in entries)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO hardlink_entries (mount_point, rel_path, dest_path, backup_path)
                VALUES (@mp, @rel, @dest, @back)
                """;
            cmd.Parameters.AddWithValue("@mp",   mountPoint);
            cmd.Parameters.AddWithValue("@rel",  e.RelPath);
            cmd.Parameters.AddWithValue("@dest", e.DestPath);
            cmd.Parameters.AddWithValue("@back", (object?)e.BackupPath ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<HardlinkStateEntry> Load(string? mountPoint)
    {
        var result = new List<HardlinkStateEntry>();
        try
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            if (mountPoint != null)
            {
                cmd.CommandText = "SELECT rel_path, dest_path, backup_path FROM hardlink_entries WHERE mount_point = @mp";
                cmd.Parameters.AddWithValue("@mp", mountPoint);
            }
            else
            {
                cmd.CommandText = "SELECT rel_path, dest_path, backup_path FROM hardlink_entries";
            }
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(new HardlinkStateEntry(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
        catch { }
        return result;
    }

    public void Clear(string? mountPoint)
    {
        try
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            if (mountPoint != null)
            {
                cmd.CommandText = "DELETE FROM hardlink_entries WHERE mount_point = @mp";
                cmd.Parameters.AddWithValue("@mp", mountPoint);
            }
            else
            {
                cmd.CommandText = "DELETE FROM hardlink_entries";
            }
            cmd.ExecuteNonQuery();
        }
        catch { }
    }
}

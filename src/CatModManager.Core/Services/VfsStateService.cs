using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace CatModManager.Core.Services;

public class VfsStateService : IVfsStateService
{
    private readonly AppDatabase _db;
    private readonly ILogService _logService;
    private Dictionary<string, string> _activeMounts = new();

    public VfsStateService(AppDatabase db, ILogService logService)
    {
        _db = db;
        _logService = logService;
        LoadState();
    }

    public void RegisterMount(string originalPath, string backupPath)
    {
        _activeMounts[originalPath] = backupPath;
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO active_mounts (original_path, backup_path) VALUES (@orig, @back)
            ON CONFLICT(original_path) DO UPDATE SET backup_path = excluded.backup_path
            """;
        cmd.Parameters.AddWithValue("@orig", originalPath);
        cmd.Parameters.AddWithValue("@back", backupPath);
        cmd.ExecuteNonQuery();
    }

    public void UnregisterMount(string originalPath)
    {
        if (!_activeMounts.Remove(originalPath)) return;
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM active_mounts WHERE original_path = @orig";
        cmd.Parameters.AddWithValue("@orig", originalPath);
        cmd.ExecuteNonQuery();
    }

    public void RecoverStaleMounts()
    {
        if (_activeMounts.Count == 0) return;

        var recovered = new List<string>();
        foreach (var mount in _activeMounts)
        {
            string original = mount.Key;
            string backup   = mount.Value;

            if (Directory.Exists(backup) && !Directory.Exists(original))
            {
                bool success = false;
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        Directory.Move(backup, original);
                        var di = new DirectoryInfo(original);
                        di.Attributes &= ~FileAttributes.Hidden;
                        di.Attributes &= ~FileAttributes.System;
                        success = true;
                        break;
                    }
                    catch { Thread.Sleep(500); }
                }
                if (success)
                {
                    _logService.Log($"Recovered Safe Swap: {original}");
                    recovered.Add(original);
                }
            }
            else if (Directory.Exists(backup) && Directory.Exists(original))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(original).Any())
                    {
                        Directory.Delete(original);
                        Directory.Move(backup, original);
                        var di = new DirectoryInfo(original);
                        di.Attributes &= ~FileAttributes.Hidden;
                        di.Attributes &= ~FileAttributes.System;
                        recovered.Add(original);
                    }
                }
                catch { }
            }
        }

        foreach (var r in recovered)
            UnregisterMount(r);
    }

    private void LoadState()
    {
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT original_path, backup_path FROM active_mounts";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                _activeMounts[reader.GetString(0)] = reader.GetString(1);
        }
        catch { _activeMounts = new Dictionary<string, string>(); }
    }
}

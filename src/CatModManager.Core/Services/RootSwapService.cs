using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace CatModManager.Core.Services;

/// <summary>
/// Deploys mod files from each mod's Root/ subfolder to the game root at mount time,
/// and reverses the operation at unmount time. All moves are recorded in the DB for
/// crash-safe recovery.
/// </summary>
public class RootSwapService : IRootSwapService
{
    public const string RootFolderName = "Root";

    private readonly AppDatabase _db;
    private readonly ILogService _log;

    public RootSwapService(AppDatabase db, ILogService log)
    {
        _db  = db;
        _log = log;
    }

    public async Task DeployAsync(IEnumerable<Mod> activeMods, string gameFolder)
    {
        // Build deployment plan: higher-priority mod wins on filename conflict.
        // activeMods is ordered highest-priority first.
        var plan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in activeMods)
        {
            var rootDir = Path.Combine(mod.RootPath, RootFolderName);
            if (!Directory.Exists(rootDir)) continue;

            foreach (var file in Directory.GetFiles(rootDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(rootDir, file);
                if (!plan.ContainsKey(rel))        // first (highest priority) wins
                    plan[rel] = file;
            }
        }

        if (plan.Count == 0) return;

        await Task.Run(() =>
        {
            using var conn = _db.Open();
            using var tx   = conn.BeginTransaction();

            foreach (var (rel, sourcePath) in plan)
            {
                var destPath = Path.Combine(gameFolder, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                string? backupPath = null;
                if (File.Exists(destPath))
                {
                    backupPath = destPath + ".cmm_backup";
                    File.Move(destPath, backupPath, overwrite: true);
                }

                File.Move(sourcePath, destPath);

                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO root_swap_entries (game_folder, source_path, dest_path, original_backup_path)
                    VALUES (@gf, @src, @dst, @bak)
                    """;
                cmd.Parameters.AddWithValue("@gf",  gameFolder);
                cmd.Parameters.AddWithValue("@src", sourcePath);
                cmd.Parameters.AddWithValue("@dst", destPath);
                cmd.Parameters.AddWithValue("@bak", backupPath as object ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        });

        _log.Log($"[RootSwap] Deployed {plan.Count} file(s) to {gameFolder}");
    }

    public async Task UndeployAsync(string gameFolder)
    {
        var entries = LoadEntries(gameFolder);
        if (entries.Count == 0) return;

        await Task.Run(() =>
        {
            foreach (var e in entries)
                ReverseEntry(e);

            DeleteEntries(gameFolder);
        });

        _log.Log($"[RootSwap] Undeployed {entries.Count} file(s) from {gameFolder}");
    }

    public async Task UndeployModAsync(string modRootPath, string gameFolder)
    {
        var all     = LoadEntries(gameFolder);
        var forMod  = all.Where(e => e.SourcePath.StartsWith(modRootPath, StringComparison.OrdinalIgnoreCase)).ToList();
        if (forMod.Count == 0) return;

        await Task.Run(() =>
        {
            foreach (var e in forMod)
                ReverseEntry(e);

            DeleteEntriesBySource(forMod.Select(e => e.SourcePath).ToList());
        });
    }

    public void RecoverStaleDeployments()
    {
        var gamefolders = LoadAllGameFolders();
        foreach (var gf in gamefolders)
        {
            var entries = LoadEntries(gf);
            if (entries.Count == 0) continue;

            var recovered = 0;
            foreach (var e in entries)
            {
                try { ReverseEntry(e); recovered++; }
                catch { }
            }

            if (recovered > 0)
            {
                DeleteEntries(gf);
                _log.Log($"[RootSwap] Recovered {recovered} stale file(s) for {gf}");
            }
        }
    }

    public bool HasDeployedFiles(string gameFolder)
    {
        using var conn = _db.Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM root_swap_entries WHERE game_folder = @gf";
        cmd.Parameters.AddWithValue("@gf", gameFolder);
        return Convert.ToInt64(cmd.ExecuteScalar()!) > 0;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void ReverseEntry(RootSwapEntry e)
    {
        if (File.Exists(e.DestPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(e.SourcePath)!);
            File.Move(e.DestPath, e.SourcePath, overwrite: true);
        }

        if (e.OriginalBackupPath != null && File.Exists(e.OriginalBackupPath))
            File.Move(e.OriginalBackupPath, e.DestPath, overwrite: true);
    }

    private List<RootSwapEntry> LoadEntries(string gameFolder)
    {
        var list = new List<RootSwapEntry>();
        try
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                SELECT source_path, dest_path, original_backup_path
                FROM root_swap_entries WHERE game_folder = @gf
                """;
            cmd.Parameters.AddWithValue("@gf", gameFolder);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new RootSwapEntry(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        }
        catch { }
        return list;
    }

    private List<string> LoadAllGameFolders()
    {
        var list = new List<string>();
        try
        {
            using var conn = _db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT game_folder FROM root_swap_entries";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetString(0));
        }
        catch { }
        return list;
    }

    private void DeleteEntries(string gameFolder)
    {
        using var conn = _db.Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM root_swap_entries WHERE game_folder = @gf";
        cmd.Parameters.AddWithValue("@gf", gameFolder);
        cmd.ExecuteNonQuery();
    }

    private void DeleteEntriesBySource(List<string> sourcePaths)
    {
        using var conn = _db.Open();
        foreach (var src in sourcePaths)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM root_swap_entries WHERE source_path = @src";
            cmd.Parameters.AddWithValue("@src", src);
            cmd.ExecuteNonQuery();
        }
    }

    private record RootSwapEntry(string SourcePath, string DestPath, string? OriginalBackupPath);
}

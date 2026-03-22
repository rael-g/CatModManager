using System.IO.Compression;
using CatModManager.PluginSdk;
using CmmPlugin.SaveManager.Models;

namespace CmmPlugin.SaveManager.Services;

public class SaveBackup
{
    public string   FilePath  { get; init; } = "";
    public string   Label     { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public long     SizeBytes { get; init; }
}

public class SaveBackupService
{
    private readonly string        _backupsRoot;
    private readonly IPluginLogger _log;
    private const    int           MaxBackups = 15;

    public SaveBackupService(string appDataPath, IPluginLogger log)
    {
        _backupsRoot = Path.Combine(appDataPath, "save_backups");
        _log         = log;
    }

    public string BackupFolderFor(string gameId) =>
        Path.Combine(_backupsRoot, gameId);

    public async Task<string?> CreateBackupAsync(SaveGameDef def, string saveFolder, string? label = null)
    {
        string dest = BackupFolderFor(def.GameId);
        Directory.CreateDirectory(dest);

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string suffix    = string.IsNullOrWhiteSpace(label) ? "" : $"_{SanitizeLabel(label)}";
        string zipPath   = Path.Combine(dest, timestamp + suffix + ".zip");

        try
        {
            await Task.Run(() =>
                ZipFile.CreateFromDirectory(saveFolder, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false));
            _log.Log($"[SaveManager] Backup created: {Path.GetFileName(zipPath)}");
            PruneOldBackups(def.GameId);
            return zipPath;
        }
        catch (Exception ex)
        {
            _log.LogError($"[SaveManager] Backup failed for {def.GameId}", ex);
            return null;
        }
    }

    public IReadOnlyList<SaveBackup> ListBackups(string gameId)
    {
        string folder = BackupFolderFor(gameId);
        if (!Directory.Exists(folder)) return [];

        return Directory.GetFiles(folder, "*.zip")
            .Select(f => new SaveBackup
            {
                FilePath  = f,
                Label     = Path.GetFileNameWithoutExtension(f),
                CreatedAt = File.GetCreationTime(f),
                SizeBytes = new FileInfo(f).Length
            })
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    public async Task RestoreBackupAsync(SaveBackup backup, string saveFolder)
    {
        // Safety snapshot of current saves before overwriting
        if (Directory.Exists(saveFolder) && Directory.EnumerateFileSystemEntries(saveFolder).Any())
        {
            string safetyZip = Path.Combine(
                Path.GetDirectoryName(backup.FilePath)!,
                DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_pre-restore.zip");
            await Task.Run(() =>
                ZipFile.CreateFromDirectory(saveFolder, safetyZip, CompressionLevel.Fastest, false));
        }

        // Clear + extract
        if (Directory.Exists(saveFolder))
            Directory.Delete(saveFolder, recursive: true);
        Directory.CreateDirectory(saveFolder);

        await Task.Run(() => ZipFile.ExtractToDirectory(backup.FilePath, saveFolder));
        _log.Log($"[SaveManager] Restored '{backup.Label}' → {saveFolder}");
    }

    public void DeleteBackup(SaveBackup backup)
    {
        File.Delete(backup.FilePath);
        _log.Log($"[SaveManager] Deleted backup: {backup.Label}");
    }

    private void PruneOldBackups(string gameId)
    {
        var backups = ListBackups(gameId).ToList();
        // ListBackups returns newest-first; prune from the tail
        while (backups.Count > MaxBackups)
        {
            File.Delete(backups[^1].FilePath);
            backups.RemoveAt(backups.Count - 1);
        }
    }

    private static string SanitizeLabel(string label)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(label.Select(c => invalid.Contains(c) ? '_' : c));
    }
}

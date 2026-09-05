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
            else if (!Directory.Exists(backup) && IsVolumePresent(original))
            {
                // Sem backup não há o que restaurar, e a linha continuaria aqui para sempre — foi
                // assim que 102 linhas mortas se acumularam no banco de um usuário.
                //
                // O `IsVolumePresent` é o que separa "a pasta sumiu" de "o disco não está montado".
                // Uma linha de active_mounts é estado de execução e pode ser apagada, mas apagá-la
                // com o volume offline joga fora a única anotação de que aquela troca precisa ser
                // desfeita quando o disco voltar. Na dúvida a linha fica: ela é inerte.
                _logService.Log($"Dropping stale mount record, nothing to restore: {original}");
                recovered.Add(original);
            }
        }

        foreach (var r in recovered)
            UnregisterMount(r);
    }

    /// <summary>
    /// Se a pasta-mãe do alvo existe, o sistema de arquivos está lá e a ausência do alvo é real.
    /// Se nem ela existe, pode ser um HD externo desmontado ou um Flatpak que não subiu — casos em
    /// que caminhos válidos somem temporariamente e apagar seria perder a informação.
    /// </summary>
    private static bool IsVolumePresent(string path)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        return !string.IsNullOrEmpty(parent) && Directory.Exists(parent);
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

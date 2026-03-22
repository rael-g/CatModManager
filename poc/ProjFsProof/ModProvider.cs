using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Windows.ProjFS;

namespace ProjFsProof;

public record VfsEntry(string Name, bool IsDirectory, long Size, DateTime LastWrite);

/// <summary>
/// Provider ProjFS que serve arquivos de mod com prioridade sobre os arquivos base do jogo.
/// Assinaturas baseadas na API real do Microsoft.Windows.ProjFS 2.0.0.
/// </summary>
public sealed class ModProvider : IRequiredCallbacks
{
    private readonly Dictionary<string, string> _fileIndex;
    private readonly Dictionary<string, List<VfsEntry>> _dirIndex;
    private readonly ConcurrentDictionary<Guid, EnumState> _enumerations = new();

    // FileStreams abertos por caminho — evita open/close por chamada em pak files grandes.
    // Múltiplas threads lêem o mesmo arquivo via Seek+Read com lock por stream.
    private readonly ConcurrentDictionary<string, (FileStream Stream, object Lock)> _openFiles = new(StringComparer.OrdinalIgnoreCase);

    private VirtualizationInstance? _instance;

    // Log de erros em arquivo para diagnóstico (console pode não capturar tudo)
    private static readonly string LogPath = Path.Combine(
        AppContext.BaseDirectory, "projfs_errors.log");

    public ModProvider(Dictionary<string, string> fileIndex, HashSet<string>? emptyDirs = null)
    {
        _fileIndex = fileIndex;
        _dirIndex  = BuildDirIndex(fileIndex, emptyDirs ?? new HashSet<string>());
        File.WriteAllText(LogPath, $"=== ProjFS Log {DateTime.Now} ===\n");
    }

    private static void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n";
        Console.Write(line);
        try { File.AppendAllText(LogPath, line); } catch { }
    }

    public void SetInstance(VirtualizationInstance instance) => _instance = instance;

    // ── Índice de diretórios ─────────────────────────────────────────────────

    private static Dictionary<string, List<VfsEntry>> BuildDirIndex(
        Dictionary<string, string> fileIndex, HashSet<string> emptyDirs)
    {
        var dirs = new Dictionary<string, List<VfsEntry>>(StringComparer.OrdinalIgnoreCase);
        dirs[""] = new List<VfsEntry>();

        foreach (var (rel, src) in fileIndex)
        {
            var parts = rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < parts.Length - 1; i++)
            {
                var parentKey = i == 0 ? "" : string.Join(Path.DirectorySeparatorChar, parts[..i]);
                var childKey  = string.Join(Path.DirectorySeparatorChar, parts[..(i + 1)]);
                var dirName   = parts[i];

                if (!dirs.ContainsKey(parentKey)) dirs[parentKey] = new();
                if (!dirs.ContainsKey(childKey))  dirs[childKey]  = new();

                if (!dirs[parentKey].Any(e => e.Name.Equals(dirName, StringComparison.OrdinalIgnoreCase)))
                    dirs[parentKey].Add(new VfsEntry(dirName, true, 0, DateTime.UtcNow));
            }

            var fileDir  = parts.Length == 1 ? "" : string.Join(Path.DirectorySeparatorChar, parts[..^1]);
            var fileName = parts[^1];
            var fi       = new FileInfo(src);

            if (!dirs.ContainsKey(fileDir)) dirs[fileDir] = new();
            dirs[fileDir].RemoveAll(e => e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            dirs[fileDir].Add(new VfsEntry(fileName, false, fi.Length, fi.LastWriteTimeUtc));
        }

        // Adiciona diretórios vazios que não têm arquivos (não seriam incluídos pelo loop acima)
        foreach (var emptyRel in emptyDirs)
        {
            var parts2 = emptyRel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (parts2.Length == 0) continue;

            var dirKey    = emptyRel;
            var parentKey = parts2.Length == 1 ? "" : string.Join(Path.DirectorySeparatorChar, parts2[..^1]);
            var dirName   = parts2[^1];

            if (!dirs.ContainsKey(dirKey))    dirs[dirKey]    = new();
            if (!dirs.ContainsKey(parentKey)) dirs[parentKey] = new();

            if (!dirs[parentKey].Any(e => e.Name.Equals(dirName, StringComparison.OrdinalIgnoreCase)))
                dirs[parentKey].Add(new VfsEntry(dirName, true, 0, DateTime.UtcNow));
        }

        // ProjFS exige ordem alfabética para fazer merge correto com entradas reais (hydrated).
        foreach (var key in dirs.Keys.ToList())
            dirs[key] = dirs[key].OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

        return dirs;
    }

    // ── IRequiredCallbacks ───────────────────────────────────────────────────

    public HResult StartDirectoryEnumerationCallback(
        int commandId, Guid enumerationId, string relativePath,
        uint triggeringProcessId, string triggeringProcessImageFileName)
    {
        var key = Normalize(relativePath);
        _dirIndex.TryGetValue(key, out var entries);
        _enumerations[enumerationId] = new EnumState(entries ?? new());
        return HResult.Ok;
    }

    public HResult EndDirectoryEnumerationCallback(Guid enumerationId)
    {
        _enumerations.TryRemove(enumerationId, out _);
        return HResult.Ok;
    }

    // API real: Add(name, size, isDir, attrs, creation, lastAccess, lastWrite, change)
    // Sem contentId/providerId nesta sobrecarga.
    public HResult GetDirectoryEnumerationCallback(
        int commandId, Guid enumerationId, string filterFileName,
        bool restartScan, IDirectoryEnumerationResults results)
    {
        if (!_enumerations.TryGetValue(enumerationId, out var state))
            return HResult.InternalError;

        if (restartScan)
            state.Reset();

        while (state.HasMore)
        {
            var entry = state.Current;

            if (!MatchesFilter(entry.Name, filterFileName))
            {
                state.Advance();
                continue;
            }

            bool added = results.Add(
                entry.Name,
                entry.IsDirectory ? 0 : entry.Size,
                entry.IsDirectory,
                entry.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
                entry.LastWrite,  // creationTime
                entry.LastWrite,  // lastAccessTime
                entry.LastWrite,  // lastWriteTime
                entry.LastWrite); // changeTime

            if (!added) return HResult.Ok; // buffer cheio

            state.Advance();
        }

        return HResult.Ok;
    }

    public HResult GetPlaceholderInfoCallback(
        int commandId, string relativePath,
        uint triggeringProcessId, string triggeringProcessImageFileName)
    {
        var key = Normalize(relativePath);

        if (_fileIndex.TryGetValue(key, out var src))
        {
            var fi = new FileInfo(src);
            return _instance!.WritePlaceholderInfo(
                relativePath,
                fi.CreationTimeUtc,
                fi.LastAccessTimeUtc,
                fi.LastWriteTimeUtc,
                fi.LastWriteTimeUtc,
                FileAttributes.Normal,
                fi.Length,
                false,
                contentId:  [],
                providerId: []);
        }

        if (_dirIndex.ContainsKey(key))
        {
            var now = DateTime.UtcNow;
            return _instance!.WritePlaceholderInfo(
                relativePath,
                now, now, now, now,
                FileAttributes.Directory,
                0,
                true,
                contentId:  [],
                providerId: []);
        }

        return HResult.FileNotFound;
    }

    // API real: WriteFileData(Guid, IWriteBuffer, ulong, uint) — não byte[]
    // Lê em chunks de 64 MB para evitar overflow em arquivos grandes (pak files de centenas de MB).
    public HResult GetFileDataCallback(
        int commandId, string relativePath,
        ulong byteOffset, uint length,
        Guid dataStreamId, byte[] contentId, byte[] providerId,
        uint triggeringProcessId, string triggeringProcessImageFileName)
    {
        var key = Normalize(relativePath);

        if (!_fileIndex.TryGetValue(key, out var src))
            return HResult.FileNotFound;

        const uint ChunkSize = 64 * 1024 * 1024; // 64 MB

        try
        {
            // Reutiliza FileStream aberto — evita open/close por chamada em pak files grandes.
            var entry = _openFiles.GetOrAdd(src, path =>
                (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), new object()));

            ulong remaining  = length;
            ulong currentOff = byteOffset;

            while (remaining > 0)
            {
                uint chunk = (uint)Math.Min(remaining, ChunkSize);
                var writeBuffer = _instance!.CreateWriteBuffer(chunk);
                var tmp = new byte[chunk];
                int read;

                // Seek+Read não é thread-safe — lock por arquivo
                lock (entry.Lock)
                {
                    entry.Stream.Seek((long)currentOff, SeekOrigin.Begin);
                    read = entry.Stream.Read(tmp, 0, (int)chunk);
                }

                if (read == 0) break;
                writeBuffer.Stream.Write(tmp, 0, read);

                var result = _instance.WriteFileData(dataStreamId, writeBuffer, currentOff, (uint)read);
                if (result != HResult.Ok) return result;

                currentOff += (ulong)read;
                remaining  -= (ulong)read;
            }

            return HResult.Ok;
        }
        catch (Exception ex)
        {
            Log($"[ERROR] GetFileData({relativePath}): {ex.Message}");
            return HResult.InternalError;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Normalize(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar)
            .Trim(Path.DirectorySeparatorChar);

    private static bool MatchesFilter(string name, string? filter)
    {
        if (string.IsNullOrEmpty(filter) || filter == "*" || filter == "*.*")
            return true;

        var pattern = "^" +
            Regex.Escape(filter).Replace("\\*", ".*").Replace("\\?", ".") +
            "$";

        return Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase);
    }
}

internal sealed class EnumState
{
    private readonly List<VfsEntry> _entries;
    private int _index;

    public EnumState(List<VfsEntry> entries) => _entries = entries;
    public bool    HasMore  => _index < _entries.Count;
    public VfsEntry Current => _entries[_index];
    public void Advance()   => _index++;
    public void Reset()     => _index = 0;
}

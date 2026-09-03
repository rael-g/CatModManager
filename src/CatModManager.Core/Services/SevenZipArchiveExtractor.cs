using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CatModManager.PluginSdk;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace CatModManager.Core.Services;

public class SevenZipArchiveExtractor : IArchiveExtractor
{
    /// <summary>
    /// Extracts every file in the archive, in one forward pass.
    ///
    /// The single pass is the whole point. Calling <c>entry.WriteToDirectory</c> per entry is random
    /// access, and in a *solid* archive — the default for <c>.7z</c> — every file shares one LZMA2
    /// stream, so seeking to any file means decoding that stream from the start again. Extraction
    /// then costs O(files × archive size): a 708 MB mod with 126 entries took over ten minutes and
    /// was still slowing down, against 4 seconds for the `7z` CLI on the same file. Iterating with
    /// <see cref="IArchive.ExtractAllEntries"/> decodes the stream once, which is what the CLI does.
    /// </summary>
    public async Task ExtractAsync(string archivePath, string destinationDir, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            using var archive = ArchiveFactory.Open(archivePath);
            int total = archive.Entries.Count(e => !e.IsDirectory);
            int count = 0;

            var options = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };

            using var reader = archive.ExtractAllEntries();
            double lastReported = -1;

            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                if (reader.Entry.IsDirectory) continue;

                reader.WriteEntryToDirectory(destinationDir, options);
                count++;

                // Throttled to whole percentage points, the way the downloader already does it.
                // Progress<T> marshals every report to the UI thread, and the handler there
                // recomputes an aggregate over the mod list and invalidates layout. Reporting once
                // per entry meant a large archive enqueued one such callback per file — tens of
                // thousands of them, for several archives at once. The UI thread could not drain
                // the queue as fast as extraction filled it, so the window stopped responding and
                // the backlog grew until the process was killed for memory.
                if (progress != null && total > 0)
                {
                    double pct = (double)count / total * 100;
                    if (pct - lastReported >= 1.0 || count == total)
                    {
                        progress.Report(pct);
                        lastReported = pct;
                    }
                }
            }
        }, ct);
    }

    /// <summary>
    /// The archive's <em>files</em>. Folder entries are deliberately excluded: every caller routes
    /// what it gets here as a file, and the folder tree is already implied by the file paths.
    /// Handing them back let an installer map "Data" onto itself, which InstallModFromMappingAsync
    /// resolves with CopyDirectory — installing the whole subtree a second time, next to the
    /// correctly routed files.
    /// </summary>
    public IEnumerable<string> GetFileList(string archivePath)
    {
        using var archive = ArchiveFactory.Open(archivePath);
        return archive.Entries
            .Where(e => !e.IsDirectory)
            .Select(e => e.Key.Replace('/', '\\'))
            .ToList();
    }

    public Stream? OpenFileStream(string archivePath, string entryPath)
    {
        // We must return a stream that doesn't depend on the archive being disposed immediately.
        // MemoryStream is safest for small metadata files (FOMOD, etc).
        // For larger files, this interface might need a more complex 'Entry' abstraction.
        
        using var archive = ArchiveFactory.Open(archivePath);
        var entry = archive.Entries.FirstOrDefault(e => 
            string.Equals(e.Key.Replace('/', '\\'), entryPath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase));
        
        if (entry == null || entry.IsDirectory) return null;

        var ms = new MemoryStream();
        using (var entryStream = entry.OpenEntryStream())
        {
            entryStream.CopyTo(ms);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// One forward pass for all of them, for the same reason <see cref="ExtractAsync"/> makes one:
    /// a solid archive decodes from the start of the block for every random access, so the loop
    /// this replaces cost O(entries × archive size).
    /// </summary>
    public IReadOnlyDictionary<string, Stream> OpenFileStreams(string archivePath, IEnumerable<string> entryPaths)
    {
        // Map normalized key → the caller's spelling, so results come back under the paths asked for.
        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in entryPaths)
            wanted.TryAdd(p.Replace('/', '\\'), p);

        var result = new Dictionary<string, Stream>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return result;

        using var archive = ArchiveFactory.Open(archivePath);
        using var reader  = archive.ExtractAllEntries();

        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory) continue;
            if (!wanted.TryGetValue(reader.Entry.Key?.Replace('/', '\\') ?? "", out var asked)) continue;

            var ms = new MemoryStream();
            using (var es = reader.OpenEntryStream()) es.CopyTo(ms);
            ms.Position = 0;
            result[asked] = ms;

            // Nothing left to find: stop decoding the rest of the archive. For a wizard whose
            // images sit near the front, this is the difference between reading a few megabytes
            // and reading the whole gigabyte.
            if (result.Count == wanted.Count) break;
        }

        return result;
    }
}

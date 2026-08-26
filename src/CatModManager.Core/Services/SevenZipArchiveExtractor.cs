using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CatModManager.PluginSdk;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace CatModManager.Core.Services;

public class SevenZipArchiveExtractor : IArchiveExtractor
{
    public async Task ExtractAsync(string archivePath, string destinationDir, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            using var archive = ArchiveFactory.Open(archivePath);
            int total = archive.Entries.Count(e => !e.IsDirectory);
            int count = 0;

            var options = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.IsDirectory) continue;

                entry.WriteToDirectory(destinationDir, options);
                count++;
                progress?.Report((double)count / total * 100);
            }
        }, ct);
    }

    /// <summary>
    /// The archive's <em>files</em>. Folder entries are deliberately excluded: every caller routes
    /// what it gets here as a file, and the folder tree is already implied by the file paths.
    /// Handing them back made BethesdaModInstaller map "Data" onto itself, which
    /// InstallModFromMappingAsync resolves with CopyDirectory — installing the whole subtree a
    /// second time, next to the correctly routed files.
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
}

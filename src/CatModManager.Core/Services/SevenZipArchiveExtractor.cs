using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            
            // Filter entries to extract (exclude directories as they are created automatically)
            var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
            if (entries.Count == 0) return;

            double total = entries.Count;
            double current = 0;

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                entry.WriteToDirectory(destinationDir, new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true
                });

                current++;
                progress?.Report(current / total * 100.0);
            }
        }, ct);
    }
}

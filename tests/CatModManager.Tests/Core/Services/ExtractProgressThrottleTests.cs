using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using CatModManager.Core.Services;
using Xunit;

namespace CatModManager.Tests.Core.Services;

/// <summary>
/// Extraction progress must be throttled at the source.
///
/// Every report crosses to the UI thread — Progress&lt;T&gt; captures the synchronisation context —
/// and the handler recomputes an aggregate over the whole mod list and invalidates layout. Reporting
/// once per entry means an archive with tens of thousands of files enqueues tens of thousands of
/// those, several archives at a time. The UI thread cannot drain the queue as fast as the extractor
/// fills it, so the queue grows without bound: the window stops responding first and memory climbs
/// until the process is killed. The download path already throttles this way.
/// </summary>
public class ExtractProgressThrottleTests
{
    private const int EntryCount = 5000;

    [Fact]
    public async Task ExtractingManyEntriesDoesNotReportOncePerEntry()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cmm-throttle-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        string archivePath = Path.Combine(dir, "many.zip");
        string dest = Path.Combine(dir, "out");
        Directory.CreateDirectory(dest);

        try
        {
            using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                for (int i = 0; i < EntryCount; i++)
                {
                    var entry = zip.CreateEntry($"files/file{i}.txt");
                    using var s = new StreamWriter(entry.Open());
                    s.Write(i);
                }
            }

            int reports = 0;
            var progress = new Progress<double>(_ => Interlocked.Increment(ref reports));

            await new SevenZipArchiveExtractor()
                .ExtractAsync(archivePath, dest, progress, CancellationToken.None);

            // Progress<T> dispatches asynchronously; without a sync context it goes to the thread
            // pool, so give the queued callbacks a moment to land before counting.
            await Task.Delay(500);

            Assert.Equal(EntryCount, Directory.GetFiles(Path.Combine(dest, "files")).Length);

            // 100 buckets plus slack. The point is that it is bounded by the percentage scale
            // rather than by how many files the archive happens to contain.
            Assert.InRange(reports, 1, 110);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}

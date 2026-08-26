using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CatModManager.Core.Services;
using Xunit;

namespace CatModManager.Tests.Core.Services;

/// <summary>
/// The Cancel button on an installing mod has always been wired to a real CancellationToken, but it
/// looked dead: extraction only checks the token between entries, and while a solid .7z was being
/// re-decoded per entry a single entry could take minutes. Responsiveness therefore depends on
/// entries completing promptly — worth pinning, since it is the difference between a button that
/// works and one that appears not to.
/// </summary>
public class ExtractionCancellationTests : IDisposable
{
    private readonly string _tempDir;

    public ExtractionCancellationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_Cancel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    private string MakeZip(int entries)
    {
        string zipPath = Path.Combine(_tempDir, "many.zip");
        var payload = new string('x', 64 * 1024);
        using var fs = new FileStream(zipPath, FileMode.Create);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        for (int i = 0; i < entries; i++)
        {
            var e = zip.CreateEntry($"folder/file{i}.txt");
            using var w = new StreamWriter(e.Open());
            w.Write(payload);
        }
        return zipPath;
    }

    [Fact]
    public async Task Cancelling_StopsExtraction_AndSurfacesAsCancellation()
    {
        string zipPath = MakeZip(400);
        string outDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outDir);

        var cts = new CancellationTokenSource();
        var extractor = new SevenZipArchiveExtractor();

        // Cancel as soon as extraction has visibly started, so we exercise mid-run cancellation
        // rather than a token that was already cancelled before the first check.
        var progress = new Progress<double>(p => { if (p > 0) cts.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => extractor.ExtractAsync(zipPath, outDir, progress, cts.Token));

        int written = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Length;
        Assert.True(written < 400, $"Extraction should have stopped early, but wrote {written}/400 files.");
    }

    [Fact]
    public async Task AlreadyCancelledToken_ExtractsNothing()
    {
        string zipPath = MakeZip(10);
        string outDir = Path.Combine(_tempDir, "out2");
        Directory.CreateDirectory(outDir);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new SevenZipArchiveExtractor().ExtractAsync(zipPath, outDir, null, cts.Token));

        Assert.Empty(Directory.GetFiles(outDir, "*", SearchOption.AllDirectories));
    }
}

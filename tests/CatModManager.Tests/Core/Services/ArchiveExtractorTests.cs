using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using CatModManager.Core.Services;

namespace CatModManager.Tests.Core.Services;

public class ArchiveExtractorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _zipPath;

    public ArchiveExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_Archive_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _zipPath = Path.Combine(_tempDir, "test.zip");

        // Create a real zip file using System.IO.Compression for testing
        using (var fs = new FileStream(_zipPath, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry1 = zip.CreateEntry("file1.txt");
            using (var writer = new StreamWriter(entry1.Open())) writer.Write("content1");

            var entry2 = zip.CreateEntry("sub/file2.txt");
            using (var writer = new StreamWriter(entry2.Open())) writer.Write("content2");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void GetFileList_Should_Return_All_Files()
    {
        var extractor = new SevenZipArchiveExtractor();
        var files = extractor.GetFileList(_zipPath).ToList();

        Assert.Equal(2, files.Count);
        Assert.Contains("file1.txt", files);
        Assert.Contains("sub\\file2.txt", files);
    }

    [Fact]
    public async Task ExtractAsync_Should_Extract_All_Files()
    {
        var extractor = new SevenZipArchiveExtractor();
        string extractDir = Path.Combine(_tempDir, "Extracted");
        Directory.CreateDirectory(extractDir);
        
        await extractor.ExtractAsync(_zipPath, extractDir);

        Assert.True(File.Exists(Path.Combine(extractDir, "file1.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, "sub", "file2.txt")));
        Assert.Equal("content1", File.ReadAllText(Path.Combine(extractDir, "file1.txt")));
    }

    [Fact]
    public void OpenFileStream_Should_Return_Valid_Stream()
    {
        var extractor = new SevenZipArchiveExtractor();
        using var stream = extractor.OpenFileStream(_zipPath, "file1.txt");
        
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        Assert.Equal("content1", reader.ReadToEnd());
    }
}

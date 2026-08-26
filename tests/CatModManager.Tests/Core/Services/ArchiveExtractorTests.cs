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
    public void GetFileList_OmitsDirectoryEntries()
    {
        // Many archives carry explicit entries for the folders themselves. ExtractAsync has always
        // skipped them, but GetFileList used to hand them back alongside real files — and callers
        // treat every entry as a file to route. In BethesdaModInstaller that produced a mapping of
        // "Data" → "Data", which the mapping installer resolved with CopyDirectory, copying the
        // whole subtree verbatim *in addition* to the per-file routing. A mod shipping
        // Data/SFSE/Plugins/x.dll ended up installed twice: once at SFSE/Plugins and once at
        // Data/SFSE/Plugins.
        string zipWithDirs = Path.Combine(_tempDir, "withdirs.zip");
        using (var fs = new FileStream(zipWithDirs, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            zip.CreateEntry("Data/");
            zip.CreateEntry("Data/SFSE/");
            zip.CreateEntry("Data/SFSE/Plugins/");
            var dll = zip.CreateEntry("Data/SFSE/Plugins/sfee.dll");
            using var writer = new StreamWriter(dll.Open());
            writer.Write("binary");
        }

        var files = new SevenZipArchiveExtractor().GetFileList(zipWithDirs).ToList();

        Assert.Equal(new[] { "Data\\SFSE\\Plugins\\sfee.dll" }, files);
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

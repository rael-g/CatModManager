using System;
using System.IO;
using Xunit;
using CatModManager.Core.Services;

namespace CatModManager.Tests.Core.Services;

public class FileServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PhysicalFileService _service;

    public FileServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_FileService_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _service = new PhysicalFileService();
    }

    [Fact]
    public void FileExists_ReturnsTrue_WhenFileExists()
    {
        string path = Path.Combine(_tempDir, "exist.txt");
        File.WriteAllText(path, "hi");
        Assert.True(_service.FileExists(path));
    }

    [Fact]
    public void DirectoryExists_ReturnsTrue_WhenDirectoryExists()
    {
        string path = Path.Combine(_tempDir, "exist_dir");
        Directory.CreateDirectory(path);
        Assert.True(_service.DirectoryExists(path));
    }

    [Fact]
    public void CreateDirectory_CreatesActualDirectoryOnDisk()
    {
        string path = Path.Combine(_tempDir, "new_dir");
        _service.CreateDirectory(path);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void CopyFile_CopiesContentToDestination()
    {
        string src = Path.Combine(_tempDir, "src.txt");
        string dst = Path.Combine(_tempDir, "dst.txt");
        File.WriteAllText(src, "data");

        _service.CopyFile(src, dst, true);

        Assert.Equal("data", File.ReadAllText(dst));
    }

    [Fact]
    public void DeleteFile_RemovesFileFromDisk()
    {
        string path = Path.Combine(_tempDir, "delete.txt");
        File.WriteAllText(path, "bye");
        
        _service.DeleteFile(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CopyDirectory_CopiesFilesRecursively()
    {
        string src = Path.Combine(_tempDir, "src_dir");
        string dst = Path.Combine(_tempDir, "dst_dir");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "test.txt"), "recursive");

        _service.CopyDirectory(src, dst);

        Assert.True(File.Exists(Path.Combine(dst, "test.txt")));
    }

    [Fact]
    public void MoveDirectory_MovesAndReplacesTarget()
    {
        string src = Path.Combine(_tempDir, "move_src");
        string dst = Path.Combine(_tempDir, "move_dst");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(dst);
        File.WriteAllText(Path.Combine(src, "moved.txt"), "moved");

        _service.MoveDirectory(src, dst);

        Assert.True(File.Exists(Path.Combine(dst, "moved.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}

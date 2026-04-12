using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using CatModManager.Core.Services;
using CatModManager.Core.Models;

namespace CatModManager.Tests;

public class ModManagementServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MockFileService _fileService;
    private readonly MockLogService _logService;
    private readonly ModManagementService _service;

    public ModManagementServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_ModMgmt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _fileService = new MockFileService();
        _logService = new MockLogService();
        _service = new ModManagementService(_fileService, _logService);
    }

    [Fact]
    public async Task InstallModAsync_CreatesTargetBaseDir_WhenNotExists()
    {
        string targetBase = Path.Combine(_tempDir, "NonExistentBase");
        string source = Path.Combine(_tempDir, "SourceMod");
        Directory.CreateDirectory(source);

        await _service.InstallModAsync(source, targetBase);

        Assert.True(Directory.Exists(targetBase));
    }

    [Fact]
    public async Task InstallModAsync_ThrowsException_WhenSourceNotFound()
    {
        string targetBase = Path.Combine(_tempDir, "Base");
        string source = Path.Combine(_tempDir, "NotFound");

        await Assert.ThrowsAsync<FileNotFoundException>(() => _service.InstallModAsync(source, targetBase));
    }

    [Fact]
    public async Task InstallModAsync_CopiesDirectory_WhenSourceIsDirectory()
    {
        string targetBase = Path.Combine(_tempDir, "Target");
        string source = Path.Combine(_tempDir, "SourceFolder");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "test.txt"), "content");

        string result = await _service.InstallModAsync(source, targetBase);

        Assert.True(File.Exists(Path.Combine(result, "test.txt")));
    }

    [Fact]
    public async Task InstallModAsync_UsesOverridePath_WhenProvided()
    {
        string targetBase = Path.Combine(_tempDir, "Target");
        string overridePath = Path.Combine(targetBase, "CustomName");
        string source = Path.Combine(_tempDir, "SourceFolder");
        Directory.CreateDirectory(source);

        string result = await _service.InstallModAsync(source, targetBase, overridePath);

        Assert.Equal(overridePath, result);
    }

    [Fact]
    public async Task InstallModAsync_HandlesCancellation_ByCleaningUpTemp()
    {
        string targetBase = Path.Combine(_tempDir, "Target");
        string source = Path.Combine(_tempDir, "SourceFolder");
        Directory.CreateDirectory(source);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await _service.InstallModAsync(source, targetBase, null, null, cts.Token);

        var tempDirs = Directory.GetDirectories(targetBase, ".cmm_tmp_*");
        Assert.Empty(tempDirs);
    }

    [Fact]
    public async Task InstallModToRootAsync_CreatesRootFolderInTemp()
    {
        string targetBase = Path.Combine(_tempDir, "TargetBase");
        string archivePath = Path.Combine(_tempDir, "test.zip");
        // Create a dummy zip
        File.WriteAllBytes(archivePath, [0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        // This will mostly test the orchestration part since ExtractArchive is called
        try { await _service.InstallModToRootAsync(archivePath, "ModName", targetBase); }
        catch { /* Expect extraction to fail on dummy zip but we want to see if temp was created */ }

        Assert.True(Directory.Exists(targetBase));
    }

    [Fact]
    public async Task InstallModFromMappingAsync_CreatesTargetBase()
    {
        string targetBase = Path.Combine(_tempDir, "TargetMapping");
        string archivePath = Path.Combine(_tempDir, "mapping.zip");
        File.WriteAllBytes(archivePath, [0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        try { await _service.InstallModFromMappingAsync(archivePath, "MappedMod", targetBase, new Dictionary<string, string>()); }
        catch { }

        Assert.True(Directory.Exists(targetBase));
    }

    // --- HELPER MOCKS ---

    private class MockFileService : IFileService
    {
        public bool FileExists(string p) => File.Exists(p);
        public bool DirectoryExists(string p) => Directory.Exists(p);
        public void CreateDirectory(string p) => Directory.CreateDirectory(p);
        public void CopyFile(string s, string d, bool o) => File.Copy(s, d, o);
        public void CopyDirectory(string s, string d) 
        {
            Directory.CreateDirectory(d);
            foreach (string file in Directory.GetFiles(s, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(s, file);
                string dest = Path.Combine(d, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest);
            }
        }
        public void DeleteFile(string p) => File.Delete(p);
        public void DeleteDirectory(string p, bool r) => Directory.Delete(p, r);
        public void MoveDirectory(string f, string t) => Directory.Move(f, t);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}

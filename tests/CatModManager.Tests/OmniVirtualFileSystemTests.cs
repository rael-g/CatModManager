using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Vfs;

namespace CatModManager.Tests;

public class OmniVirtualFileSystemTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _mountPoint;
    private readonly string _baseFolder;
    private readonly ILogService _logService;

    public OmniVirtualFileSystemTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "VfsTests_" + Guid.NewGuid().ToString("N"));
        _mountPoint = Path.Combine(_tempDir, "Mount");
        _baseFolder = Path.Combine(_tempDir, "Base");
        Directory.CreateDirectory(_mountPoint);
        Directory.CreateDirectory(_baseFolder);
        _logService = new LogService();
    }

    [Fact]
    public void Vfs_Mount_And_Unmount_Flow()
    {
        var resolver = new SimpleConflictResolver(_logService);
        var vfs = new CatVirtualFileSystem(resolver, new MockDriver());
        var mods = new List<Mod>();

        vfs.Mount(_mountPoint, mods, null);
        Assert.True(vfs.IsMounted);

        vfs.Unmount();
        Assert.False(vfs.IsMounted);
    }

    [Fact]
    public void GetInfo_Returns_Correct_Nodes()
    {
        var resolver = new SimpleConflictResolver(_logService);
        var vfs = new CatVirtualFileSystem(resolver, new MockDriver());
        
        // Simula arquivos no resolver
        File.WriteAllText(Path.Combine(_baseFolder, "test.txt"), "hello");
        vfs.Mount(_mountPoint, new List<Mod>(), _baseFolder);

        var info = vfs.GetInfo("test.txt");
        Assert.NotNull(info);
        Assert.False(info.IsDirectory);
    }

    private class MockDriver : CatModManager.VirtualFileSystem.IFileSystemDriver
    {
        public bool IsMounted { get; private set; }
        public void Mount(string m, CatModManager.VirtualFileSystem.IFileSystem fs) => IsMounted = true;
        public void Unmount() => IsMounted = false;
        public void Dispose() { }
    }

    public void Dispose() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
}

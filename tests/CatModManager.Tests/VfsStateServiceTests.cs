using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using CatModManager.Core.Services;

namespace CatModManager.Tests;

public class VfsStateServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ICatPathService _pathService;
    private readonly ILogService _logService;

    public VfsStateServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _logService = new LogService();
        _pathService = new CatPathService(); 
    }

    [Fact]
    public void RegisterMount_SavesState()
    {
        var service = new VfsStateService(new AppDatabase(_pathService), _logService);
        string original = Path.Combine(_tempDir, "orig");
        string backup = Path.Combine(_tempDir, "back");
        
        service.RegisterMount(original, backup);
        
        var service2 = new VfsStateService(new AppDatabase(_pathService), _logService);
        service2.RecoverStaleMounts(); 
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using CatModManager.Core.Services;
using CatModManager.Core.Models;

namespace CatModManager.Tests;

public class CoverageInfrastructureTest : IDisposable
{
    private readonly string _tempDir;
    public CoverageInfrastructureTest() { _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(_tempDir); }

    [Fact]
    public void SimpleConflictResolver_Basic_Coverage()
    {
        var resolver = new SimpleConflictResolver(new LogService());
        var mods = new List<Mod> { new Mod("Test", "Path", 1) };
        var result = resolver.ResolveConflicts(mods, _tempDir);
        Assert.NotNull(result);
    }

    public void Dispose() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
}

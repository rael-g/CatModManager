using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;

namespace CatModManager.Tests;

public class SimpleConflictResolverTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ILogService _logService;

    public SimpleConflictResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ResolverTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _logService = new LogService();
    }

    [Fact]
    public void ResolveConflicts_Mods_Override_Base()
    {
        var resolver = new SimpleConflictResolver(_logService);
        string baseDir = Path.Combine(_tempDir, "Base");
        string modDir = Path.Combine(_tempDir, "Mod1");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(modDir);

        File.WriteAllText(Path.Combine(baseDir, "file.txt"), "base");
        File.WriteAllText(Path.Combine(modDir, "file.txt"), "mod");

        var mods = new List<Mod> { new Mod("Mod1", modDir, 1) };
        var result = resolver.ResolveConflicts(mods, baseDir);

        Assert.True(result.ContainsKey("file.txt"));
        // O PhysicalFileSource não guarda o conteúdo, mas o path deve ser do mod
        var source = result["file.txt"] as PhysicalFileSource;
        Assert.Contains("Mod1", source!.FilePath);
    }

    public void Dispose() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
}

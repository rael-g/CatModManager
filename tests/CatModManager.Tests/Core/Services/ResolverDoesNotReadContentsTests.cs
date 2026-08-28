using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.PluginSdk;
using CatModManager.Tests.Support;

namespace CatModManager.Tests.Core.Services;

/// <summary>
/// Resolving a mount must map files, not load them.
///
/// It used to read every single one into memory as it went, which made mounting cost a full read of
/// the whole mod list — a minute and gigabytes of RAM for a 2 GB Starfield setup. Worse, a file over
/// the 2 GB File.ReadAllBytes ceiling threw, and the catch around the directory enumeration
/// swallowed it: Starfield's own 4 GB .ba2 archives silently took the rest of that directory's scan
/// down with them.
/// </summary>
public class ResolverDoesNotReadContentsTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "ResolverNoRead_" + Guid.NewGuid().ToString("N"));

    public ResolverDoesNotReadContentsTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public void AFileLargerThanTwoGigabytesIsMappedAndDoesNotStopTheScan()
    {
        string baseDir = Path.Combine(_tempDir, "Base");
        Directory.CreateDirectory(baseDir);

        // Sparse, so this costs no disk: only the length matters. Named to sort before the small
        // files, so a scan that dies on it would take them with it.
        string huge = Path.Combine(baseDir, "0-huge.ba2");
        using (var fs = new FileStream(huge, FileMode.CreateNew, FileAccess.Write))
            fs.SetLength(3L * 1024 * 1024 * 1024);

        File.WriteAllText(Path.Combine(baseDir, "1-after.esm"), "still here");

        var resolver = new SimpleConflictResolver(new MockLogService(), new SevenZipArchiveExtractor());
        var map = resolver.ResolveConflicts(new List<Mod>(), baseDir);

        Assert.True(map.ContainsKey("0-huge.ba2"), "The oversized archive was dropped from the map.");
        Assert.Equal(3L * 1024 * 1024 * 1024, map["0-huge.ba2"].Length);
        Assert.True(map.ContainsKey("1-after.esm"), "The scan stopped at the oversized archive.");
    }

    [Fact]
    public void ResolvingDoesNotPullFileContentsIntoMemory()
    {
        string modDir = Path.Combine(_tempDir, "BigMod");
        Directory.CreateDirectory(modDir);

        // Not sparse — these have to be real bytes, or "we never read them" proves nothing.
        var payload = new byte[8 * 1024 * 1024];
        Random.Shared.NextBytes(payload);
        for (int i = 0; i < 24; i++)
            File.WriteAllBytes(Path.Combine(modDir, $"tex{i}.dds"), payload);

        var resolver = new SimpleConflictResolver(new MockLogService(), new SevenZipArchiveExtractor());
        var mods = new List<Mod> { new Mod("BigMod", modDir, 1) { IsEnabled = true } };

        GC.Collect();
        GC.WaitForPendingFinalizers();
        long before = GC.GetTotalMemory(forceFullCollection: true);

        var map = resolver.ResolveConflicts(mods, null);

        long grew = GC.GetTotalMemory(forceFullCollection: true) - before;

        Assert.Equal(24, map.Count);

        // 192 MB of files. Anything close to that means the contents came along for the ride;
        // the map itself is a few kilobytes of paths.
        Assert.True(grew < 32 * 1024 * 1024,
            $"Resolving 192 MB of mod files grew the heap by {grew / (1024 * 1024)} MB — contents are being read.");
    }

    [Fact]
    public void ContentsAreStillReadableAfterResolving()
    {
        string modDir = Path.Combine(_tempDir, "Mod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "readme.txt"), "hello");

        var resolver = new SimpleConflictResolver(new MockLogService(), new SevenZipArchiveExtractor());
        var mods = new List<Mod> { new Mod("Mod", modDir, 1) { IsEnabled = true } };

        var map = resolver.ResolveConflicts(mods, null);

        using var stream = map["readme.txt"].OpenRead();
        using var reader = new StreamReader(stream);
        Assert.Equal("hello", reader.ReadToEnd());
    }

    /// <summary>
    /// Files under the mount target keep an open descriptor, and reads through it must be correct
    /// regardless of where they start — FUSE asks for arbitrary offsets, from several threads.
    /// </summary>
    [Fact]
    public void PinnedSourcesReadCorrectlyFromAnOffset()
    {
        string baseDir = Path.Combine(_tempDir, "Game");
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(Path.Combine(baseDir, "data.bin"), "0123456789");

        var resolver = new SimpleConflictResolver(new MockLogService(), new SevenZipArchiveExtractor());
        var map = resolver.ResolveConflicts(new List<Mod>(), baseDir);

        var source = map["data.bin"];

        using var a = source.OpenRead();
        using var b = source.OpenRead();

        a.Seek(4, SeekOrigin.Begin);
        var bufA = new byte[3];
        Assert.Equal(3, a.Read(bufA, 0, 3));
        Assert.Equal("456", System.Text.Encoding.ASCII.GetString(bufA));

        // The second view must not have been dragged along by the first one's seek.
        var bufB = new byte[3];
        Assert.Equal(3, b.Read(bufB, 0, 3));
        Assert.Equal("012", System.Text.Encoding.ASCII.GetString(bufB));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}

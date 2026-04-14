using System;
using System.IO;
using CatModManager.PluginSdk;

namespace CatModManager.Core.Services;

/// <summary>
/// Source for a file that resides inside a compressed archive.
/// Uses IArchiveExtractor to access the file stream.
/// </summary>
public class ArchiveFileSource : IFileSource
{
    private readonly string _archivePath;
    private readonly string _entryPath;
    private readonly IArchiveExtractor _extractor;

    public string Name => Path.GetFileName(_entryPath);
    public long Length { get; }
    public DateTime LastWriteTime { get; }

    public ArchiveFileSource(string archivePath, string entryPath, IArchiveExtractor extractor)
    {
        _archivePath = archivePath;
        _entryPath = entryPath;
        _extractor = extractor;

        // Note: For full correctness, we should fetch Length/Date from extractor too.
        // For VFS performance, we usually cache these during the first scan.
        Length = 0; 
        LastWriteTime = DateTime.Now;
    }

    // Simplified constructor for existing code (using a default extractor if not provided)
    // In a real refactor, we should ensure DI provides this everywhere.
    public ArchiveFileSource(string archivePath, string entryPath) 
        : this(archivePath, entryPath, new SevenZipArchiveExtractor())
    {
    }

    public Stream OpenRead()
    {
        var stream = _extractor.OpenFileStream(_archivePath, _entryPath);
        if (stream == null) throw new FileNotFoundException($"Entry {_entryPath} not found in archive {_archivePath}");
        return stream;
    }
}

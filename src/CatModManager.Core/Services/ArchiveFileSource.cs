using System;
using System.IO;
using System.Linq;
using SharpCompress.Archives;

namespace CatModManager.Core.Services;

public class ArchiveFileSource : IFileSource
{
    private readonly string _archivePath;
    private readonly string _entryKey;
    private readonly long _length;
    private readonly DateTime _lastWriteTime;

    public ArchiveFileSource(string archivePath, string entryKey, long length, DateTime lastWriteTime)
    {
        _archivePath = archivePath;
        _entryKey = entryKey;
        _length = length;
        _lastWriteTime = lastWriteTime;
    }

    public long Length => _length;
    public DateTime LastWriteTime => _lastWriteTime;

    public Stream OpenRead()
    {
        var archive = ArchiveFactory.Open(_archivePath);
        var entry = archive.Entries.FirstOrDefault(e => e.Key == _entryKey);
        
        if (entry == null) 
        {
            archive.Dispose();
            throw new FileNotFoundException($"Entry {_entryKey} not found in archive {_archivePath}");
        }

        var ms = new MemoryStream();
        using (var entryStream = entry.OpenEntryStream())
        {
            entryStream.CopyTo(ms);
        }
        ms.Position = 0;
        archive.Dispose();
        return ms;
    }

    public void Dispose() { }
}




using System;
using System.IO;

namespace CatModManager.Core.Services;

public interface IFileSource
{
    long Length { get; }
    DateTime LastWriteTime { get; }
    Stream OpenRead();
}

public class PhysicalFileSource : IFileSource
{
    public string FilePath { get; }
    public long Length { get; }
    public DateTime LastWriteTime { get; }
    private readonly byte[] _data;

    public PhysicalFileSource(string filePath)
    {
        // Use the long path prefix to bypass Windows 260-character limit
        if (OperatingSystem.IsWindows())
            FilePath = filePath.StartsWith(@"\\?\") ? filePath : @"\\?\" + Path.GetFullPath(filePath);
        else
            FilePath = Path.GetFullPath(filePath);

        var info = new FileInfo(filePath);
        Length = info.Length;
        LastWriteTime = info.LastWriteTime;

        // Read fully into memory now, before this path can be shadowed by a later
        // FUSE mount. On Linux, unmodified base-game files are scanned from the very
        // directory the VFS then mounts over — a lazy re-open by path at FUSE-read
        // time would route back through that same mount, deadlocking (the handler
        // thread blocks waiting for a free handler thread to serve its own nested
        // open() call). Reading eagerly avoids ever touching the path again.
        _data = File.ReadAllBytes(FilePath);
    }

    public Stream OpenRead() => new MemoryStream(_data, writable: false);
}

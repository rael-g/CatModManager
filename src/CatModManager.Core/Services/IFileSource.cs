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

    public PhysicalFileSource(string filePath)
    {
        // Use the long path prefix to bypass Windows 260-character limit
        FilePath = filePath.StartsWith(@"\\?\") ? filePath : @"\\?\" + Path.GetFullPath(filePath);
        
        var info = new FileInfo(filePath);
        Length = info.Length;
        LastWriteTime = info.LastWriteTime;
    }

    public Stream OpenRead()
    {
        // Share delete allows the parent directory to be renamed even if handles are briefly held
        return new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }
}

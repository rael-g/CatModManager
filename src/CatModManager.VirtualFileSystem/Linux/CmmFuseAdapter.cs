using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Fuse.NETStandard;
using Mono.Unix.Native;

namespace CatModManager.VirtualFileSystem.Linux;

/// <summary>
/// Read-only FUSE filesystem that proxies reads to an <see cref="IFileSystem"/>.
/// </summary>
internal class CmmFuseAdapter : FileSystem
{
    private readonly IFileSystem _impl;

    public CmmFuseAdapter(IFileSystem impl) => _impl = impl;

    private static string ToRelative(string fusePath) => fusePath.TrimStart('/');

    protected override Errno OnGetPathStatus(string path, out Stat stat)
    {
        stat = new Stat();

        if (path == "/")
        {
            stat.st_mode = FilePermissions.S_IFDIR | NativeConvert.FromOctalPermissionString("0555");
            stat.st_nlink = 2;
            return 0;
        }

        var info = _impl.GetInfo(ToRelative(path));
        if (info == null) return Errno.ENOENT;

        stat.st_mode = info.IsDirectory
            ? FilePermissions.S_IFDIR | NativeConvert.FromOctalPermissionString("0555")
            : FilePermissions.S_IFREG | NativeConvert.FromOctalPermissionString("0444");
        stat.st_nlink = 1;
        stat.st_size = info.Size;
        stat.st_mtime = ((DateTimeOffset)info.LastWriteTime).ToUnixTimeSeconds();
        return 0;
    }

    protected override Errno OnReadDirectory(string path, OpenedPathInfo fi, out IEnumerable<DirectoryEntry> paths)
    {
        var rel = ToRelative(path);
        if (rel != "" && _impl.GetInfo(rel) == null)
        {
            paths = Array.Empty<DirectoryEntry>();
            return Errno.ENOENT;
        }

        var entries = new List<DirectoryEntry> { new("."), new("..") };
        entries.AddRange(_impl.ReadDirectory(rel).Select(e => new DirectoryEntry(e)));
        paths = entries;
        return 0;
    }

    protected override Errno OnOpenHandle(string path, OpenedPathInfo info)
    {
        if (info.OpenAccess != OpenFlags.O_RDONLY) return Errno.EACCES;
        return _impl.GetInfo(ToRelative(path)) == null ? Errno.ENOENT : 0;
    }

    protected override Errno OnReadHandle(string path, OpenedPathInfo info, byte[] buf, long offset, out int bytesWritten)
    {
        bytesWritten = 0;
        using var stream = _impl.OpenFile(ToRelative(path));
        if (stream == null) return Errno.ENOENT;

        stream.Seek(offset, SeekOrigin.Begin);
        bytesWritten = stream.Read(buf, 0, buf.Length);
        return 0;
    }
}

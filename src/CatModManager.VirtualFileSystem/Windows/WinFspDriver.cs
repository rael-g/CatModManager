using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Fsp;

namespace CatModManager.VirtualFileSystem.Windows;

public class WinFspDriver : IFileSystemDriver
{
    private INativeHost? _host;
    private readonly INativeHostFactory _factory;
    private bool _isMounted;

    public bool IsMounted => _isMounted;

    public WinFspDriver() : this(new WinFspNativeHostFactory()) { }

    internal WinFspDriver(INativeHostFactory factory)
    {
        _factory = factory;
    }

    public void Mount(string mountPoint, IFileSystem fileSystem)
    {
        if (_isMounted) return;

        // Ensure single-instance cleanup before attempting a new mount
        Unmount();

        var nativeFs = new WinFspFileSystemProxy(fileSystem);
        _host = _factory.CreateHost(nativeFs);
        
        // Generate a unique name to prevent 0xC0000035 (Object Name Collision) at the kernel level
        _host.FileSystemName = $"CatMM-{Guid.NewGuid().ToString("N").Substring(0, 6)}";

        string cleaned = mountPoint.TrimEnd(Path.DirectorySeparatorChar);
        int result = _host.Mount(cleaned);
        
        if (result < 0) throw new Exception($"WinFsp Mount failed: 0x{result:X8}");

        _isMounted = true;
    }

    public void Unmount()
    {
        if (_host != null)
        {
            try { _host.Unmount(); _host.Dispose(); } catch { }
            _host = null;
        }
        _isMounted = false;
    }

    public void Dispose() => Unmount();

    private class WinFspNativeHost : INativeHost
    {
        private readonly FileSystemHost _host;
        public WinFspNativeHost(FileSystemBase fs) => _host = new FileSystemHost(fs);
        public string FileSystemName { get => _host.FileSystemName; set => _host.FileSystemName = value; }
        public int Mount(string mountPoint) => _host.Mount(mountPoint, null, true, 0);
        public void Unmount() => _host.Unmount();
        public void Dispose() => _host.Dispose();
    }

    private class WinFspNativeHostFactory : INativeHostFactory
    {
        public INativeHost CreateHost(object fileSystem) => new WinFspNativeHost((FileSystemBase)fileSystem);
    }

    private class WinFspFileSystemProxy : FileSystemBase
    {
        private readonly IFileSystem _impl;
        public WinFspFileSystemProxy(IFileSystem impl) => _impl = impl;

        private class FileContext
        {
            public string Path { get; init; } = "";
            public Stream? Stream { get; set; }
            public List<string>? DirectoryEntries { get; set; }
        }

        public override int GetSecurityByName(string fileName, out uint fileAttributes, ref byte[] securityDescriptor)
        {
            var info = _impl.GetInfo(fileName.TrimStart('\\'));
            if (info == null) { fileAttributes = 0; return STATUS_OBJECT_NAME_NOT_FOUND; }
            fileAttributes = info.IsDirectory ? (uint)FileAttributes.Directory : (uint)FileAttributes.Normal;
            return STATUS_SUCCESS;
        }

        public override int Open(string fileName, uint createOptions, uint grantedAccess, out object fileNode, out object fileDesc, out Fsp.Interop.FileInfo fileInfo, out string normalizedName)
        {
            string relPath = fileName.TrimStart('\\');
            fileNode = null!; fileDesc = null!; fileInfo = default; normalizedName = null!;
            var info = _impl.GetInfo(relPath);
            if (info == null) return STATUS_OBJECT_NAME_NOT_FOUND;
            var ctx = new FileContext { Path = relPath };
            if (!info.IsDirectory) { ctx.Stream = _impl.OpenFile(relPath); if (ctx.Stream == null) return STATUS_ACCESS_DENIED; }
            fileNode = ctx;
            return GetFileInfo(ctx, null!, out fileInfo);
        }

        public override int GetFileInfo(object fileNode, object fileDesc, out Fsp.Interop.FileInfo fileInfo)
        {
            var ctx = (FileContext)fileNode;
            fileInfo = default;
            var info = _impl.GetInfo(ctx.Path);
            if (info == null) return STATUS_OBJECT_NAME_NOT_FOUND;
            fileInfo.FileAttributes = info.IsDirectory ? (uint)FileAttributes.Directory : (uint)FileAttributes.Normal;
            fileInfo.FileSize = (ulong)info.Size;
            fileInfo.AllocationSize = (fileInfo.FileSize + 4095) & ~4095UL;
            fileInfo.CreationTime = (ulong)info.CreationTime.ToFileTimeUtc();
            fileInfo.LastAccessTime = (ulong)info.LastAccessTime.ToFileTimeUtc();
            fileInfo.LastWriteTime = (ulong)info.LastWriteTime.ToFileTimeUtc();
            fileInfo.ChangeTime = (ulong)info.LastWriteTime.ToFileTimeUtc();
            return STATUS_SUCCESS;
        }

        public override bool ReadDirectoryEntry(object fileNode, object fileDesc, string pattern, string marker, ref object context, out string fileName, out Fsp.Interop.FileInfo fileInfo)
        {
            var ctx = (FileContext)fileNode;
            fileName = null!; fileInfo = default;
            if (ctx.DirectoryEntries == null) { ctx.DirectoryEntries = new List<string> { ".", ".." }; ctx.DirectoryEntries.AddRange(_impl.ReadDirectory(ctx.Path)); }
            int index;
            if (context == null)
            {
                if (marker != null)
                {
                    int markerIdx = ctx.DirectoryEntries.FindIndex(e => string.Equals(e, marker, StringComparison.OrdinalIgnoreCase));
                    index = markerIdx >= 0 ? markerIdx + 1 : 0;
                }
                else { index = 0; }
            }
            else { index = (int)context; }
            if (index < ctx.DirectoryEntries.Count)
            {
                fileName = ctx.DirectoryEntries[index];
                if (fileName == "." || fileName == "..") { fileInfo.FileAttributes = (uint)FileAttributes.Directory; }
                else
                {
                    string subPath = string.IsNullOrEmpty(ctx.Path) ? fileName : Path.Combine(ctx.Path, fileName);
                    var info = _impl.GetInfo(subPath);
                    if (info != null) { fileInfo.FileAttributes = info.IsDirectory ? (uint)FileAttributes.Directory : (uint)FileAttributes.Normal; fileInfo.FileSize = (ulong)info.Size; fileInfo.LastWriteTime = (ulong)info.LastWriteTime.ToFileTimeUtc(); }
                }
                context = index + 1;
                return true;
            }
            return false;
        }

        public override int Read(object fileNode, object fileDesc, IntPtr buffer, ulong offset, uint length, out uint bytesTransferred)
        {
            var ctx = (FileContext)fileNode;
            bytesTransferred = 0;
            if (ctx.Stream == null) return STATUS_ACCESS_DENIED;
            try { ctx.Stream.Seek((long)offset, SeekOrigin.Begin); byte[] managed = new byte[length]; int read = ctx.Stream.Read(managed, 0, (int)length); Marshal.Copy(managed, 0, buffer, read); bytesTransferred = (uint)read; return STATUS_SUCCESS; }
            catch { return STATUS_ACCESS_DENIED; }
        }

        public override void Cleanup(object fileNode, object fileDesc, string fileName, uint flags) { var ctx = (FileContext)fileNode; ctx.Stream?.Dispose(); ctx.Stream = null; }
        public override void Close(object fileNode, object fileDesc) { var ctx = (FileContext)fileNode; ctx.Stream?.Dispose(); ctx.Stream = null; }
    }
}

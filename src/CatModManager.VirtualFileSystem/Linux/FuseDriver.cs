using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

#if LINUX
using LTRData.FuseDotNet;
#endif

namespace CatModManager.VirtualFileSystem.Linux;

public class FuseDriver : IFileSystemDriver
{
    private INativeFuseHost? _host;
    private readonly INativeFuseHostFactory _factory;
    private bool _isMounted;

    public bool IsMounted => _isMounted;

    public FuseDriver() : this(new FuseNativeHostFactory()) { }

    internal FuseDriver(INativeFuseHostFactory factory)
    {
        _factory = factory;
    }

    public void Mount(string mountPoint, IFileSystem fileSystem)
    {
        if (_isMounted) return;

        var nativeFs = new FuseFileSystemProxy(fileSystem);
        _host = _factory.CreateHost(nativeFs);
        
        var options = new[] { "-o", "ro", "-o", "allow_other" };
        
        try
        {
            _host.Mount(mountPoint, options);
            _isMounted = true;
        }
        catch
        {
            _host.Dispose();
            _host = null;
            throw;
        }
    }

    public void Unmount()
    {
        if (!_isMounted) return;
        _host?.Unmount();
        _host?.Dispose();
        _host = null;
        _isMounted = false;
    }

    public void Dispose() => Unmount();

    private class FuseNativeHost : INativeFuseHost
    {
#if LINUX
        private FuseMount? _mount;
        private readonly FuseFileSystemBase _fs;
        public FuseNativeHost(FuseFileSystemBase fs) => _fs = fs;

        public void Mount(string mountPoint, string[] options)
        {
            _mount = FuseMount.Mount(mountPoint, _fs, options);
        }

        public void Unmount() => _mount?.Dispose();
        public void Dispose() => _mount?.Dispose();
#else
        public FuseNativeHost(object fs) { }
        public void Mount(string mountPoint, string[] options) => throw new PlatformNotSupportedException("FUSE is only available on Linux.");
        public void Unmount() { }
        public void Dispose() { }
#endif
    }

    private class FuseNativeHostFactory : INativeFuseHostFactory
    {
        public INativeFuseHost CreateHost(object fileSystem) 
        {
#if LINUX
            return new FuseNativeHost((FuseFileSystemBase)fileSystem);
#else
            return new FuseNativeHost(fileSystem);
#endif
        }
    }

#if LINUX
    private class FuseFileSystemProxy : FuseFileSystemBase
    {
        private readonly IFileSystem _impl;
        public FuseFileSystemProxy(IFileSystem impl) => _impl = impl;

        public override int GetAttr(string path, ref FuseFileInfo stat)
        {
            var info = _impl.GetInfo(path.TrimStart('/'));
            if (info == null) return -2; // ENOENT

            stat.Mode = info.IsDirectory ? FileMode.Directory | (FileMode)0555 : FileMode.Regular | (FileMode)0444;
            stat.Size = info.Size;
            stat.Mtime = info.LastWriteTime;
            return 0;
        }

        public override int ReadDir(string path, nint buf, Filler filler, long offset, ref FuseFileInfo info)
        {
            filler(buf, ".", ref info, 0);
            filler(buf, "..", ref info, 0);
            foreach (var entry in _impl.ReadDirectory(path.TrimStart('/')))
            {
                filler(buf, entry, ref info, 0);
            }
            return 0;
        }

        public override int Open(string path, ref FuseFileInfo info) => 0;

        public override int Read(string path, nint buf, ulong size, long offset, ref FuseFileInfo info)
        {
            using var stream = _impl.OpenFile(path.TrimStart('/'));
            if (stream == null) return -2;
            
            stream.Seek(offset, SeekOrigin.Begin);
            byte[] buffer = new byte[size];
            int read = stream.Read(buffer, 0, (int)size);
            Marshal.Copy(buffer, 0, buf, read);
            return read;
        }
    }
#else
    private class FuseFileSystemProxy
    {
        public FuseFileSystemProxy(IFileSystem impl) { }
    }
#endif
}




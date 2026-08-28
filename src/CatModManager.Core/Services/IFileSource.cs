using System;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace CatModManager.Core.Services;

public interface IFileSource
{
    long Length { get; }
    DateTime LastWriteTime { get; }
    Stream OpenRead();
}

public class PhysicalFileSource : IFileSource, IDisposable
{
    public string FilePath { get; }
    public long Length { get; }
    public DateTime LastWriteTime { get; }

    /// <summary>
    /// An open descriptor kept for files that a later FUSE mount will shadow, or null when the
    /// file can simply be re-opened by path.
    /// </summary>
    private readonly SafeFileHandle? _pinned;

    /// <param name="pinHandle">
    /// True when this file lives under the directory the VFS is about to mount over.
    ///
    /// Once the overlay is up, that path resolves back through the mount, so a FUSE handler
    /// re-opening it by path blocks waiting for a handler thread to serve its own nested open() —
    /// the whole filesystem deadlocks. Holding a descriptor opened *before* the mount sidesteps
    /// that: the inode is already resolved and reads never consult the path again.
    ///
    /// This used to be solved by reading every file into memory in this constructor. That is what
    /// made mounting take a minute and hold gigabytes: a 2 GB mod list was 2 GB read off disk and
    /// held in RAM on every mount, and Starfield's own .ba2 files — over the 2 GB
    /// <see cref="File.ReadAllBytes"/> ceiling — threw, which the resolver's catch swallowed along
    /// with the rest of that directory's scan. A descriptor costs nothing and has neither limit.
    /// </param>
    public PhysicalFileSource(string filePath, bool pinHandle = false)
    {
        // Use the long path prefix to bypass Windows 260-character limit
        if (OperatingSystem.IsWindows())
            FilePath = filePath.StartsWith(@"\\?\") ? filePath : @"\\?\" + Path.GetFullPath(filePath);
        else
            FilePath = Path.GetFullPath(filePath);

        var info = new FileInfo(filePath);
        Length = info.Length;
        LastWriteTime = info.LastWriteTime;

        if (pinHandle)
        {
            // A game folder with more files than the process may hold descriptors would otherwise
            // fail the whole mount. Re-opening by path is only wrong under FUSE, and a hard-link
            // deployment never reads through this at all, so degrading is better than refusing.
            // FileShare.Delete is not optional. Every file pinned here sits under the mount point,
            // which is exactly the set of files the hard-link driver is about to rename aside or
            // delete — and Windows refuses both while a handle is open without it. Without Delete
            // the app locks itself out of its own game folder: the mount fails with "could not set
            // aside the existing file", and the unmount silently fails to remove what it deployed,
            // which is how mod files end up living in the game folder permanently.
            try { _pinned = File.OpenHandle(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
            catch { _pinned = null; }
        }
    }

    public Stream OpenRead() => _pinned != null
        ? new PinnedHandleStream(_pinned, Length)
        : new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public void Dispose() => _pinned?.Dispose();

    /// <summary>
    /// A read-only view over a shared descriptor, with its own position. FUSE serves reads from
    /// several threads at once, so seeking a shared <see cref="FileStream"/> is not an option.
    /// </summary>
    private sealed class PinnedHandleStream : Stream
    {
        private readonly SafeFileHandle _handle;
        private long _position;

        public PinnedHandleStream(SafeFileHandle handle, long length)
        {
            _handle = handle;
            Length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length { get; }

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int read = RandomAccess.Read(_handle, buffer, _position);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin   => offset,
                SeekOrigin.Current => _position + offset,
                _                  => Length + offset,
            };
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        // The descriptor belongs to the source, which outlives every stream handed out over it.
        protected override void Dispose(bool disposing) { }
    }
}

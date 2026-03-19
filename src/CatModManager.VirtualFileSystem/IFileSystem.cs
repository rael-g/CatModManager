using System;
using System.Collections.Generic;
using System.IO;

namespace CatModManager.VirtualFileSystem;

/// <summary>
/// Informações básicas de um nó (arquivo ou pasta).
/// </summary>
public record FileSystemNodeInfo
{
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime CreationTime { get; init; } = DateTime.Now;
    public DateTime LastAccessTime { get; init; } = DateTime.Now;
    public DateTime LastWriteTime { get; init; } = DateTime.Now;
}

/// <summary>
/// Interface genérica para implementação da lógica de um sistema de arquivos.
/// </summary>
public interface IFileSystem
{
    FileSystemNodeInfo? GetInfo(string path);
    IEnumerable<string> ReadDirectory(string path);
    Stream? OpenFile(string path);
}

/// <summary>
/// Interface para o driver de montagem do sistema de arquivos.
/// </summary>
public interface IFileSystemDriver : IDisposable
{
    void Mount(string mountPoint, IFileSystem fileSystem);
    void Unmount();
    bool IsMounted { get; }
}




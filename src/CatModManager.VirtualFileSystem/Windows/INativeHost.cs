using System;

namespace CatModManager.VirtualFileSystem.Windows;

/// <summary>
/// Interface interna para abstrair o FileSystemHost do WinFsp.
/// </summary>
internal interface INativeHost : IDisposable
{
    string FileSystemName { get; set; }
    int Mount(string mountPoint);
    void Unmount();
}

internal interface INativeHostFactory
{
    INativeHost CreateHost(object fileSystem);
}




using System;

namespace CatModManager.VirtualFileSystem.Linux;

/// <summary>
/// Interface interna para abstrair o Host do FUSE (LTRData.FuseDotNet).
/// </summary>
internal interface INativeFuseHost : IDisposable
{
    void Mount(string mountPoint, string[] options);
    void Unmount();
}

internal interface INativeFuseHostFactory
{
    INativeFuseHost CreateHost(object fileSystem);
}




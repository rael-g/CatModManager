using System;
using System.Collections.Generic;
using CatModManager.Core.Models;

namespace CatModManager.Core.Vfs;

public interface IVirtualFileSystem : IDisposable
{
    void Mount(string mountPoint, List<Mod> activeMods, string? baseFolderPath, string? dataSubFolder = null);
    void Unmount();
    bool IsMounted { get; }
    event EventHandler<string>? ErrorOccurred;
}



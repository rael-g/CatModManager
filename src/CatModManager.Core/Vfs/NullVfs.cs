using System;
using System.Collections.Generic;
using CatModManager.Core.Models;

namespace CatModManager.Core.Vfs;

public class NullVfs : IVirtualFileSystem
{
    public bool IsMounted => false;
    public event EventHandler<string>? ErrorOccurred;
    public void Mount(string gameFolderPath, List<Mod> activeMods, string? dataSubFolder = null) { }
    public void Unmount() { }
    public void Dispose() { }
}



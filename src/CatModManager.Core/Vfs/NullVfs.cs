using System;
using System.Collections.Generic;
using CatModManager.Core.Models;

namespace CatModManager.Core.Vfs;

public class NullVfs : IVirtualFileSystem
{
    public bool IsMounted => false;
    public event EventHandler<string>? ErrorOccurred;
    public void Mount(string m, List<Mod> a, string? b, string? d = null) { }
    public void Unmount() { }
    public void Dispose() { }
}



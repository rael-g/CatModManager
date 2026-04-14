using System;
using System.Collections.Generic;
using CatModManager.Core.Models;

namespace CatModManager.Core.Vfs;

public interface IVirtualFileSystem : IDisposable
{
    /// <param name="gameFolderPath">Real game root — never modified by callers.</param>
    /// <param name="activeMods">Mods to deploy, ordered highest-priority first.</param>
    void Mount(string gameFolderPath, List<Mod> activeMods);
    void Unmount();
    bool IsMounted { get; }
    event EventHandler<string>? ErrorOccurred;
}

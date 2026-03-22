using System;
using System.Collections.Generic;
using CatModManager.Core.Models;

namespace CatModManager.Core.Vfs;

public interface IVirtualFileSystem : IDisposable
{
    /// <param name="gameFolderPath">Real game root — never modified by callers.</param>
    /// <param name="activeMods">Mods to deploy, ordered highest-priority first.</param>
    /// <param name="dataSubFolder">
    ///   Optional sub-folder inside the game root where the VFS or hard links should
    ///   operate. Null means operate at the game root itself.
    /// </param>
    void Mount(string gameFolderPath, List<Mod> activeMods, string? dataSubFolder = null);
    void Unmount();
    bool IsMounted { get; }
    event EventHandler<string>? ErrorOccurred;
}

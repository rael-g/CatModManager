using System;
using System.Collections.Generic;
using Xunit;
using CatModManager.Core.Vfs;
using CatModManager.Core.Models;

namespace CatModManager.Tests.Core.Vfs;

public class VfsImplementationsTests
{
    [Fact]
    public void NullVfs_Coverage()
    {
        var vfs = new NullVfs();
        Assert.False(vfs.IsMounted);

        vfs.Mount("V:", new List<Mod>());
        vfs.Mount("V:", new List<Mod>());

        vfs.Unmount();
        vfs.Dispose();
    }
}



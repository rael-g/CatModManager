using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Xunit;
using CatModManager.VirtualFileSystem;
using CatModManager.Core.Services;

namespace CatModManager.Tests;

public class FileSystemFactoryTests
{
    [Fact]
    public void CreateDriver_ReturnsNonNullDriver_OnSupportedPlatforms()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var store  = new NullHardlinkStateStore();
            var driver = FileSystemFactory.CreateDriver(store);
            Assert.NotNull(driver);
        }
    }

    private sealed class NullHardlinkStateStore : IHardlinkStateStore
    {
        public void Save(string mountPoint, IReadOnlyList<HardlinkStateEntry> entries) { }
        public IReadOnlyList<HardlinkStateEntry> Load(string? mountPoint) => Array.Empty<HardlinkStateEntry>();
        public void Clear(string? mountPoint) { }
    }
}

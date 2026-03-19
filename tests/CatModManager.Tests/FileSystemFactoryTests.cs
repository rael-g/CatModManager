using System;
using System.Runtime.InteropServices;
using Xunit;
using CatModManager.VirtualFileSystem;

namespace CatModManager.Tests;

public class FileSystemFactoryTests
{
    [Fact]
    public void CreateDriver_ReturnsNonNullDriver_OnSupportedPlatforms()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var driver = FileSystemFactory.CreateDriver();
            Assert.NotNull(driver);
        }
    }
}




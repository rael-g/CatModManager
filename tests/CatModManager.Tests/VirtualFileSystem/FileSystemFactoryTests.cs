using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Xunit;
using CatModManager.VirtualFileSystem;
using CatModManager.Core.Services;

namespace CatModManager.Tests.VirtualFileSystem;

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

    [Fact]
    public void CreateDriver_UsesFuseOverlay_OnAnOrdinaryLinuxFilesystem()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;

        // The temp dir is on a normal Linux filesystem, so the overlay is available and
        // preferred — it leaves the game folder untouched.
        var driver = FileSystemFactory.CreateDriver(
            new NullHardlinkStateStore(), System.IO.Path.GetTempPath());

        Assert.IsType<CatModManager.VirtualFileSystem.Linux.FuseDriver>(driver);
    }

    [Theory]
    [InlineData("ext4",  true)]
    [InlineData("btrfs", true)]
    [InlineData("xfs",   true)]
    [InlineData("ntfs",  false)]
    [InlineData("ntfs3", false)]
    public void FuseOverlay_IsUnavailableOnFilesystemsFusermountRefuses(string fsType, bool expected)
    {
        // fusermount rejects the mount itself ("mounting over filesystem type 0x7366746e is
        // forbidden"), before CMM gets any say — so the driver has to be chosen up front.
        Assert.Equal(expected, FileSystemFactory.IsFuseMountableFilesystem(fsType));
    }

    [Fact]
    public void FuseOverlay_IsAssumedAvailable_WhenTheTargetIsUnknown()
    {
        // Null target means crash recovery, which inspects state instead of mounting.
        Assert.True(FileSystemFactory.SupportsFuseOverlay(null));
    }

    private sealed class NullHardlinkStateStore : IHardlinkStateStore
    {
        public void Save(string mountPoint, IReadOnlyList<HardlinkStateEntry> entries) { }
        public IReadOnlyList<HardlinkStateEntry> Load(string? mountPoint) => Array.Empty<HardlinkStateEntry>();
        public void Clear(string? mountPoint) { }
    }
}

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
    public void CreateCrashRecoveryDriver_AlwaysCleansHardlinks()
    {
        // Crash recovery reverts deployed hard links from their persisted state. Picking a driver
        // by platform used to hand back a FuseDriver on Linux, whose Unmount() returns immediately
        // when nothing was mounted — so a crash left mod files and backups in the game folder
        // forever. Orphaned FUSE mounts are recovered separately, from /proc/mounts.
        var driver = FileSystemFactory.CreateCrashRecoveryDriver(new NullHardlinkStateStore());

        Assert.IsType<HardlinkDriver>(driver);
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

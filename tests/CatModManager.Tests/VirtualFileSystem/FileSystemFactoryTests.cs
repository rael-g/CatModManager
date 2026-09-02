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
    public void CreateDriver_ReturnsHardlinks_OnEverySupportedPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux used to get a FUSE overlay with a hard link fallback. Now every platform gets
            // the same driver, so the target path no longer influences the choice.
            var store = new NullHardlinkStateStore();

            Assert.IsType<HardlinkDriver>(FileSystemFactory.CreateDriver(store));
            Assert.IsType<HardlinkDriver>(FileSystemFactory.CreateDriver(store, "/anywhere"));
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

    [Fact]
    public void DescribeFilesystem_NamesTheFilesystem_ForDiagnostics()
    {
        // Survives the FUSE driver because a cross-device hard link failure is otherwise reported
        // as a bare "Invalid cross-device link", with no hint of which filesystems were involved.
        Assert.Equal("an unknown filesystem", FileSystemFactory.DescribeFilesystem(null));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Assert.NotEqual("an unknown filesystem", FileSystemFactory.DescribeFilesystem("/"));
    }

    private sealed class NullHardlinkStateStore : IHardlinkStateStore
    {
        public void Save(string mountPoint, IReadOnlyList<HardlinkStateEntry> entries) { }
        public IReadOnlyList<HardlinkStateEntry> Load(string? mountPoint) => Array.Empty<HardlinkStateEntry>();
        public void Clear(string? mountPoint) { }
    }
}

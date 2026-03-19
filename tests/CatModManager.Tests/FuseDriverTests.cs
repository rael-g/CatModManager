using System;
using Xunit;
using CatModManager.VirtualFileSystem;
using CatModManager.VirtualFileSystem.Linux;

namespace CatModManager.Tests;

public class FuseDriverTests
{
    private class MockNativeFuseHost : INativeFuseHost
    {
        public bool IsMounted { get; private set; }
        public bool IsUnmounted { get; private set; }
        public bool ShouldFail { get; set; }
        public void Mount(string mountPoint, string[] options) { if (ShouldFail) throw new Exception(); IsMounted = true; }
        public void Unmount() { IsMounted = false; IsUnmounted = true; }
        public void Dispose() { }
    }

    private class MockNativeFuseHostFactory : INativeFuseHostFactory
    {
        public MockNativeFuseHost LastHost { get; private set; } = new();
        public INativeFuseHost CreateHost(object fileSystem) => LastHost;
    }

    private class MockFileSystem : IFileSystem
    {
        public FileSystemNodeInfo? GetInfo(string path) => null;
        public System.Collections.Generic.IEnumerable<string> ReadDirectory(string path) => Array.Empty<string>();
        public System.IO.Stream? OpenFile(string path) => null;
    }

    [Fact]
    public void FuseDriver_Mount_SetsIsMounted()
    {
        var factory = new MockNativeFuseHostFactory();
        var driver = new FuseDriver(factory);
        driver.Mount("/mnt/test", new MockFileSystem());
        Assert.True(driver.IsMounted);
    }

    [Fact]
    public void FuseDriver_Mount_AlreadyMounted_DoesNothing()
    {
        var factory = new MockNativeFuseHostFactory();
        var driver = new FuseDriver(factory);
        driver.Mount("/mnt/test", new MockFileSystem());
        driver.Mount("/mnt/test", new MockFileSystem());
        Assert.True(driver.IsMounted);
    }

    [Fact]
    public void FuseDriver_Mount_HandlesFailure()
    {
        var factory = new MockNativeFuseHostFactory();
        factory.LastHost.ShouldFail = true;
        var driver = new FuseDriver(factory);
        Assert.ThrowsAny<Exception>(() => driver.Mount("/mnt/test", new MockFileSystem()));
        Assert.False(driver.IsMounted);
    }

    [Fact]
    public void FuseDriver_Unmount_ResetsState()
    {
        var factory = new MockNativeFuseHostFactory();
        var driver = new FuseDriver(factory);
        driver.Mount("/mnt/test", new MockFileSystem());
        driver.Unmount();
        Assert.False(driver.IsMounted);
        Assert.True(factory.LastHost.IsUnmounted);
    }

    [Fact]
    public void FuseDriver_Dispose_Unmounts()
    {
        var factory = new MockNativeFuseHostFactory();
        var driver = new FuseDriver(factory);
        driver.Mount("/mnt/test", new MockFileSystem());
        driver.Dispose();
        Assert.False(driver.IsMounted);
    }
}




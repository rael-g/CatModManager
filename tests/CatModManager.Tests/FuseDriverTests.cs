using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using CatModManager.VirtualFileSystem;
using CatModManager.VirtualFileSystem.Linux;

namespace CatModManager.Tests;

/// <summary>
/// Unit tests for FuseDriver that run on all platforms by injecting a fake
/// INativeFuseHostFactory.  They verify the driver's lifecycle logic without
/// requiring a real FUSE installation.
/// </summary>
public class FuseDriverTests
{
    // ── doubles ───────────────────────────────────────────────────────────────

    private sealed class FakeNativeFuseHost : INativeFuseHost
    {
        public string?   LastMountPoint { get; private set; }
        public string[]? LastOptions    { get; private set; }
        public bool      Mounted        { get; private set; }
        public bool      Disposed       { get; private set; }
        public bool      ThrowOnMount   { get; set; }

        public void Mount(string mountPoint, string[] options)
        {
            if (ThrowOnMount) throw new InvalidOperationException("FUSE mount failed");
            LastMountPoint = mountPoint;
            LastOptions    = options;
            Mounted        = true;
        }

        public void Unmount() => Mounted = false;
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeHostFactory : INativeFuseHostFactory
    {
        public FakeNativeFuseHost? LastCreated { get; private set; }
        public bool ThrowOnMount { get; set; }

        public INativeFuseHost CreateHost(object fileSystem)
        {
            LastCreated = new FakeNativeFuseHost { ThrowOnMount = ThrowOnMount };
            return LastCreated;
        }
    }

    private sealed class StubFs : IFileSystem
    {
        public FileSystemNodeInfo? GetInfo(string path)        => null;
        public IEnumerable<string> ReadDirectory(string path)  => Array.Empty<string>();
        public Stream?             OpenFile(string path)        => null;
        public string?             GetPhysicalPath(string path) => null;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static (FuseDriver driver, FakeHostFactory factory) Make(bool throwOnMount = false)
    {
        var factory = new FakeHostFactory { ThrowOnMount = throwOnMount };
        return (new FuseDriver(factory), factory);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void IsMounted_IsFalse_BeforeMount()
    {
        var (driver, _) = Make();
        Assert.False(driver.IsMounted);
    }

    [Fact]
    public void Mount_CallsCreateHostAndMount()
    {
        var (driver, factory) = Make();
        driver.Mount("/game", new StubFs());

        Assert.NotNull(factory.LastCreated);
        Assert.True(factory.LastCreated!.Mounted);
        Assert.Equal("/game", factory.LastCreated.LastMountPoint);
    }

    [Fact]
    public void Mount_PassesReadOnlyAndAllowOtherOptions()
    {
        var (driver, factory) = Make();
        driver.Mount("/game", new StubFs());

        var opts = factory.LastCreated!.LastOptions!;
        Assert.Contains("-o", opts);
        Assert.Contains("ro", opts);
        Assert.Contains("allow_other", opts);
    }

    [Fact]
    public void IsMounted_IsTrue_AfterMount()
    {
        var (driver, _) = Make();
        driver.Mount("/game", new StubFs());
        Assert.True(driver.IsMounted);
    }

    [Fact]
    public void Mount_WhenAlreadyMounted_IsNoOp()
    {
        var (driver, factory) = Make();
        driver.Mount("/game", new StubFs());

        // Second mount should be a no-op — factory.CreateHost is NOT called again.
        var firstHost = factory.LastCreated;
        driver.Mount("/game2", new StubFs());

        Assert.Same(firstHost, factory.LastCreated);
        Assert.Equal("/game", firstHost!.LastMountPoint);
    }

    [Fact]
    public void Unmount_SetsIsMountedFalse()
    {
        var (driver, _) = Make();
        driver.Mount("/game", new StubFs());
        driver.Unmount();
        Assert.False(driver.IsMounted);
    }

    [Fact]
    public void Unmount_CallsHostUnmountAndDispose()
    {
        var (driver, factory) = Make();
        driver.Mount("/game", new StubFs());
        var host = factory.LastCreated!;

        driver.Unmount();

        Assert.False(host.Mounted);
        Assert.True(host.Disposed);
    }

    [Fact]
    public void Unmount_WhenNotMounted_IsNoOp()
    {
        var (driver, _) = Make();
        // Should not throw
        driver.Unmount();
        Assert.False(driver.IsMounted);
    }

    [Fact]
    public void Dispose_CallsUnmount()
    {
        var (driver, factory) = Make();
        driver.Mount("/game", new StubFs());
        var host = factory.LastCreated!;

        driver.Dispose();

        Assert.False(host.Mounted);
        Assert.True(host.Disposed);
        Assert.False(driver.IsMounted);
    }

    [Fact]
    public void Mount_OnException_SetsIsMountedFalseAndDisposesHost()
    {
        var (driver, factory) = Make(throwOnMount: true);

        Assert.Throws<InvalidOperationException>(() => driver.Mount("/game", new StubFs()));

        Assert.False(driver.IsMounted);
        Assert.True(factory.LastCreated?.Disposed ?? false);
    }

    [Fact]
    public void Mount_AfterFailedMount_CanRetry()
    {
        var factory = new FakeHostFactory { ThrowOnMount = true };
        var driver  = new FuseDriver(factory);

        Assert.Throws<InvalidOperationException>(() => driver.Mount("/game", new StubFs()));

        // Fix the factory and retry
        factory.ThrowOnMount = false;
        driver.Mount("/game", new StubFs());

        Assert.True(driver.IsMounted);
    }
}

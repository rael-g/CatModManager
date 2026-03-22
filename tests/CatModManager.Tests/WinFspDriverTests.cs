using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;
using CatModManager.VirtualFileSystem;
using CatModManager.VirtualFileSystem.Windows;

namespace CatModManager.Tests;

public class WinFspDriverTests
{
    private class MockNativeHost : INativeHost
    {
        public string FileSystemName { get; set; } = "";
        public int MountResult { get; set; } = 0;
        public bool IsUnmounted { get; private set; }
        public int Mount(string mountPoint) => MountResult;
        public void Unmount() => IsUnmounted = true;
        public void Dispose() { }
    }

    private class MockNativeHostFactory : INativeHostFactory
    {
        public MockNativeHost LastHost { get; private set; } = new();
        public object? LastFs { get; private set; }
        public INativeHost CreateHost(object fileSystem) { LastFs = fileSystem; return LastHost; }
    }

    private class MockFileSystem : IFileSystem
    {
        public FileSystemNodeInfo? GetInfo(string path) => 
            path == "test.txt" ? new FileSystemNodeInfo { IsDirectory = false, Size = 100, LastWriteTime = DateTime.Now } : 
            path == "dir" ? new FileSystemNodeInfo { IsDirectory = true } : null;
        public IEnumerable<string> ReadDirectory(string path) => new[] { "test.txt" };
        public Stream? OpenFile(string path) => new MemoryStream(new byte[100]);
        public string? GetPhysicalPath(string path) => null;
    }

    [Fact]
    public void WinFspDriver_Proxy_DeepCoverage()
    {
        var factory = new MockNativeHostFactory();
        var driver = new WinFspDriver(factory);
        var mockFs = new MockFileSystem();
        
        driver.Mount("V:", mockFs);
        var proxy = factory.LastFs!;
        var proxyType = proxy.GetType();

        // 1. GetSecurityByName
        object[] args1 = { "test.txt", 0u, new byte[0] };
        var res1 = (int)proxyType.GetMethod("GetSecurityByName")!.Invoke(proxy, args1)!;
        Assert.Equal(0, res1); // STATUS_SUCCESS

        // 2. Open
        object[] args2 = { "test.txt", 0u, 0u, null!, null!, null!, null! };
        var res2 = (int)proxyType.GetMethod("Open")!.Invoke(proxy, args2)!;
        Assert.Equal(0, res2);
        var fileNode = args2[3];

        // 3. GetFileInfo
        object[] args3 = { fileNode, null!, null! };
        var res3 = (int)proxyType.GetMethod("GetFileInfo")!.Invoke(proxy, args3)!;
        Assert.Equal(0, res3);

        // 4. ReadDirectoryEntry
        object context = null!;
        object[] args4 = { fileNode, null!, "*", null!, context, null!, null! };
        var res4 = (bool)proxyType.GetMethod("ReadDirectoryEntry")!.Invoke(proxy, args4)!;
        Assert.True(res4);

        // 5. Read
        IntPtr buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(10);
        object[] args5 = { fileNode, null!, buffer, 0UL, 10u, 0u };
        var res5 = (int)proxyType.GetMethod("Read")!.Invoke(proxy, args5)!;
        Assert.Equal(0, res5);
        System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);

        // 6. Cleanup & Close
        proxyType.GetMethod("Cleanup")!.Invoke(proxy, new[] { fileNode, null!, "test.txt", 0u });
        proxyType.GetMethod("Close")!.Invoke(proxy, new[] { fileNode, null! });

        driver.Unmount();
    }

    [Fact]
    public void WinFspDriver_Mount_HandlesFailure()
    {
        var factory = new MockNativeHostFactory();
        factory.LastHost.MountResult = -1;
        var driver = new WinFspDriver(factory);
        Assert.ThrowsAny<Exception>(() => driver.Mount("V:", new MockFileSystem()));
    }
}




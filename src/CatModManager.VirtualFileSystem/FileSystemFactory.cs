using System;
using System.Runtime.InteropServices;

namespace CatModManager.VirtualFileSystem;

public static class FileSystemFactory
{
    public static IFileSystemDriver CreateDriver()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new CatModManager.VirtualFileSystem.Windows.WinFspDriver();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new CatModManager.VirtualFileSystem.Linux.FuseDriver();
        }
        
        throw new PlatformNotSupportedException("No file system driver available for this platform.");
    }
}




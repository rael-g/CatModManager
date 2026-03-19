using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CatModManager.Core.Services;

public interface IRegistryService
{
    object? GetValue(string keyPath, string? valueName);
    bool KeyExists(RegistryHive hive, string subKey);
}

public class WindowsRegistryService : IRegistryService
{
    public object? GetValue(string keyPath, string? valueName) => Registry.GetValue(keyPath, valueName ?? "", null);
    
    public bool KeyExists(RegistryHive hive, string subKey)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey);
            return key != null;
        }
        catch { return false; }
    }
}

public interface IDriverService
{
    bool IsDriverInstalled();
}

public class WinFspDriverService : IDriverService
{
    private readonly IRegistryService _registry;

    public WinFspDriverService() : this(new WindowsRegistryService()) { }

    public WinFspDriverService(IRegistryService registry)
    {
        _registry = registry;
    }

    public bool IsDriverInstalled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

        try 
        {
            if (_registry.KeyExists(RegistryHive.LocalMachine, @"SOFTWARE\WinFsp")) return true;
            if (_registry.KeyExists(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\WinFsp")) return true;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WINFSP_DIR"))) return true;
            
            string sysPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (File.Exists(Path.Combine(sysPath, "winfsp-ms64.dll")) || File.Exists(Path.Combine(sysPath, "winfsp-x64.dll"))) return true;
            
            return false;
        } 
        catch { return false; }
    }
}




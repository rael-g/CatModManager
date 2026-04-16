namespace CatModManager.Core.Services;

/// <summary>
/// Abstraction for Windows Registry access to enable unit testing of platform scanners.
/// </summary>
public interface IRegistryService
{
    string? GetCurrentUserValue(string keyPath, string valueName);
    string? GetLocalMachineValue(string keyPath, string valueName);
    string[] GetLocalMachineSubKeys(string keyPath);
    string? GetLocalMachineSubKeyValue(string keyPath, string subKeyName, string valueName);
}

public class WindowsRegistryService : IRegistryService
{
    public string? GetCurrentUserValue(string keyPath, string valueName)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(valueName) as string;
    }

    public string? GetLocalMachineValue(string keyPath, string valueName)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
        return key?.GetValue(valueName) as string;
    }

    public string[] GetLocalMachineSubKeys(string keyPath)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
        return key?.GetSubKeyNames() ?? System.Array.Empty<string>();
    }

    public string? GetLocalMachineSubKeyValue(string keyPath, string subKeyName, string valueName)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
        using var sub = key?.OpenSubKey(subKeyName);
        return sub?.GetValue(valueName) as string;
    }
}

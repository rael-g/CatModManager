using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CatModManager.Core.Services;

/// <summary>
/// Abstraction for starting and querying system processes, enabling unit testing of ProcessService.
/// </summary>
public interface IProcessRunner
{
    Task<bool> StartAsync(ProcessStartInfo info);
    Process[] GetProcesses();
    Task WaitForExitAsync(Process process);
    string? GetMainModuleFileName(Process process);
}

public class DefaultProcessRunner : IProcessRunner
{
    public async Task<bool> StartAsync(ProcessStartInfo info)
    {
        var p = Process.Start(info);
        if (p == null) return false;
        await p.WaitForExitAsync();
        return true;
    }

    public Process[] GetProcesses() => Process.GetProcesses();

    public Task WaitForExitAsync(Process process) => process.WaitForExitAsync();

    public string? GetMainModuleFileName(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; }
    }
}

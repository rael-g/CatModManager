using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CatModManager.Core.Services;

/// <summary>
/// Abstraction for starting and querying system processes, enabling unit testing of ProcessService.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Starts the process and returns it, or throws when it cannot be started.
    ///
    /// Replaced a <c>Task&lt;bool&gt; StartAsync</c> that waited for the process to exit and then
    /// reported success on the strength of <c>Process.Start</c> having returned non-null. Both
    /// halves were wrong: waiting meant an external tool held the caller for as long as the user
    /// kept it open, and ignoring the exit meant a launcher that failed still reported success.
    /// Whether to wait is the caller's decision, and it is made in ProcessService.
    /// </summary>
    Process? Start(ProcessStartInfo info);

    Process[] GetProcesses();
    Task WaitForExitAsync(Process process);
    string? GetMainModuleFileName(Process process);
}

public class DefaultProcessRunner : IProcessRunner
{
    public Process? Start(ProcessStartInfo info) => Process.Start(info);

    public Process[] GetProcesses() => Process.GetProcesses();

    public Task WaitForExitAsync(Process process) => process.WaitForExitAsync();

    public string? GetMainModuleFileName(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; }
    }
}

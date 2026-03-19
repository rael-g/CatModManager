using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace CatModManager.Core.Services;

public class ProcessService : IProcessService
{
    private readonly ILogService _logService;

    public ProcessService(ILogService logService)
    {
        _logService = logService;
    }

    public async Task<bool> StartProcessAsync(string filePath, string arguments, bool runAsAdmin)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = filePath,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = runAsAdmin ? "runas" : ""
            };

            var process = Process.Start(info);
            if (process == null) return false;

            await process.WaitForExitAsync();

            // The launched exe may be a thin launcher that spawns the real game process
            // and exits early (common with Steam/UE5 games). Wait for any remaining
            // processes running from the same game directory before returning.
            await WaitForGameDirectoryProcesses(filePath);

            return true;
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to start process: {filePath}", ex);
            return false;
        }
    }

    private async Task WaitForGameDirectoryProcesses(string launcherPath)
    {
        var gameDir = Path.GetDirectoryName(Path.GetFullPath(launcherPath));
        if (string.IsNullOrEmpty(gameDir)) return;

        var prefix = gameDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        // Poll for up to 30 seconds after the launcher exits. This covers cases where
        // the launcher shows a Steam dialog that the user must dismiss before the real
        // game process starts.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(2000);
            var children = GetProcessesInDirectory(prefix);
            if (children.Length > 0)
            {
                _logService.Log($"Detected {children.Length} game process(es); waiting for exit...");
                await Task.WhenAll(children.Select(p => p.WaitForExitAsync()));
                return;
            }
        }
        // No child process appeared — the launcher was the game itself; already done.
    }

    private static Process[] GetProcessesInDirectory(string prefix)
    {
        // Filter to the current user session to skip system/service processes (session 0),
        // which always deny MainModule access and would generate spurious exceptions.
        int userSession = Process.GetCurrentProcess().SessionId;
        var result = new System.Collections.Generic.List<Process>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.SessionId != userSession) continue;
                var exe = proc.MainModule?.FileName;
                if (exe != null && exe.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    result.Add(proc);
            }
            catch { /* process exited or access denied — skip */ }
        }
        return result.ToArray();
    }

    public Task OpenFolderAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return Task.CompletedTask;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start("explorer.exe", path);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", path);
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to open folder: {path}", ex);
        }
        return Task.CompletedTask;
    }
}
